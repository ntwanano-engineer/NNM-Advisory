# ---------------------------------------------------------------------------
# SSM Parameter Store (Task 24.9 / Requirement 13.6)
# ---------------------------------------------------------------------------
# Every piece of sensitive runtime configuration is stored as a SecureString
# parameter under the /nnm/* namespace. The .NET API loads these at startup
# via AWS.Extensions.Configuration.SystemsManager; the ECS task role (iam.tf)
# grants path-scoped read access, and the ECS task EXECUTION role can inject
# them as container secrets in the task definition (task 24.8).
#
# No real secret values are ever committed to source control:
#   * The DB connection string is ASSEMBLED from the RDS address/db name and
#     the already-sensitive var.db_username / var.db_password (rds.tf).
#   * The JWT signing secret comes from the sensitive var.jwt_secret, which
#     has NO default and must be supplied out-of-band (TF_VAR_jwt_secret or a
#     secrets backend).
#   * The remaining values (SES emails, images bucket, CORS origin) are
#     derived from existing resources/variables with sensible defaults.
#
# All six parameters use type = "SecureString" so they are encrypted at rest
# with the account's default aws/ssm KMS key.
# ---------------------------------------------------------------------------

# --- Variables --------------------------------------------------------------
# Scoped to this file to avoid collisions with concurrently-authored infra.

variable "jwt_secret" {
  description = "JWT signing secret (minimum 256-bit / 32+ chars). Supplied via TF_VAR_jwt_secret or a secrets backend; never committed. Stored in SSM as a SecureString."
  type        = string
  sensitive   = true
  # No default: Terraform requires this to be provided explicitly.
}

variable "ses_sender_email" {
  description = "Verified SES sender (from) address for outbound mail. Defaults to noreply@<domain_name> when empty."
  type        = string
  default     = ""
}

variable "ses_admin_email" {
  description = "Company admin inbox that receives contact-form notifications. Defaults to admin@<domain_name> when empty."
  type        = string
  default     = ""
}

variable "cors_allowed_origin" {
  description = "Frontend origin URL allowed by the API's CORS policy. Defaults to https://<domain_name> when empty."
  type        = string
  default     = ""
}

# --- Derived values ---------------------------------------------------------

locals {
  # PostgreSQL (Npgsql) connection string consumed by the .NET API.
  db_connection_string = "Host=${aws_db_instance.main.address};Port=${aws_db_instance.main.port};Database=${aws_db_instance.main.db_name};Username=${var.db_username};Password=${var.db_password}"

  ses_sender_email    = var.ses_sender_email != "" ? var.ses_sender_email : "noreply@${var.domain_name}"
  ses_admin_email     = var.ses_admin_email != "" ? var.ses_admin_email : "admin@${var.domain_name}"
  cors_allowed_origin = var.cors_allowed_origin != "" ? var.cors_allowed_origin : "https://${var.domain_name}"
}

# --- Parameters -------------------------------------------------------------

resource "aws_ssm_parameter" "db_connection_string" {
  name        = "/nnm/db/connection-string"
  description = "Full PostgreSQL connection string for the .NET API."
  type        = "SecureString"
  value       = local.db_connection_string

  tags = merge(
    local.common_tags,
    {
      Name = "${local.name_prefix}-db-connection-string"
    },
  )
}

resource "aws_ssm_parameter" "jwt_secret" {
  name        = "/nnm/jwt/secret"
  description = "JWT signing secret (min 256-bit) for the .NET API."
  type        = "SecureString"
  value       = var.jwt_secret

  tags = merge(
    local.common_tags,
    {
      Name = "${local.name_prefix}-jwt-secret"
    },
  )
}

resource "aws_ssm_parameter" "ses_sender_email" {
  name        = "/nnm/ses/sender-email"
  description = "Verified SES sender email address."
  type        = "SecureString"
  value       = local.ses_sender_email

  tags = merge(
    local.common_tags,
    {
      Name = "${local.name_prefix}-ses-sender-email"
    },
  )
}

resource "aws_ssm_parameter" "ses_admin_email" {
  name        = "/nnm/ses/admin-email"
  description = "Company admin inbox address for contact-form notifications."
  type        = "SecureString"
  value       = local.ses_admin_email

  tags = merge(
    local.common_tags,
    {
      Name = "${local.name_prefix}-ses-admin-email"
    },
  )
}

resource "aws_ssm_parameter" "s3_images_bucket" {
  name        = "/nnm/s3/images-bucket"
  description = "S3 bucket name for leadership profile image uploads."
  type        = "SecureString"
  value       = aws_s3_bucket.images.id

  tags = merge(
    local.common_tags,
    {
      Name = "${local.name_prefix}-s3-images-bucket"
    },
  )
}

resource "aws_ssm_parameter" "cors_allowed_origin" {
  name        = "/nnm/cors/allowed-origin"
  description = "Frontend origin URL allowed by the API CORS policy."
  type        = "SecureString"
  value       = local.cors_allowed_origin

  tags = merge(
    local.common_tags,
    {
      Name = "${local.name_prefix}-cors-allowed-origin"
    },
  )
}

# --- Outputs ----------------------------------------------------------------
# Consumed by the ECS task definition (task 24.8) to wire the container's
# `secrets` block to each SecureString parameter ARN.

output "ssm_parameter_arns" {
  description = "Map of logical name => ARN for every /nnm/* SecureString parameter, consumed by the ECS task definition (24.8)."
  value = {
    db_connection_string = aws_ssm_parameter.db_connection_string.arn
    jwt_secret           = aws_ssm_parameter.jwt_secret.arn
    ses_sender_email     = aws_ssm_parameter.ses_sender_email.arn
    ses_admin_email      = aws_ssm_parameter.ses_admin_email.arn
    s3_images_bucket     = aws_ssm_parameter.s3_images_bucket.arn
    cors_allowed_origin  = aws_ssm_parameter.cors_allowed_origin.arn
  }
}

output "ssm_parameter_names" {
  description = "Map of logical name => SSM parameter name for every /nnm/* SecureString parameter."
  value = {
    db_connection_string = aws_ssm_parameter.db_connection_string.name
    jwt_secret           = aws_ssm_parameter.jwt_secret.name
    ses_sender_email     = aws_ssm_parameter.ses_sender_email.name
    ses_admin_email      = aws_ssm_parameter.ses_admin_email.name
    s3_images_bucket     = aws_ssm_parameter.s3_images_bucket.name
    cors_allowed_origin  = aws_ssm_parameter.cors_allowed_origin.name
  }
}
