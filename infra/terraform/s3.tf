# ---------------------------------------------------------------------------
# S3 buckets (Task 24.4 / Requirement 13.2)
# ---------------------------------------------------------------------------
# Two fully private S3 buckets. Neither is ever publicly readable or writable.
#
#   frontend : hosts the compiled React SPA static assets. Readable ONLY by
#              CloudFront via Origin Access Control (OAC). The bucket policy
#              that grants that OAC read access is attached in Task 24.6,
#              because it needs the CloudFront distribution ARN as a condition
#              and that distribution does not exist yet. See the placeholder
#              note on aws_s3_bucket.frontend below.
#
#   images   : stores leadership profile images (the S3_IMAGES_BUCKET concept).
#              No public access whatsoever. The ECS task role (Task 24.9) is
#              granted s3:PutObject / s3:GetObject against this bucket, and the
#              API hands images to browsers via short-lived pre-signed URLs.
#
# Both buckets apply the same hardening baseline:
#   - public access block with all four flags true (no ACL/policy can open them)
#   - BucketOwnerEnforced ownership (ACLs disabled entirely)
#   - server-side encryption with SSE-S3 (AES256)
#   - versioning enabled (cheap safety net against accidental overwrite/delete)
#
# Bucket names must be globally unique across all AWS accounts, so each name is
# suffixed with the deploying account ID (from data.aws_caller_identity). An
# explicit override variable is provided for environments that prefer a fixed
# name. Account ID is used (rather than a random_id) so names stay stable and
# predictable across plan/apply cycles.
# ---------------------------------------------------------------------------

# --- Inputs -----------------------------------------------------------------

variable "frontend_bucket_name" {
  description = "Optional explicit name for the static frontend assets bucket. When empty, derived from name_prefix + account id for global uniqueness."
  type        = string
  default     = ""
}

variable "images_bucket_name" {
  description = "Optional explicit name for the leadership profile images bucket. When empty, derived from name_prefix + account id for global uniqueness."
  type        = string
  default     = ""
}

# --- Supporting data / locals ----------------------------------------------

# Account ID gives each bucket name a deterministic, globally-unique suffix.
data "aws_caller_identity" "current" {}

locals {
  frontend_bucket_name = var.frontend_bucket_name != "" ? var.frontend_bucket_name : "${local.name_prefix}-frontend-${data.aws_caller_identity.current.account_id}"
  images_bucket_name   = var.images_bucket_name != "" ? var.images_bucket_name : "${local.name_prefix}-images-${data.aws_caller_identity.current.account_id}"
}

# ===========================================================================
# Static frontend assets bucket (CloudFront OAC origin)
# ===========================================================================

resource "aws_s3_bucket" "frontend" {
  bucket = local.frontend_bucket_name

  tags = {
    Name = "${local.name_prefix}-frontend"
    Role = "frontend-static-assets"
  }
}

# NOTE (Task 24.6): The bucket policy granting CloudFront OAC read access
# (s3:GetObject allowed for principal cloudfront.amazonaws.com, conditioned on
# AWS:SourceArn = the CloudFront distribution ARN) is defined as an
# aws_s3_bucket_policy in the CloudFront task, which owns the distribution
# resource. No public-read policy is ever attached here.

resource "aws_s3_bucket_public_access_block" "frontend" {
  bucket = aws_s3_bucket.frontend.id

  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_ownership_controls" "frontend" {
  bucket = aws_s3_bucket.frontend.id

  rule {
    object_ownership = "BucketOwnerEnforced"
  }
}

resource "aws_s3_bucket_versioning" "frontend" {
  bucket = aws_s3_bucket.frontend.id

  versioning_configuration {
    status = "Enabled"
  }
}

resource "aws_s3_bucket_server_side_encryption_configuration" "frontend" {
  bucket = aws_s3_bucket.frontend.id

  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
    bucket_key_enabled = true
  }
}

# ===========================================================================
# Leadership profile images bucket (private; API-mediated via pre-signed URLs)
# ===========================================================================

resource "aws_s3_bucket" "images" {
  bucket = local.images_bucket_name

  tags = {
    Name = "${local.name_prefix}-images"
    Role = "leadership-profile-images"
  }
}

resource "aws_s3_bucket_public_access_block" "images" {
  bucket = aws_s3_bucket.images.id

  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_ownership_controls" "images" {
  bucket = aws_s3_bucket.images.id

  rule {
    object_ownership = "BucketOwnerEnforced"
  }
}

resource "aws_s3_bucket_versioning" "images" {
  bucket = aws_s3_bucket.images.id

  versioning_configuration {
    status = "Enabled"
  }
}

resource "aws_s3_bucket_server_side_encryption_configuration" "images" {
  bucket = aws_s3_bucket.images.id

  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
    bucket_key_enabled = true
  }
}

# --- Outputs ----------------------------------------------------------------
# Consumed by later tasks: CloudFront (24.6) needs the frontend bucket id and
# regional domain name for its origin + OAC bucket policy; IAM (24.9) needs the
# images bucket arn for the ECS task role's least-privilege S3 policy.

output "frontend_bucket_id" {
  description = "Name/ID of the static frontend assets S3 bucket."
  value       = aws_s3_bucket.frontend.id
}

output "frontend_bucket_arn" {
  description = "ARN of the static frontend assets S3 bucket."
  value       = aws_s3_bucket.frontend.arn
}

output "frontend_bucket_regional_domain_name" {
  description = "Regional domain name of the frontend bucket, used as the CloudFront S3 origin."
  value       = aws_s3_bucket.frontend.bucket_regional_domain_name
}

output "images_bucket_id" {
  description = "Name/ID of the leadership profile images S3 bucket."
  value       = aws_s3_bucket.images.id
}

output "images_bucket_arn" {
  description = "ARN of the leadership profile images S3 bucket (used by the ECS task role IAM policy)."
  value       = aws_s3_bucket.images.arn
}
