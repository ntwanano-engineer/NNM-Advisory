# ---------------------------------------------------------------------------
# Terraform & provider requirements
# ---------------------------------------------------------------------------
# Pins the Terraform CLI and AWS provider versions so infrastructure is
# reproducible across machines and CI runs. Subsequent 24.x tasks (security
# groups, RDS, S3, CloudFront, ALB, ECS, Route53, CloudWatch, SSM) add their
# own .tf files in this directory and reuse this single provider config.
# ---------------------------------------------------------------------------

terraform {
  required_version = ">= 1.5.0"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
  }
}

provider "aws" {
  region = var.aws_region

  default_tags {
    tags = local.common_tags
  }
}

# Aliased provider pinned to us-east-1. CloudFront requires its ACM
# certificate to live in us-east-1 regardless of the deployment region,
# so the CloudFront certificate (apex + www) is created through this
# provider while the ALB certificate (api.*) uses the default provider
# in the deployment region. See acm.tf.
provider "aws" {
  alias  = "us_east_1"
  region = "us-east-1"

  default_tags {
    tags = local.common_tags
  }
}
