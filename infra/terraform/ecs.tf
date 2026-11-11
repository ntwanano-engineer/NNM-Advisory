# ---------------------------------------------------------------------------
# ECS Fargate — ECR, CloudWatch logs, cluster, task definition, service
# (Task 24.8 / Requirement 13.4)
# ---------------------------------------------------------------------------
# Runs the containerised .NET API on Fargate behind the ALB:
#
#   ALB target group (8080) ──▶ ECS service ──▶ Fargate task ──▶ container "api"
#
#   * ECR repository holds the multi-stage image built from api/Dockerfile.
#   * CloudWatch log group /nnm/api collects container stdout/stderr via the
#     awslogs log driver (Serilog writes structured JSON to stdout).
#   * Task definition (awsvpc + FARGATE) wires:
#       - execution role  → image pull, log writes, SSM secret injection
#       - task role       → application runtime identity (SSM/SES/S3)
#       - secrets         → each /nnm/* SecureString ARN mapped to the env var
#                           the API's MapEnvironmentVariables() expects
#       - environment     → non-secret provider selectors (SES + S3)
#   * Service runs in the private subnets (no public IP), attached to sg-ecs,
#     and registers tasks into the ALB target group on port 8080. It waits for
#     the HTTPS listener so the target group is wired before tasks register.
#
# Reuses outputs/resources from the rest of the stack:
#   aws_vpc.main, aws_subnet.private[*], aws_security_group.ecs,
#   aws_lb_target_group.api, aws_lb_listener.https,
#   aws_iam_role.api_task / aws_iam_role.api_task_execution,
#   the ssm_parameter_arns map (ssm.tf), local.name_prefix / local.common_tags,
#   and var.aws_region.
# ---------------------------------------------------------------------------

# --- Variables --------------------------------------------------------------
# Declared here so this task's inputs stay self-contained alongside the
# resources that use them (matches the pattern in rds.tf / alb.tf / ssm.tf).

variable "api_image_tag" {
  description = "Container image tag deployed to the ECS service. The CI pipeline pushes an immutable tag per build; defaults to \"latest\" for bootstrap."
  type        = string
  default     = "latest"
}

variable "api_desired_count" {
  description = "Number of ECS tasks to run. Minimum 1 per the design (Requirement 13.4)."
  type        = number
  default     = 1

  validation {
    condition     = var.api_desired_count >= 1
    error_message = "api_desired_count must be at least 1 (the service runs a minimum of one task)."
  }
}

variable "api_task_cpu" {
  description = "Fargate task CPU units (256 = 0.25 vCPU). Must be a valid Fargate CPU/memory pairing."
  type        = number
  default     = 512
}

variable "api_task_memory" {
  description = "Fargate task memory (MiB). Must be a valid Fargate CPU/memory pairing."
  type        = number
  default     = 1024
}

variable "api_log_retention_days" {
  description = "Retention period in days for the /nnm/api CloudWatch log group."
  type        = number
  default     = 30
}

variable "ecr_image_tag_mutability" {
  description = "ECR image tag mutability. MUTABLE allows re-pushing \"latest\"; IMMUTABLE is stricter for production."
  type        = string
  default     = "MUTABLE"
}

# --- ECR repository ---------------------------------------------------------
# Holds the API container image built from api/Dockerfile and pushed by CI.
resource "aws_ecr_repository" "api" {
  name                 = "${local.name_prefix}-api"
  image_tag_mutability = var.ecr_image_tag_mutability
  force_delete         = true

  image_scanning_configuration {
    scan_on_push = true
  }

  tags = merge(
    local.common_tags,
    {
      Name = "${local.name_prefix}-api"
    },
  )
}

# Expire untagged images after 14 days to keep the repository tidy; retain the
# most recent tagged images.
resource "aws_ecr_lifecycle_policy" "api" {
  repository = aws_ecr_repository.api.name

  policy = jsonencode({
    rules = [
      {
        rulePriority = 1
        description  = "Expire untagged images older than 14 days"
        selection = {
          tagStatus   = "untagged"
          countType   = "sinceImagePushed"
          countUnit   = "days"
          countNumber = 14
        }
        action = {
          type = "expire"
        }
      },
      {
        rulePriority = 2
        description  = "Keep only the 10 most recent tagged images"
        selection = {
          tagStatus   = "any"
          countType   = "imageCountMoreThan"
          countNumber = 10
        }
        action = {
          type = "expire"
        }
      },
    ]
  })
}

# --- CloudWatch log group ---------------------------------------------------
# Container stdout/stderr lands here via the awslogs driver (Requirement 13.4).
resource "aws_cloudwatch_log_group" "api" {
  name              = "/nnm/api"
  retention_in_days = var.api_log_retention_days

  tags = merge(
    local.common_tags,
    {
      Name = "${local.name_prefix}-api-logs"
    },
  )
}

# --- ECS cluster ------------------------------------------------------------
resource "aws_ecs_cluster" "main" {
  name = "${local.name_prefix}-cluster"

  setting {
    name  = "containerInsights"
    value = "enabled"
  }

  tags = merge(
    local.common_tags,
    {
      Name = "${local.name_prefix}-cluster"
    },
  )
}

# --- Task definition --------------------------------------------------------
# Single "api" container, Fargate/awsvpc. The container_definitions JSON wires
# ports, logging, non-secret environment, and SSM-injected secrets.
resource "aws_ecs_task_definition" "api" {
  family                   = "${local.name_prefix}-api"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = var.api_task_cpu
  memory                   = var.api_task_memory

  # ECS agent identity (image pull, logs, secret injection).
  execution_role_arn = aws_iam_role.api_task_execution.arn
  # Application runtime identity (SSM/SES/S3 least-privilege).
  task_role_arn = aws_iam_role.api_task.arn

  container_definitions = jsonencode([
    {
      name      = "api"
      image     = "${aws_ecr_repository.api.repository_url}:${var.api_image_tag}"
      essential = true

      portMappings = [
        {
          containerPort = 8080
          hostPort      = 8080
          protocol      = "tcp"
        },
      ]

      # Non-secret runtime configuration. The provider selectors drive the
      # SES / S3 branches in Program.cs; ASPNETCORE_* pin the environment and
      # listen URL to match the container's EXPOSE 8080.
      environment = [
        { name = "ASPNETCORE_ENVIRONMENT", value = "Production" },
        { name = "ASPNETCORE_URLS", value = "http://+:8080" },
        { name = "EMAIL_PROVIDER", value = "ses" },
        { name = "IMAGE_STORAGE_PROVIDER", value = "s3" },
      ]

      # SecureString SSM parameters injected by the ECS agent at task start.
      # Keys match the flat env var names MapEnvironmentVariables() reads in
      # Program.cs; values are the parameter ARNs from ssm.tf.
      secrets = [
        { name = "DB_CONNECTION_STRING", valueFrom = local.ssm_parameter_arns.db_connection_string },
        { name = "JWT_SECRET", valueFrom = local.ssm_parameter_arns.jwt_secret },
        { name = "SENDER_EMAIL", valueFrom = local.ssm_parameter_arns.ses_sender_email },
        { name = "ADMIN_EMAIL", valueFrom = local.ssm_parameter_arns.ses_admin_email },
        { name = "S3_IMAGES_BUCKET", valueFrom = local.ssm_parameter_arns.s3_images_bucket },
        { name = "CORS_ALLOWED_ORIGIN", valueFrom = local.ssm_parameter_arns.cors_allowed_origin },
      ]

      logConfiguration = {
        logDriver = "awslogs"
        options = {
          "awslogs-group"         = aws_cloudwatch_log_group.api.name
          "awslogs-region"        = var.aws_region
          "awslogs-stream-prefix" = "api"
        }
      }
    },
  ])

  tags = merge(
    local.common_tags,
    {
      Name = "${local.name_prefix}-api-task"
    },
  )
}

# Local alias for the SSM parameter ARN map (output defined in ssm.tf) so the
# task definition secrets block reads clearly. The map is resolved from the
# same resources ssm.tf declares.
locals {
  ssm_parameter_arns = {
    db_connection_string = aws_ssm_parameter.db_connection_string.arn
    jwt_secret           = aws_ssm_parameter.jwt_secret.arn
    ses_sender_email     = aws_ssm_parameter.ses_sender_email.arn
    ses_admin_email      = aws_ssm_parameter.ses_admin_email.arn
    s3_images_bucket     = aws_ssm_parameter.s3_images_bucket.arn
    cors_allowed_origin  = aws_ssm_parameter.cors_allowed_origin.arn
  }
}

# --- ECS service ------------------------------------------------------------
# Fargate service in the private subnets, registered into the ALB target group.
resource "aws_ecs_service" "api" {
  name            = "${local.name_prefix}-api"
  cluster         = aws_ecs_cluster.main.id
  task_definition = aws_ecs_task_definition.api.arn
  desired_count   = var.api_desired_count
  launch_type     = "FARGATE"

  # Allow the ALB health check time to pass before counting a task unhealthy.
  health_check_grace_period_seconds = 60

  # Rolling deployment: keep at least one healthy task, allow one extra during
  # a deploy (200% / 100% of a single-task service).
  deployment_maximum_percent         = 200
  deployment_minimum_healthy_percent = 100

  network_configuration {
    subnets          = aws_subnet.private[*].id
    security_groups  = [aws_security_group.ecs.id]
    assign_public_ip = false
  }

  load_balancer {
    target_group_arn = aws_lb_target_group.api.arn
    container_name   = "api"
    container_port   = 8080
  }

  # The target group must be attached to the HTTPS listener before tasks
  # register, otherwise the service's load_balancer wiring has no live route.
  depends_on = [aws_lb_listener.https]

  # ECS mutates desired_count when auto-scaling is attached (24.11); ignore
  # external drift so Terraform doesn't fight the scaler.
  lifecycle {
    ignore_changes = [desired_count]
  }

  tags = merge(
    local.common_tags,
    {
      Name = "${local.name_prefix}-api-service"
    },
  )
}

# --- Outputs ----------------------------------------------------------------
# Consumed by the CI/CD pipeline (push image, force new deployment) and by
# monitoring/auto-scaling (24.11).

output "ecr_repository_url" {
  description = "URL of the ECR repository the CI pipeline pushes the API image to."
  value       = aws_ecr_repository.api.repository_url
}

output "ecs_cluster_name" {
  description = "Name of the ECS cluster running the API service."
  value       = aws_ecs_cluster.main.name
}

output "ecs_service_name" {
  description = "Name of the ECS Fargate service for the API."
  value       = aws_ecs_service.api.name
}

output "ecs_task_definition_arn" {
  description = "ARN of the active API task definition revision."
  value       = aws_ecs_task_definition.api.arn
}

output "log_group_name" {
  description = "Name of the CloudWatch log group receiving API container logs."
  value       = aws_cloudwatch_log_group.api.name
}
