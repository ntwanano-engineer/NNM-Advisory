#!/usr/bin/env bash
# ===========================================================================
# run-migrations.sh — apply EF Core migrations as a one-off ECS task
# (Task 24.12 / Requirement 11.4)
# ===========================================================================
# Runs the dedicated migration task definition (family "<prefix>-migrate",
# defined in infra/terraform/ecs_migrations.tf) as a single short-lived Fargate
# task. The task runs the SAME API image with RUN_MIGRATIONS=true, which makes
# Program.cs apply every pending migration in ascending order against RDS and
# then exit 0.
#
# This script is the deployment gate: it MUST run and the migration task MUST
# exit 0 BEFORE the API ECS service is updated to the new task definition. If
# the migration fails (non-zero container exit), this script exits non-zero and
# the caller MUST abort the promotion — the previous API revision keeps serving.
#
# It is CI-system agnostic: call it from any pipeline (GitHub Actions, GitLab
# CI, CodePipeline, a manual operator shell) after the new image is pushed to
# ECR and `terraform apply` has registered the new migration task revision.
#
# ---------------------------------------------------------------------------
# Typical deploy sequence (documented, not wired to a specific CI system):
#   1. docker build + push new immutable tag to ECR
#   2. terraform apply  (registers new migrate + api task definition revisions)
#   3. ./infra/scripts/run-migrations.sh      <-- THIS SCRIPT (the gate)
#   4. aws ecs update-service --force-new-deployment ...  (promote API) ONLY
#      if step 3 succeeded
# ---------------------------------------------------------------------------
#
# Required environment variables (resolve them from `terraform output`):
#   CLUSTER          ECS cluster name           (terraform output ecs_cluster_name)
#   MIGRATE_FAMILY   migration task family      (terraform output ecs_migrate_task_family)
#   SUBNETS          comma-separated private subnet IDs
#                                               (terraform output -json private_subnet_ids | jq -r 'join(",")')
#   SECURITY_GROUP   ECS tasks security group   (terraform output ecs_security_group_id)
# Optional:
#   AWS_REGION       AWS region (falls back to the CLI's configured region)
#
# Example:
#   export CLUSTER=$(terraform -chdir=infra/terraform output -raw ecs_cluster_name)
#   export MIGRATE_FAMILY=$(terraform -chdir=infra/terraform output -raw ecs_migrate_task_family)
#   export SUBNETS=$(terraform -chdir=infra/terraform output -json private_subnet_ids | jq -r 'join(",")')
#   export SECURITY_GROUP=$(terraform -chdir=infra/terraform output -raw ecs_security_group_id)
#   ./infra/scripts/run-migrations.sh
# ===========================================================================

set -euo pipefail

: "${CLUSTER:?CLUSTER (ECS cluster name) must be set}"
: "${MIGRATE_FAMILY:?MIGRATE_FAMILY (migration task definition family) must be set}"
: "${SUBNETS:?SUBNETS (comma-separated private subnet IDs) must be set}"
: "${SECURITY_GROUP:?SECURITY_GROUP (ECS tasks security group ID) must be set}"

REGION_ARG=()
if [[ -n "${AWS_REGION:-}" ]]; then
  REGION_ARG=(--region "${AWS_REGION}")
fi

echo "==> Launching one-off migration task (${MIGRATE_FAMILY}) on cluster ${CLUSTER}"

# Private subnets, no public IP — the task reaches RDS over the VPC exactly like
# the API service tasks. assign_public_ip is DISABLED; egress to ECR/SSM/CloudWatch
# goes through the NAT gateway.
TASK_ARN=$(aws ecs run-task \
  "${REGION_ARG[@]}" \
  --cluster "${CLUSTER}" \
  --task-definition "${MIGRATE_FAMILY}" \
  --launch-type FARGATE \
  --count 1 \
  --network-configuration "awsvpcConfiguration={subnets=[${SUBNETS}],securityGroups=[${SECURITY_GROUP}],assignPublicIp=DISABLED}" \
  --query 'tasks[0].taskArn' \
  --output text)

if [[ -z "${TASK_ARN}" || "${TASK_ARN}" == "None" ]]; then
  echo "ERROR: failed to start migration task" >&2
  exit 1
fi

echo "==> Migration task started: ${TASK_ARN}"
echo "==> Waiting for the task to stop..."

# Block until the task reaches STOPPED. (aws ecs wait polls for us.)
aws ecs wait tasks-stopped \
  "${REGION_ARG[@]}" \
  --cluster "${CLUSTER}" \
  --tasks "${TASK_ARN}"

# Inspect the container's exit code. Database.Migrate() succeeding → exit 0.
EXIT_CODE=$(aws ecs describe-tasks \
  "${REGION_ARG[@]}" \
  --cluster "${CLUSTER}" \
  --tasks "${TASK_ARN}" \
  --query 'tasks[0].containers[0].exitCode' \
  --output text)

STOP_REASON=$(aws ecs describe-tasks \
  "${REGION_ARG[@]}" \
  --cluster "${CLUSTER}" \
  --tasks "${TASK_ARN}" \
  --query 'tasks[0].stoppedReason' \
  --output text)

echo "==> Migration task stopped (exitCode=${EXIT_CODE}, reason='${STOP_REASON}')"
echo "    Logs: CloudWatch log group /nnm/migrate (stream prefix 'migrate')"

if [[ "${EXIT_CODE}" != "0" ]]; then
  echo "ERROR: migrations FAILED. Do NOT promote the new API revision." >&2
  echo "       Inspect /nnm/migrate logs, remediate, and re-run this script." >&2
  exit 1
fi

echo "==> Migrations applied successfully. Safe to promote the new API service revision."
