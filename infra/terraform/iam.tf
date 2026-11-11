# ---------------------------------------------------------------------------
# IAM roles for ECS (Task 24.9 / Requirement 13.6)
# ---------------------------------------------------------------------------
# Two roles, both assumed by the ECS tasks runtime (ecs-tasks.amazonaws.com).
# No IAM access keys are ever issued; credentials are vended automatically to
# the running task.
#
#   1. TASK ROLE (nnm-api-task-role) — the identity the APPLICATION code runs
#      as. Least-privilege, granting only what the .NET API needs at runtime:
#        * SSM  : read the /nnm/* SecureString parameters (+ kms:Decrypt so the
#                 SecureStrings can actually be decrypted).
#        * SES  : send transactional email from the verified domain identity.
#        * S3   : put/get leadership profile images in the images bucket.
#
#   2. EXECUTION ROLE — the identity the ECS AGENT uses to START the task:
#      pull the container image from ECR, write container logs to CloudWatch,
#      and inject the SecureString SSM parameters referenced by the task
#      definition's `secrets` block (task 24.8). Uses the AWS-managed
#      AmazonECSTaskExecutionRolePolicy plus a minimal SSM read + kms:Decrypt
#      policy for secret injection.
#
# data.aws_caller_identity.current is already declared in s3.tf and is only
# referenced here (not redeclared).
# ---------------------------------------------------------------------------

# --- Shared assume-role policy ---------------------------------------------

data "aws_iam_policy_document" "ecs_tasks_assume_role" {
  statement {
    effect  = "Allow"
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["ecs-tasks.amazonaws.com"]
    }
  }
}

# ===========================================================================
# 1. ECS TASK ROLE — application runtime identity (nnm-api-task-role)
# ===========================================================================

resource "aws_iam_role" "api_task" {
  name               = "nnm-api-task-role"
  assume_role_policy = data.aws_iam_policy_document.ecs_tasks_assume_role.json

  tags = merge(
    local.common_tags,
    {
      Name = "nnm-api-task-role"
    },
  )
}

# Least-privilege inline policy covering SSM, SES, and S3 per the design's
# IAM section.
data "aws_iam_policy_document" "api_task" {
  # SSM: read the /nnm/* parameter namespace only.
  statement {
    sid    = "SsmReadNnmParameters"
    effect = "Allow"
    actions = [
      "ssm:GetParameters",
      "ssm:GetParameter",
      "ssm:GetParametersByPath",
    ]
    resources = ["arn:aws:ssm:*:*:parameter/nnm/*"]
  }

  # SecureString parameters are encrypted with the default aws/ssm KMS key;
  # decryption is required to read their plaintext values at runtime.
  statement {
    sid       = "KmsDecryptSsmSecureStrings"
    effect    = "Allow"
    actions   = ["kms:Decrypt"]
    resources = ["arn:aws:kms:*:*:key/*"]

    condition {
      test     = "StringEquals"
      variable = "kms:ViaService"
      values   = ["ssm.${var.aws_region}.amazonaws.com"]
    }
  }

  # SES: send transactional email, scoped to the verified domain identity.
  statement {
    sid    = "SesSendFromVerifiedIdentity"
    effect = "Allow"
    actions = [
      "ses:SendEmail",
      "ses:SendRawEmail",
    ]
    resources = ["arn:aws:ses:*:*:identity/${var.domain_name}"]
  }

  # S3: read/write leadership profile images in the images bucket only.
  statement {
    sid    = "S3ImagesObjectAccess"
    effect = "Allow"
    actions = [
      "s3:PutObject",
      "s3:GetObject",
    ]
    resources = ["${aws_s3_bucket.images.arn}/*"]
  }
}

resource "aws_iam_role_policy" "api_task" {
  name   = "${local.name_prefix}-api-task-policy"
  role   = aws_iam_role.api_task.id
  policy = data.aws_iam_policy_document.api_task.json
}

# ===========================================================================
# 2. ECS TASK EXECUTION ROLE — ECS agent identity (image pull, logs, secrets)
# ===========================================================================

resource "aws_iam_role" "api_task_execution" {
  name               = "${local.name_prefix}-api-task-execution-role"
  assume_role_policy = data.aws_iam_policy_document.ecs_tasks_assume_role.json

  tags = merge(
    local.common_tags,
    {
      Name = "${local.name_prefix}-api-task-execution-role"
    },
  )
}

# AWS-managed baseline: ECR image pull + CloudWatch Logs write.
resource "aws_iam_role_policy_attachment" "api_task_execution_managed" {
  role       = aws_iam_role.api_task_execution.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
}

# Extra permission so the ECS agent can inject the SecureString SSM parameters
# referenced by the task definition's `secrets` block (task 24.8).
data "aws_iam_policy_document" "api_task_execution_secrets" {
  statement {
    sid    = "SsmReadNnmParametersForInjection"
    effect = "Allow"
    actions = [
      "ssm:GetParameters",
    ]
    resources = ["arn:aws:ssm:*:*:parameter/nnm/*"]
  }

  statement {
    sid       = "KmsDecryptSsmSecureStrings"
    effect    = "Allow"
    actions   = ["kms:Decrypt"]
    resources = ["arn:aws:kms:*:*:key/*"]

    condition {
      test     = "StringEquals"
      variable = "kms:ViaService"
      values   = ["ssm.${var.aws_region}.amazonaws.com"]
    }
  }
}

resource "aws_iam_role_policy" "api_task_execution_secrets" {
  name   = "${local.name_prefix}-api-task-execution-secrets"
  role   = aws_iam_role.api_task_execution.id
  policy = data.aws_iam_policy_document.api_task_execution_secrets.json
}

# --- Outputs ----------------------------------------------------------------
# Consumed by the ECS task definition (task 24.8).

output "ecs_task_role_arn" {
  description = "ARN of the ECS task role (nnm-api-task-role) assumed by the .NET API at runtime."
  value       = aws_iam_role.api_task.arn
}

output "ecs_task_role_name" {
  description = "Name of the ECS task role."
  value       = aws_iam_role.api_task.name
}

output "ecs_task_execution_role_arn" {
  description = "ARN of the ECS task execution role used by the ECS agent to pull images, write logs, and inject SSM secrets."
  value       = aws_iam_role.api_task_execution.arn
}

output "ecs_task_execution_role_name" {
  description = "Name of the ECS task execution role."
  value       = aws_iam_role.api_task_execution.name
}
