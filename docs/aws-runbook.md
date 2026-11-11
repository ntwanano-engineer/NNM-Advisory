# NNM Advisory — AWS Deployment Runbook

Operational guide for deploying, updating, troubleshooting, and rolling back the NNM Advisory website on AWS. It assumes no prior knowledge of this specific infrastructure; every command below lists its **exact syntax**, the **expected output on success**, and the **expected output on failure** so an operator can tell the two apart.

> **Satisfies Requirement 14** (14.1–14.7): distinct sections for Initial Deployment, Environment Variables and Secrets, Database Migrations, Update Deployment, Rollback, and Troubleshooting (≥ 5 scenarios).

---

## 0. Conventions and Prerequisites

The infrastructure is defined as Terraform in `infra/terraform/` and provisions: VPC + public/private subnets, security groups, RDS PostgreSQL (private), two private S3 buckets (frontend static assets + leadership images) with CloudFront Origin Access Control (OAC), ACM certificates (CloudFront cert in **us-east-1**, ALB cert in the deploy region), an internet-facing ALB (HTTPS:443 only), an ECS Fargate cluster/service/task definition, an ECR repository, SSM SecureString parameters under `/nnm/*`, IAM task + execution roles, Route 53 alias records, and CloudWatch alarms + SNS + ECS auto-scaling.

| Setting | Value |
| --- | --- |
| AWS region (primary) | `af-south-1` (Terraform `var.aws_region`) |
| CloudFront ACM region | `us-east-1` (CloudFront requires this) |
| Project / environment | `nnm-advisory` / `prod` → `name_prefix = nnm-advisory-prod` |
| Terraform working dir | `infra/terraform/` |
| API container | built from `api/Dockerfile`, listens on port **8080** |
| Apex / www / api hosts | `nnmadvisory.com` / `www.nnmadvisory.com` / `api.nnmadvisory.com` |

### Required tooling

| Tool | Minimum version | Verify command | Success output | Failure output |
| --- | --- | --- | --- | --- |
| AWS CLI | v2 | `aws --version` | `aws-cli/2.x.x Python/3.x ...` | `aws : The term 'aws' is not recognized` (not installed) |
| Terraform | 1.5+ | `terraform version` | `Terraform v1.x.x` | `terraform : command not found` |
| Docker | 24+ | `docker --version` | `Docker version 24.x.x, build ...` | `error during connect: ... docker daemon is not running` |
| .NET SDK | 8.0 | `dotnet --version` | `8.0.xxx` | `Command 'dotnet' not found` |
| Node.js | 18+ | `node --version` | `v18.x.x` or later | `node : command not found` |

### Authenticate the AWS CLI (run once per shell)

```bash
aws sts get-caller-identity --output json
```

- **Success output:**
  ```json
  { "UserId": "AIDA...", "Account": "123456789012", "Arn": "arn:aws:iam::123456789012:user/deployer" }
  ```
- **Failure output:** `Unable to locate credentials. You can configure credentials by running "aws configure".` — run `aws configure` (or export `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` / `AWS_DEFAULT_REGION`) and retry.

### Convenience: capture Terraform outputs as shell variables

Run these from `infra/terraform/` after the stack exists. They are reused throughout the runbook.

```bash
ACCOUNT_ID=$(aws sts get-caller-identity --query Account --output text)
REGION=af-south-1
ECR_URL=$(terraform output -raw ecr_repository_url)
CLUSTER=$(terraform output -raw ecs_cluster_name)
SERVICE=$(terraform output -raw ecs_service_name)
TASK_FAMILY=nnm-advisory-prod-api
LOG_GROUP=$(terraform output -raw log_group_name)
CF_DIST_ID=$(terraform output -raw cloudfront_distribution_id)
FRONTEND_BUCKET=$(terraform output -raw frontend_bucket_id)
RDS_ADDRESS=$(terraform output -raw rds_address)
```

- **Success output:** no output per line; variables are populated (verify with `echo $ECR_URL` → `123456789012.dkr.ecr.af-south-1.amazonaws.com/nnm-advisory-prod-api`).
- **Failure output:** `No outputs found` or `Output "ecr_repository_url" not found` — the stack is not applied yet; complete **Section 1** first.

---

## 1. Initial Deployment

Provisions the full stack from an empty AWS account. Each step names the **service**, the **action**, and the **observable outcome** that confirms success.

### Step 1.1 — Supply secrets to Terraform (no secrets in source control)

Two variables have no default and must be supplied out-of-band: `db_password` (RDS master password) and `jwt_secret` (JWT signing key, 32+ chars). Export them as `TF_VAR_*`:

```bash
export TF_VAR_db_password='<strong-db-password>'
export TF_VAR_jwt_secret='<random-32+-char-secret>'
```

- **Observable outcome:** `echo ${TF_VAR_db_password:+set}` prints `set`.
- **Failure output (if omitted):** later `terraform apply` fails with `No value for required variable ... "db_password"`.

### Step 1.2 — Initialize Terraform (service: Terraform backend / providers)

```bash
terraform -chdir=infra/terraform init
```

- **Success output:** `Terraform has been successfully initialized!`
- **Failure output:** `Error: Failed to query available provider packages` or `Backend initialization required` — check network access and backend config, then re-run.

### Step 1.3 — Review the plan (action: dry-run all resources)

```bash
terraform -chdir=infra/terraform plan -out=tfplan
```

- **Success output:** ends with `Plan: NN to add, 0 to change, 0 to destroy.` and `Saved the plan to: tfplan`.
- **Failure output:** `Error: Invalid value for variable` or `Error: error configuring ... provider` — fix the reported variable/credential and re-run.

### Step 1.4 — Decide ACM/DNS validation ordering

ACM certificates (CloudFront in us-east-1 + ALB regional) are validated via DNS records written into the Route 53 hosted zone. If the hosted zone for `nnmadvisory.com` **does not yet exist**, first apply with `-var create_dns_validation=false` so the certs are created in `PENDING_VALIDATION`, create/delegate the zone, then re-apply with the default `create_dns_validation=true` to write the validation records.

```bash
terraform -chdir=infra/terraform apply -var create_dns_validation=false -auto-approve
```

- **Observable outcome:** `Apply complete!` and `aws acm list-certificates` shows the two certs with `Status=PENDING_VALIDATION`.
- **Failure output:** `Error: creating ACM Certificate ... InvalidParameter` — confirm the domain names in `var.domain_name` / `var.www_subdomain` / `var.api_subdomain` are correct.

### Step 1.5 — Apply the full stack (services: VPC, SG, S3, RDS, ACM, ALB, ECS, CloudFront, Route 53, SSM, IAM, CloudWatch)

```bash
terraform -chdir=infra/terraform apply tfplan
```

- **Success output:** `Apply complete! Resources: NN added, 0 changed, 0 destroyed.` followed by the `Outputs:` block (`cloudfront_distribution_id`, `ecr_repository_url`, `rds_endpoint`, etc.).
- **Failure output:** `Error: creating ... : AccessDenied` (IAM permissions), or `Error: waiting for RDS DB Instance ... creation: timeout` — see Troubleshooting. RDS creation alone takes ~10–15 minutes; ACM validation can take several minutes while DNS propagates.

Per-service outcomes to confirm after apply:

| Service | Action performed | Observable outcome to confirm |
| --- | --- | --- |
| **S3** | Create `*-frontend` + `*-images` private buckets | `aws s3 ls \| findstr nnm-advisory-prod` lists both buckets; neither is public |
| **ACM** | Issue CloudFront (us-east-1) + ALB (regional) certs | `aws acm list-certificates --region us-east-1` and `--region af-south-1` show `Status=ISSUED` |
| **RDS** | Create private PostgreSQL 14 instance | `aws rds describe-db-instances --db-instance-identifier nnm-advisory-prod-db --query "DBInstances[0].DBInstanceStatus"` → `"available"` |
| **ECS** | Create cluster, task def, service (desired 1) | `aws ecs describe-services --cluster $CLUSTER --services $SERVICE --query "services[0].runningCount"` → `1` |
| **CloudFront** | Create distribution over OAC S3 origin | `aws cloudfront get-distribution --id $CF_DIST_ID --query "Distribution.Status"` → `"Deployed"` |

### Step 1.6 — Store runtime secrets (service: SSM Parameter Store)

The six `/nnm/*` SecureString parameters are created automatically by `ssm.tf` during apply (DB connection string, JWT secret, SES sender/admin emails, images bucket name, CORS origin). Confirm them:

```bash
aws ssm get-parameters-by-path --path /nnm --recursive --query "Parameters[].Name" --output table
```

- **Success output:** a table listing `/nnm/db/connection-string`, `/nnm/jwt/secret`, `/nnm/ses/sender-email`, `/nnm/ses/admin-email`, `/nnm/s3/images-bucket`, `/nnm/cors/allowed-origin`.
- **Failure output:** empty table or `AccessDeniedException` — see Troubleshooting (SSM access denied). See **Section 2** for the full variable map.

### Step 1.7 — Build and push the first API image (services: ECR + Docker)

Covered in detail in **Section 4** (Update Deployment). On first deploy the ECS service launches with the `latest` tag placeholder; push a real image, then force a new deployment so a healthy task registers with the ALB target group.

### Step 1.8 — Run the initial database migration (service: ECS run-task / RDS)

See **Section 3**. This creates the schema in the empty `nnmadvisory` database before the API serves traffic.

### Step 1.9 — Publish the frontend SPA (services: S3 + CloudFront)

```bash
npm --prefix frontend ci
npm --prefix frontend run build
aws s3 sync frontend/dist "s3://$FRONTEND_BUCKET" --delete
aws cloudfront create-invalidation --distribution-id $CF_DIST_ID --paths "/*"
```

- **Success output:** `npm run build` ends with `built in N.NNs`; `aws s3 sync` prints `upload: frontend/dist/... to s3://.../...` lines; the invalidation command returns JSON with `"Status": "InProgress"`.
- **Failure output:** `The user-provided path frontend/dist does not exist` (build did not run), or `An error occurred (AccessDenied) when calling the PutObject operation` (credentials lack `s3:PutObject` on the frontend bucket).

### Step 1.10 — End-to-end smoke test

```bash
curl -s -o /dev/null -w "%{http_code}\n" https://api.nnmadvisory.com/health
curl -s -o /dev/null -w "%{http_code}\n" https://www.nnmadvisory.com/
```

- **Success output:** `200` for both.
- **Failure output:** `000` (DNS/TLS not resolving — ACM or Route 53 not ready), `502`/`503` (no healthy ECS task — see Troubleshooting), or `403`/`404` from CloudFront (frontend not yet uploaded).

---

## 2. Environment Variables and Secrets

The API reads flat environment variable names (mapped in `Program.cs`). At runtime the ECS task definition injects the **secrets** from SSM SecureString parameters and sets the non-secret **environment** values directly. **Every secret value lives in SSM Parameter Store; nothing sensitive is baked into the image or committed.**

### 2.1 — Variables required for the application to start

| Env var (container) | Required to start | Stored in / sourced from | SSM parameter name |
| --- | --- | --- | --- |
| `DB_CONNECTION_STRING` | Yes | SSM SecureString (assembled from RDS + db creds) | `/nnm/db/connection-string` |
| `JWT_SECRET` | Yes | SSM SecureString (from `TF_VAR_jwt_secret`) | `/nnm/jwt/secret` |
| `SENDER_EMAIL` | Yes | SSM SecureString | `/nnm/ses/sender-email` |
| `ADMIN_EMAIL` | Yes | SSM SecureString | `/nnm/ses/admin-email` |
| `S3_IMAGES_BUCKET` | Yes | SSM SecureString (images bucket name) | `/nnm/s3/images-bucket` |
| `CORS_ALLOWED_ORIGIN` | Yes | SSM SecureString | `/nnm/cors/allowed-origin` |
| `ASPNETCORE_ENVIRONMENT` | Yes (non-secret) | ECS task def `environment` → `Production` | n/a |
| `ASPNETCORE_URLS` | Yes (non-secret) | ECS task def `environment` → `http://+:8080` | n/a |
| `EMAIL_PROVIDER` | Yes (non-secret) | ECS task def `environment` → `ses` | n/a |
| `IMAGE_STORAGE_PROVIDER` | Yes (non-secret) | ECS task def `environment` → `s3` | n/a |

The SecureString ARNs are wired into the task definition `secrets` block, and the ECS **execution role** is granted `ssm:GetParameters` + `kms:Decrypt` so the agent can inject them at task start.

### 2.2 — Exact configuration sequence BEFORE launch

1. Export `TF_VAR_db_password` and `TF_VAR_jwt_secret` (Step 1.1). These feed `/nnm/db/connection-string` and `/nnm/jwt/secret`.
2. (Optional) Override email/CORS defaults by setting `TF_VAR_ses_sender_email`, `TF_VAR_ses_admin_email`, `TF_VAR_cors_allowed_origin`. When empty they default to `noreply@`, `admin@`, and `https://` + the apex domain.
3. `terraform apply` creates all six SecureString parameters (Step 1.5/1.6).
4. Verify a parameter decrypts (requires `ssm:GetParameter` + `kms:Decrypt`):

   ```bash
   aws ssm get-parameter --name /nnm/jwt/secret --with-decryption --query "Parameter.Value" --output text
   ```

   - **Success output:** the decrypted secret value (handle carefully; do not log it).
   - **Failure output:** `An error occurred (ParameterNotFound)` (apply did not create it) or `AccessDeniedException` (missing KMS/SSM permission).
5. Only after all six parameters resolve should you push an image and start tasks (Sections 3–4). A task started before a required parameter exists will stop with `ResourceInitializationError: unable to pull secrets`.

### 2.3 — Rotating a secret after launch

```bash
aws ssm put-parameter --name /nnm/jwt/secret --type SecureString --value '<new-secret>' --overwrite
aws ecs update-service --cluster $CLUSTER --service $SERVICE --force-new-deployment
```

- **Success output:** `put-parameter` returns `{ "Version": 2, "Tier": "Standard" }`; `update-service` returns the service JSON with a new deployment. New tasks pick up the rotated value on start.
- **Failure output:** `ParameterNotFound` (wrong name) — SSM injection happens only at task start, so you must force a new deployment for a change to take effect.

---

## 3. Database Migrations

Migrations are EF Core migrations in `api/Migrations/` (e.g. `InitialSchema`). In production they run via a dedicated **migrate ECS task** — the same container image, overridden to run migrations and exit (gated by `RUN_MIGRATIONS=true`) — so the schema is applied against RDS from inside the private subnets without exposing the database publicly.

### 3.1 — Command to execute (run-task against the migrate configuration)

Run a one-off Fargate task in the private subnets with the migrate command override:

```bash
SUBNETS=$(terraform -chdir=infra/terraform output -json private_subnet_ids | tr -d '[]" ')
ECS_SG=$(aws ec2 describe-security-groups --filters "Name=group-name,Values=nnm-advisory-prod-ecs*" --query "SecurityGroups[0].GroupId" --output text)

aws ecs run-task \
  --cluster $CLUSTER \
  --launch-type FARGATE \
  --task-definition $TASK_FAMILY \
  --network-configuration "awsvpcConfiguration={subnets=[$SUBNETS],securityGroups=[$ECS_SG],assignPublicIp=DISABLED}" \
  --overrides '{"containerOverrides":[{"name":"api","environment":[{"name":"RUN_MIGRATIONS","value":"true"}]}]}' \
  --query "tasks[0].taskArn" --output text
```

- **Success output:** a task ARN, e.g. `arn:aws:ecs:af-south-1:123456789012:task/nnm-advisory-prod-cluster/abc123...`.
- **Failure output:** `An error occurred (InvalidParameterException) when calling the RunTask operation: ... network configuration` (bad subnet/SG), or `Unable to assume the service linked role` (ECS not initialized in the account).

Wait for the task to finish and capture its exit code:

```bash
TASK_ARN=<arn-from-above>
aws ecs wait tasks-stopped --cluster $CLUSTER --tasks $TASK_ARN
aws ecs describe-tasks --cluster $CLUSTER --tasks $TASK_ARN \
  --query "tasks[0].containers[0].exitCode"
```

- **Success output:** `0`.
- **Failure output:** `1` (migration error) or `137` (out of memory) — pull the logs (Step 3.2) to see the exception.

### 3.2 — Expected output indicating successful migration (CloudWatch logs)

The migrate task writes structured logs to the `/nnm/api` log group. Tail them:

```bash
aws logs tail $LOG_GROUP --since 10m --format short
```

- **Success output:** lines such as `Applying migration '20261101010559_InitialSchema'.` followed by `Done applying migrations.` and the container exiting with code `0`.
- **Failure output:** `Npgsql.NpgsqlException: ... Connection refused` (RDS unreachable — see Troubleshooting), `relation already exists` (migration state mismatch), or `An error occurred ... secrets` (DB connection string not injected).

### 3.3 — Verification step: confirm the schema version

EF Core records applied migrations in the `__EFMigrationsHistory` table. Run a verification query via a second one-off task (or psql from a bastion/private session). Using a verify override:

```bash
aws ecs run-task --cluster $CLUSTER --launch-type FARGATE --task-definition $TASK_FAMILY \
  --network-configuration "awsvpcConfiguration={subnets=[$SUBNETS],securityGroups=[$ECS_SG],assignPublicIp=DISABLED}" \
  --overrides '{"containerOverrides":[{"name":"api","command":["dotnet","NnmAdvisory.Api.dll","--list-migrations"]}]}' \
  --query "tasks[0].taskArn" --output text
```

Then inspect the logs:

```bash
aws logs tail $LOG_GROUP --since 5m --format short
```

- **Success output:** the applied-migrations list ends with the latest expected migration, e.g. `20261101010559_InitialSchema (applied)`, and no `Pending` migrations are listed.
- **Failure output:** output shows the migration as `pending`/not applied, or a connection exception — do **not** proceed to serve traffic; re-run Step 3.1 and resolve the error first.

> If a direct SQL check is preferred from inside the VPC: `SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 1;` should return the latest migration id. A `relation "__EFMigrationsHistory" does not exist` result means migrations never ran.

---

## 4. Update Deployment

Ships a new API version with a zero-downtime rolling deployment (service runs `deployment_minimum_healthy_percent=100`, `deployment_maximum_percent=200`). For a frontend-only change, skip to Step 4.6.

### Step 4.1 — Authenticate Docker to ECR (service: ECR)

```bash
aws ecr get-login-password --region $REGION | docker login --username AWS --password-stdin "$ACCOUNT_ID.dkr.ecr.$REGION.amazonaws.com"
```

- **Success output:** `Login Succeeded`.
- **Failure output:** `Error response from daemon: Get "https://...": denied` (bad credentials) or `Cannot perform an interactive login from a non TTY device` (pipe missing `--password-stdin`).

### Step 4.2 — Build the image (service: Docker, from `api/Dockerfile`)

Use an immutable tag — the short git SHA — so each build is traceable and rollback targets a specific revision.

```bash
IMAGE_TAG=$(git rev-parse --short HEAD)
docker build -t "nnm-advisory-api:$IMAGE_TAG" ./api
```

- **Success output:** ends with `naming to docker.io/library/nnm-advisory-api:<tag>` / `FINISHED`.
- **Failure output:** `ERROR [build N/N] RUN dotnet publish ...` — the .NET build failed; fix the code/build error before continuing.

### Step 4.3 — Tag and push to ECR

```bash
docker tag "nnm-advisory-api:$IMAGE_TAG" "$ECR_URL:$IMAGE_TAG"
docker push "$ECR_URL:$IMAGE_TAG"
```

- **Success output:** layer `Pushed` lines ending with `$IMAGE_TAG: digest: sha256:... size: ...`.
- **Failure output:** `denied: Your authorization token has expired` (re-run Step 4.1) or `name unknown: The repository ... does not exist` (wrong `$ECR_URL` / stack not applied).

### Step 4.4 — Register a new task definition revision pointing at the new image (service: ECS)

Derive a new revision from the current one with the image swapped:

```bash
aws ecs describe-task-definition --task-definition $TASK_FAMILY \
  --query "taskDefinition" --output json > taskdef.json

# Update the image to the new tag and strip read-only fields, then register.
# (jq shown; any JSON edit that sets containerDefinitions[0].image works.)
jq --arg IMG "$ECR_URL:$IMAGE_TAG" '
  .containerDefinitions[0].image = $IMG
  | del(.taskDefinitionArn,.revision,.status,.requiresAttributes,.compatibilities,.registeredAt,.registeredBy)
' taskdef.json > taskdef-new.json

aws ecs register-task-definition --cli-input-json file://taskdef-new.json \
  --query "taskDefinition.revision" --output text
```

- **Success output:** the new revision number, e.g. `8`.
- **Failure output:** `An error occurred (ClientException) when calling the RegisterTaskDefinition operation: Container.image should not be null` (image field not set) — fix the JSON edit and retry.

### Step 4.5 — Trigger the rolling deployment and wait for stability

```bash
aws ecs update-service --cluster $CLUSTER --service $SERVICE \
  --task-definition "$TASK_FAMILY:8" --query "service.deployments[0].status" --output text
aws ecs wait services-stable --cluster $CLUSTER --services $SERVICE
```

Replace `8` with the revision from Step 4.4. (To redeploy the current revision instead — e.g. after pushing a new `latest` — use `--force-new-deployment`.)

- **Success output:** `update-service` prints `PRIMARY`; `wait services-stable` returns with no output once the new task is healthy and the old one is drained.
- **Failure output:** `wait services-stable` eventually errors `Waiter ServicesStable failed: Max attempts exceeded` — the new revision never reached a steady state (tasks crash-looping or failing health checks); see Troubleshooting, then roll back (Section 5).

### Step 4.6 — Deploy frontend changes (services: S3 + CloudFront)

```bash
npm --prefix frontend ci
npm --prefix frontend run build
aws s3 sync frontend/dist "s3://$FRONTEND_BUCKET" --delete
aws cloudfront create-invalidation --distribution-id $CF_DIST_ID --paths "/*" --query "Invalidation.Status" --output text
```

- **Success output:** `aws s3 sync` shows `upload:`/`delete:` lines; the invalidation returns `InProgress` (becomes `Completed` within a few minutes).
- **Failure output:** `AccessDenied` on `PutObject` (credentials) or `NoSuchDistribution` (wrong `$CF_DIST_ID`).

### Step 4.7 — Verify the update

```bash
curl -s https://api.nnmadvisory.com/health
aws ecs describe-services --cluster $CLUSTER --services $SERVICE \
  --query "services[0].{running:runningCount,desired:desiredCount,taskDef:taskDefinition}" --output table
```

- **Success output:** `/health` returns `200`/healthy JSON; `running` equals `desired` and `taskDef` ends with the new revision (`:8`).
- **Failure output:** `running` < `desired`, or `taskDef` still showing the old revision — the deployment did not converge; investigate and roll back if needed.

---

## 5. Rollback Procedure

### 5.1 — Trigger condition (when to roll back)

Initiate rollback when **any** of these holds after an Update Deployment:

- `aws ecs wait services-stable` fails with `Max attempts exceeded` (new revision never stabilized).
- `runningCount` stays below `desiredCount` for more than ~10 minutes with tasks repeatedly stopping.
- `https://api.nnmadvisory.com/health` returns non-200 (502/503) or the ALB target group shows 0 healthy targets.
- A CloudWatch alarm (5xx rate / unhealthy host count) fires via SNS right after the deploy.

### 5.2 — Identify the previous known-good revision

```bash
aws ecs list-task-definitions --family-prefix $TASK_FAMILY --sort DESC \
  --query "taskDefinitionArns[:3]" --output table
```

- **Success output:** the three most recent revision ARNs, newest first — e.g. `...:api:8` (the failed one), `...:api:7` (previous known-good), `...:api:6`.
- **Failure output:** `An error occurred (ClientException) ... family ... not found` — wrong family name; confirm `$TASK_FAMILY`.

### 5.3 — Sequential rollback steps

1. Point the service back at the previous good revision (e.g. `7`):

   ```bash
   aws ecs update-service --cluster $CLUSTER --service $SERVICE \
     --task-definition "$TASK_FAMILY:7" --query "service.taskDefinition" --output text
   ```

   - **Success output:** the service JSON reports `...:api:7` as the active task definition.
   - **Failure output:** `InvalidParameterException: ... revision ... does not exist` — pick a revision that appears in Step 5.2.

2. Wait for the rollback to converge:

   ```bash
   aws ecs wait services-stable --cluster $CLUSTER --services $SERVICE
   ```

   - **Success output:** returns with no output (service stable on revision 7).
   - **Failure output:** `Max attempts exceeded` — the previous revision is also unhealthy (likely a data/secret/RDS issue, not the image); investigate the root cause in Troubleshooting before retrying.

3. (Frontend rollback, if the SPA was the regression) re-sync the previous build artifact and invalidate:

   ```bash
   aws s3 sync s3://$FRONTEND_BUCKET-backup/<good-build> "s3://$FRONTEND_BUCKET" --delete
   aws cloudfront create-invalidation --distribution-id $CF_DIST_ID --paths "/*"
   ```

   Alternatively restore prior object versions (the frontend bucket has versioning enabled).

### 5.4 — Verification step (confirm the previous version is serving traffic)

Rollback is complete **only** when all of the following pass:

```bash
aws ecs describe-services --cluster $CLUSTER --services $SERVICE \
  --query "services[0].{running:runningCount,desired:desiredCount,taskDef:taskDefinition}" --output table
curl -s -o /dev/null -w "%{http_code}\n" https://api.nnmadvisory.com/health
aws elbv2 describe-target-health --target-group-arn $(terraform -chdir=infra/terraform output -raw target_group_arn) \
  --query "TargetHealthDescriptions[].TargetHealth.State" --output text
```

- **Success output:** `taskDef` ends with the previous revision (`:7`); `running` equals `desired`; `/health` returns `200`; target health states are all `healthy`.
- **Failure output:** `taskDef` still showing the failed revision, `/health` ≠ 200, or target states `unhealthy`/`draining` — rollback is **not** complete; repeat Step 5.3 or escalate.

---

## 6. Troubleshooting

At least five common failure scenarios. Each lists the observable **symptom**, **diagnostic steps in order**, and the **remediation**.

### 6.1 — ECS tasks fail health checks / crash-loop

- **Symptom:** `runningCount` stays below `desiredCount`; tasks start then stop within ~1–2 minutes; `/health` returns 502/503; `wait services-stable` times out.
- **Diagnostic steps:**
  1. Inspect recent service events:
     ```bash
     aws ecs describe-services --cluster $CLUSTER --services $SERVICE --query "services[0].events[:5]" --output table
     ```
     (Success: event list; look for "unhealthy" or "unable to pull secrets".)
  2. Find the last stopped task and its stop reason:
     ```bash
     STOPPED=$(aws ecs list-tasks --cluster $CLUSTER --desired-status STOPPED --query "taskArns[0]" --output text)
     aws ecs describe-tasks --cluster $CLUSTER --tasks $STOPPED --query "tasks[0].stoppedReason" --output text
     ```
  3. Read the container logs: `aws logs tail $LOG_GROUP --since 15m --format short`.
- **Remediation:** If the stop reason is `ResourceInitializationError: unable to pull secrets`, the task role/parameter is missing — fix SSM (6.5). If logs show an unhandled startup exception (bad connection string, missing env var), correct the SSM value and `--force-new-deployment`. If `/health` returns non-200 only under the ALB, confirm the container binds `http://+:8080` (`ASPNETCORE_URLS`) and the target group path is `/health`. If the new image is faulty, **roll back** (Section 5).

### 6.2 — RDS connection failures from the API

- **Symptom:** Logs show `Npgsql.NpgsqlException: ... Connection refused` / `timeout`; API 500s on data endpoints; migrate task exits non-zero.
- **Diagnostic steps:**
  1. Confirm the instance is available:
     ```bash
     aws rds describe-db-instances --db-instance-identifier nnm-advisory-prod-db --query "DBInstances[0].DBInstanceStatus" --output text
     ```
     (Success: `available`.)
  2. Verify the security group rule: `sg-rds` must allow inbound TCP 5432 from `sg-ecs` only.
     ```bash
     aws ec2 describe-security-groups --filters "Name=group-name,Values=nnm-advisory-prod-rds*" --query "SecurityGroups[0].IpPermissions" --output json
     ```
  3. Confirm the connection string host matches the RDS endpoint: compare `terraform output rds_address` against `/nnm/db/connection-string`.
- **Remediation:** If status is not `available`, wait for it or check RDS events. If the SG lacks the 5432-from-sg-ecs rule, re-apply Terraform (`security_groups.tf` owns it). If the host/credentials in the SSM parameter are stale (e.g. after a DB recreate), re-apply so `ssm.tf` reassembles `/nnm/db/connection-string`, then `--force-new-deployment`.

### 6.3 — CloudFront returns 403 or 404 for the site

- **Symptom:** `https://www.nnmadvisory.com/` returns `403 Forbidden` (AccessDenied XML) or `404` for deep links.
- **Diagnostic steps:**
  1. Check whether objects exist in the frontend bucket: `aws s3 ls "s3://$FRONTEND_BUCKET/" --recursive | head`.
  2. Confirm the distribution is deployed: `aws cloudfront get-distribution --id $CF_DIST_ID --query "Distribution.Status" --output text` (Success: `Deployed`).
  3. Confirm the OAC bucket policy is attached: `aws s3api get-bucket-policy --bucket $FRONTEND_BUCKET`.
- **Remediation:** If the bucket is empty, run the frontend deploy (Step 4.6). Plain 403 AccessDenied from S3 means the OAC policy is missing/misscoped — re-apply Terraform (`cloudfront.tf` owns the OAC + bucket policy). Deep-link 404s should already be rewritten to `/index.html` (200) by the distribution's custom error responses; if not, confirm those `custom_error_response` blocks exist and re-apply, then invalidate `/*`.

### 6.4 — ACM certificate stuck in PENDING_VALIDATION

- **Symptom:** `terraform apply` hangs on `aws_acm_certificate_validation`; HTTPS to the site/API fails with a TLS error; `aws acm describe-certificate` shows `PENDING_VALIDATION`.
- **Diagnostic steps:**
  1. Check status (remember CloudFront cert is in **us-east-1**):
     ```bash
     aws acm list-certificates --region us-east-1 --query "CertificateSummaryList[].{D:DomainName,A:CertificateArn}" --output table
     aws acm describe-certificate --region us-east-1 --certificate-arn <arn> --query "Certificate.DomainValidationOptions" --output json
     ```
  2. Confirm the DNS validation CNAME exists in the hosted zone:
     ```bash
     aws route53 list-resource-record-sets --hosted-zone-id $(terraform -chdir=infra/terraform output -raw hosted_zone_id) --query "ResourceRecordSets[?Type=='CNAME']" --output table
     ```
- **Remediation:** If the hosted zone did not exist at apply time, the ordering in Step 1.4 was skipped — create/delegate the zone, then re-apply with `create_dns_validation=true` so the CNAMEs are written. Validation completes within minutes of correct DNS propagation. Verify nameserver delegation at the registrar if records exist but validation never flips to `ISSUED`.

### 6.5 — Image pull / ECR authentication errors

- **Symptom:** Task stops with `CannotPullContainerError: ... pull access denied` or `no basic auth credentials`; `docker push` returns `denied: Your authorization token has expired`.
- **Diagnostic steps:**
  1. Re-authenticate Docker to ECR (Step 4.1) — tokens expire after 12 hours.
  2. Confirm the image/tag exists in ECR:
     ```bash
     aws ecr describe-images --repository-name nnm-advisory-prod-api --query "imageDetails[].imageTags" --output json
     ```
  3. Confirm the task definition image matches a real tag (compare Step 4.4 image with the above).
- **Remediation:** For local `docker push` failures, re-run the `get-login-password | docker login` command. For ECS `CannotPullContainerError`, verify the **execution role** has `ecr:GetDownloadUrlForLayer`, `ecr:BatchGetImage`, `ecr:GetAuthorizationToken` (owned by `iam.tf`) and re-apply if missing; ensure the referenced tag actually exists in ECR, then `--force-new-deployment`.

### 6.6 — SSM parameter access denied / secret injection fails

- **Symptom:** Task stops with `ResourceInitializationError: unable to pull secrets or registry auth: ... AccessDeniedException` or `ParameterNotFound`.
- **Diagnostic steps:**
  1. Confirm all six parameters exist:
     ```bash
     aws ssm get-parameters-by-path --path /nnm --recursive --query "Parameters[].Name" --output table
     ```
  2. Confirm a parameter decrypts with your own perms:
     ```bash
     aws ssm get-parameter --name /nnm/db/connection-string --with-decryption --query "Parameter.Name" --output text
     ```
  3. Review the execution role policy for `ssm:GetParameters` + `kms:Decrypt` on `/nnm/*`.
- **Remediation:** If a parameter is missing, re-apply Terraform (`ssm.tf` creates them; ensure `TF_VAR_jwt_secret`/`TF_VAR_db_password` were set). If it exists but the task cannot read it, the execution role lacks `ssm:GetParameters`/`kms:Decrypt` scoped to the `/nnm/*` ARNs — re-apply `iam.tf`. After fixing, `--force-new-deployment` so new tasks re-inject secrets at start.

---

## Appendix A — Terraform output reference

| Output | Meaning | Used by |
| --- | --- | --- |
| `ecr_repository_url` | ECR repo for the API image | `docker tag`/`push`, task def image |
| `ecs_cluster_name` / `ecs_service_name` | ECS cluster + service names | `update-service`, `run-task`, `describe-services` |
| `ecs_task_definition_arn` | Active task definition revision | deploy / rollback |
| `log_group_name` | `/nnm/api` CloudWatch log group | `logs tail` during migrations/debug |
| `cloudfront_distribution_id` | Distribution id | cache invalidation |
| `frontend_bucket_id` | Frontend static assets bucket | `s3 sync` |
| `images_bucket_id` | Leadership images bucket | API pre-signed URLs |
| `rds_endpoint` / `rds_address` | RDS connection endpoint/host | connection string checks |
| `target_group_arn` | ALB target group | `describe-target-health` |
| `alb_dns_name` / `hosted_zone_id` | ALB DNS + hosted zone | DNS / Route 53 checks |
| `acm_cloudfront_certificate_arn` / `acm_alb_certificate_arn` | Issued cert ARNs | TLS troubleshooting |

## Appendix B — Quick command index

| Goal | Command |
| --- | --- |
| Who am I | `aws sts get-caller-identity` |
| Apply infra | `terraform -chdir=infra/terraform apply` |
| ECR login | `aws ecr get-login-password --region $REGION \| docker login --username AWS --password-stdin $ACCOUNT_ID.dkr.ecr.$REGION.amazonaws.com` |
| Push image | `docker push $ECR_URL:$IMAGE_TAG` |
| Roll out | `aws ecs update-service --cluster $CLUSTER --service $SERVICE --task-definition $TASK_FAMILY:<rev>` |
| Wait stable | `aws ecs wait services-stable --cluster $CLUSTER --services $SERVICE` |
| Run migration | `aws ecs run-task ... --overrides '{"containerOverrides":[{"name":"api","environment":[{"name":"RUN_MIGRATIONS","value":"true"}]}]}'` |
| Tail logs | `aws logs tail $LOG_GROUP --since 15m --format short` |
| Deploy frontend | `aws s3 sync frontend/dist s3://$FRONTEND_BUCKET --delete && aws cloudfront create-invalidation --distribution-id $CF_DIST_ID --paths "/*"` |
| Roll back | `aws ecs update-service --cluster $CLUSTER --service $SERVICE --task-definition $TASK_FAMILY:<previous-rev>` |
