# ---------------------------------------------------------------------------
# ECS one-off migration task (Task 24.12 / Requirement 11.4)
# ---------------------------------------------------------------------------
# EF Core migrations are applied by a SEPARATE, short-lived Fargate task that
# runs the SAME API image with RUN_MIGRATIONS=true. Program.cs's migrate-and-
# exit path (step 8b) calls Database.Migrate() to apply every pending migration
# in ascending order against RDS, then exits 0 WITHOUT starting Kestrel.
#
# WHY NOT `dotnet ef database update`:
#   The lean runtime image (mcr.microsoft.com/dotnet/aspnet:8.0) does not ship
#   the EF Core CLI tooling, and we deliberately keep it out of the production
#   image. Driving migrations through the app itself (RUN_MIGRATIONS=true) reuses
#   the already-shipped assemblies and the exact same connection string, task
#   roles, and SSM secrets as the running service — no extra tooling, no drift.
#
# DEPLOYMENT ORDERING (Requirement 11.4):
#   The pipeline runs this one-off task and WAITS for it to exit 0 BEFORE it
#   updates the API service to the new task definition. If the migration task
#   fails, the service is NOT promoted and the previous revision keeps serving.
#   See infra/scripts/run-migrations.sh for the exact run-task + wait sequence.
#
# This task definition reuses:
#   * the same ECR image (var.api_image_tag) as the API service,
#   * the same execution + task roles (iam.tf),
#   * the DB connection string SSM SecureString (ssm.tf),
#   * a dedicated /nnm/migrate CloudWatch log group for clean separation.
# ---------------------------------------------------------------------------

# --- CloudWatch log group ---------------------------------------------------
# Migration task stdout/stderr (the Database.Migrate() progress lines Program.cs
# logs) lands here, kept separate from the long-running API logs.
resource "aws_cloudwatch_log_group" "migrate" {
  name              = "/nnm/migrate"
  retention_in_days = var.api_log_retention_days

  tags = merge(
    local.common_tags,
    {
      Name = "${local.name_prefix}-migrate-logs"
    },
  )
}

# --- Migration task definition ----------------------------------------------
# Single "migrate" container, Fargate/awsvpc. Identical image/roles/secret to
# the API task, but with RUN_MIGRATIONS=true so the container applies pending
# migrations and exits instead of serving HTTP.
resource "aws_ecs_task_definition" "migrate" {
  family                   = "${local.name_prefix}-migrate"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = var.api_task_cpu
  memory                   = var.api_task_memory

  # Same roles as the API task: the execution role pulls the image, writes logs
  # and injects the DB secret; the task role is the app's runtime identity.
  execution_role_arn = aws_iam_role.api_task_execution.arn
  task_role_arn      = aws_iam_role.api_task.arn

  container_definitions = jsonencode([
    {
      name      = "migrate"
      image     = "${aws_ecr_repository.api.repository_url}:${var.api_image_tag}"
      essential = true

      # No portMappings — this task never listens; it runs to completion.

      # RUN_MIGRATIONS=true triggers Program.cs's migrate-and-exit path.
      # USE_SSM=true so the app can still load any /nnm/* parameters it needs,
      # mirroring the API service. ASPNETCORE_ENVIRONMENT=Production matches the
      # service so configuration resolves identically.
      environment = [
        { name = "ASPNETCORE_ENVIRONMENT", value = "Production" },
        { name = "RUN_MIGRATIONS", value = "true" },
        { name = "USE_SSM", value = "true" },
      ]

      # The migration only needs the database connection string. It is injected
      # from the same SSM SecureString the API service uses, and mapped to
      # Database:ConnectionString by MapEnvironmentVariables() in Program.cs.
      secrets = [
        { name = "DB_CONNECTION_STRING", valueFrom = local.ssm_parameter_arns.db_connection_string },
      ]

      logConfiguration = {
        logDriver = "awslogs"
        options = {
          "awslogs-group"         = aws_cloudwatch_log_group.migrate.name
          "awslogs-region"        = var.aws_region
          "awslogs-stream-prefix" = "migrate"
        }
      }
    },
  ])

  tags = merge(
    local.common_tags,
    {
      Name = "${local.name_prefix}-migrate-task"
    },
  )
}

# --- Outputs ----------------------------------------------------------------
# Consumed by the deployment pipeline / run-migrations.sh to launch the one-off
# task with `aws ecs run-task` before promoting the API service.

output "ecs_migrate_task_definition_arn" {
  description = "ARN of the one-off EF Core migration task definition. Run this task and wait for exit 0 before updating the API service (Requirement 11.4)."
  value       = aws_ecs_task_definition.migrate.arn
}

output "ecs_migrate_task_family" {
  description = "Family name of the migration task definition (used by `aws ecs run-task --task-definition`)."
  value       = aws_ecs_task_definition.migrate.family
}

output "ecs_migrate_log_group_name" {
  description = "CloudWatch log group receiving migration task output."
  value       = aws_cloudwatch_log_group.migrate.name
}

# Networking identifiers the one-off migration run-task needs for its
# --network-configuration (the task runs in the private subnets behind the ECS
# security group, exactly like the API service tasks).
output "ecs_security_group_id" {
  description = "ID of the ECS tasks security group (egress to RDS + HTTPS). Used in the migration run-task network configuration."
  value       = aws_security_group.ecs.id
}
