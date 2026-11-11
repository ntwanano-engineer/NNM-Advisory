# NNM Advisory — Infrastructure (Terraform)

Infrastructure-as-code for the NNM Advisory production environment, authored
with Terraform (AWS provider). This directory is the single Terraform root
module; each concern is split into its own `.tf` file and shares one provider
and variable set.

## Layout

| File           | Purpose                                                            |
|----------------|--------------------------------------------------------------------|
| `versions.tf`  | Terraform / AWS provider version pins and provider config.         |
| `variables.tf` | Input variables (region, project, CIDRs, tags).                    |
| `locals.tf`    | Naming prefix and common tags.                                     |
| `data.tf`      | Data sources (availability zones).                                 |
| `vpc.tf`       | VPC, subnets, IGW, NAT Gateway, route tables (Task 24.1).          |
| `outputs.tf`   | Exported IDs consumed by later tasks (SGs, RDS, ALB, ECS, etc.).   |

Subsequent infra tasks (24.2–24.12) add their own `.tf` files here and
reference the outputs above (e.g. `aws_vpc.main.id`, `aws_subnet.private[*].id`).

## Networking (Task 24.1)

| Layer            | CIDR          | Notes                            |
|------------------|---------------|----------------------------------|
| VPC              | `10.0.0.0/16` | DNS support & hostnames enabled  |
| Public Subnet A  | `10.0.1.0/24` | AZ-a — ALB, NAT Gateway          |
| Public Subnet B  | `10.0.2.0/24` | AZ-b — ALB (multi-AZ)            |
| Private Subnet A | `10.0.11.0/24`| AZ-a — ECS tasks, RDS primary    |
| Private Subnet B | `10.0.12.0/24`| AZ-b — ECS tasks, RDS standby    |

Public subnets route `0.0.0.0/0` to an Internet Gateway. Private subnets route
`0.0.0.0/0` to a NAT Gateway in the first public subnet for outbound-only
internet access.

## Usage

```bash
cp terraform.tfvars.example terraform.tfvars   # adjust values
terraform init
terraform fmt
terraform validate
terraform plan
terraform apply
```

> Remote state (S3 backend + DynamoDB lock) should be configured before
> running `apply` against a shared account. A local backend is used by default
> for authoring/validation.

## Configuration loading from Parameter Store (Task 24.12 / Req 13.6)

All sensitive runtime configuration lives as SecureString parameters under the
`/nnm/*` namespace (`ssm.tf`). The .NET API loads them two complementary ways:

1. **ECS `secrets` injection (primary).** The API task definition (`ecs.tf`)
   maps each `/nnm/*` parameter ARN to the flat env var the app expects
   (`DB_CONNECTION_STRING`, `JWT_SECRET`, ...). `Program.cs`'s
   `MapEnvironmentVariables()` re-maps those onto the nested settings keys
   (`Database:ConnectionString`, `Jwt:Secret`, ...).
2. **`AddSystemsManager("/nnm")` (in-app).** In AWS (when `USE_SSM=true` or the
   `Production` environment), `Program.cs` also attaches the SSM configuration
   provider for the `/nnm` subtree with a 5-minute reload. Because the SSM names
   (`/nnm/db/connection-string`) don't match the app's nested keys, a small
   in-app mapping (`MapSsmParameters`) re-maps the loaded keys onto
   `Database:ConnectionString` etc. Any value already present as a flat env var
   (from path 1) wins, so SSM only fills in what's otherwise unset.

Local development is unaffected: without `USE_SSM`/`Production`, the provider is
never attached, and the app uses `.env` / env vars as before.

## Database migrations deployment gate (Task 24.12 / Req 11.4)

EF Core migrations run as a **separate one-off Fargate task** before a new API
revision is promoted, so the schema is always migrated ahead of the code that
depends on it.

- `ecs_migrations.tf` defines task family `<prefix>-migrate`: the same image and
  roles as the API, but with `RUN_MIGRATIONS=true`. `Program.cs`'s migrate-and-
  exit path applies every pending migration in ascending order
  (`Database.Migrate()`) against RDS, then exits — it never starts Kestrel.
- The runtime image intentionally omits the `dotnet ef` CLI; migrations are
  applied by the app itself, reusing the shipped assemblies, roles, and the DB
  connection string SSM secret.

**Deploy order (must be followed):**

```bash
# 1. build + push the new immutable image tag to ECR
# 2. terraform apply   (registers new migrate + api task revisions)

# 3. run the migration gate — MUST succeed (exit 0) before promoting the API
export CLUSTER=$(terraform output -raw ecs_cluster_name)
export MIGRATE_FAMILY=$(terraform output -raw ecs_migrate_task_family)
export SUBNETS=$(terraform output -json private_subnet_ids | jq -r 'join(",")')
export SECURITY_GROUP=$(terraform output -raw ecs_security_group_id)
../scripts/run-migrations.sh

# 4. ONLY if step 3 exited 0, promote the API service to the new revision
aws ecs update-service --cluster "$CLUSTER" \
  --service "$(terraform output -raw ecs_service_name)" \
  --force-new-deployment
```

If the migration task exits non-zero, `run-migrations.sh` fails and the
promotion in step 4 must be aborted — the previous API revision keeps serving.
Migration task output is in the `/nnm/migrate` CloudWatch log group.
