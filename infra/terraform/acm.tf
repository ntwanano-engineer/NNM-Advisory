# ---------------------------------------------------------------------------
# ACM TLS certificates (Task 24.5)
# ---------------------------------------------------------------------------
# Two certificates are issued, both validated via DNS:
#
#   1. CloudFront certificate  — covers the apex (nnmadvisory.com) and
#      www.nnmadvisory.com. CloudFront ONLY accepts certificates from
#      us-east-1, so this one is created through the aws.us_east_1
#      aliased provider (see versions.tf). Consumed by CloudFront (24.6).
#
#   2. ALB certificate         — covers api.nnmadvisory.com. The ALB uses a
#      certificate in the deployment region, so this one uses the default
#      provider. Consumed by the ALB HTTPS listener (24.7).
#
# DNS validation records are written into the public hosted zone for the
# apex domain. The hosted zone itself is created/managed by the Route 53
# task (24.10); here we look it up by name via a data source. If the zone
# does not yet exist, set create_dns_validation = false to provision the
# certificates in a PENDING_VALIDATION state and let 24.10 reconcile the
# validation records.
# ---------------------------------------------------------------------------

# --- Variables -------------------------------------------------------------

variable "domain_name" {
  description = "Apex/root domain for the site. Used to derive the www and api hostnames and to locate the Route 53 hosted zone."
  type        = string
  default     = "nnmadvisory.com"
}

variable "www_subdomain" {
  description = "Fully-qualified www hostname served by CloudFront alongside the apex."
  type        = string
  default     = "www.nnmadvisory.com"
}

variable "api_subdomain" {
  description = "Fully-qualified API hostname served by the ALB."
  type        = string
  default     = "api.nnmadvisory.com"
}

variable "create_dns_validation" {
  description = "When true, look up the Route 53 hosted zone and create the DNS validation records + certificate validation resources here. Set to false if the hosted zone is not yet provisioned (the Route 53 task 24.10 will reconcile validation)."
  type        = bool
  default     = true
}

# --- Hosted zone lookup ----------------------------------------------------

# The public hosted zone for the apex domain. Managed by the Route 53 task
# (24.10); referenced here only when create_dns_validation is enabled.
data "aws_route53_zone" "primary" {
  count        = var.create_dns_validation ? 1 : 0
  name         = "${var.domain_name}."
  private_zone = false
}

# ---------------------------------------------------------------------------
# Certificate 1: CloudFront (apex + www) — MUST be in us-east-1
# ---------------------------------------------------------------------------

resource "aws_acm_certificate" "cloudfront" {
  provider = aws.us_east_1

  domain_name               = var.domain_name
  subject_alternative_names = [var.www_subdomain]
  validation_method         = "DNS"

  tags = merge(
    local.common_tags,
    {
      Name = "${local.name_prefix}-cloudfront-cert"
    },
  )

  lifecycle {
    create_before_destroy = true
  }
}

# One Route 53 record per distinct domain validation option on the
# CloudFront certificate (apex + www may share an option, hence for_each
# dedupes by record name).
resource "aws_route53_record" "cloudfront_validation" {
  for_each = var.create_dns_validation ? {
    for dvo in aws_acm_certificate.cloudfront.domain_validation_options :
    dvo.domain_name => {
      name   = dvo.resource_record_name
      record = dvo.resource_record_value
      type   = dvo.resource_record_type
    }
  } : {}

  zone_id         = data.aws_route53_zone.primary[0].zone_id
  name            = each.value.name
  type            = each.value.type
  records         = [each.value.record]
  ttl             = 60
  allow_overwrite = true
}

resource "aws_acm_certificate_validation" "cloudfront" {
  count    = var.create_dns_validation ? 1 : 0
  provider = aws.us_east_1

  certificate_arn         = aws_acm_certificate.cloudfront.arn
  validation_record_fqdns = [for record in aws_route53_record.cloudfront_validation : record.fqdn]
}

# ---------------------------------------------------------------------------
# Certificate 2: ALB (api.nnmadvisory.com) — deployment region
# ---------------------------------------------------------------------------

resource "aws_acm_certificate" "alb" {
  domain_name       = var.api_subdomain
  validation_method = "DNS"

  tags = merge(
    local.common_tags,
    {
      Name = "${local.name_prefix}-alb-cert"
    },
  )

  lifecycle {
    create_before_destroy = true
  }
}

resource "aws_route53_record" "alb_validation" {
  for_each = var.create_dns_validation ? {
    for dvo in aws_acm_certificate.alb.domain_validation_options :
    dvo.domain_name => {
      name   = dvo.resource_record_name
      record = dvo.resource_record_value
      type   = dvo.resource_record_type
    }
  } : {}

  zone_id         = data.aws_route53_zone.primary[0].zone_id
  name            = each.value.name
  type            = each.value.type
  records         = [each.value.record]
  ttl             = 60
  allow_overwrite = true
}

resource "aws_acm_certificate_validation" "alb" {
  count = var.create_dns_validation ? 1 : 0

  certificate_arn         = aws_acm_certificate.alb.arn
  validation_record_fqdns = [for record in aws_route53_record.alb_validation : record.fqdn]
}

# --- Outputs ---------------------------------------------------------------
# Certificate ARNs consumed by CloudFront (24.6) and the ALB (24.7).
# When DNS validation is enabled the validated ARN is exposed so downstream
# resources depend on successful issuance; otherwise the raw certificate ARN
# is exposed.

output "acm_cloudfront_certificate_arn" {
  description = "ARN of the us-east-1 ACM certificate for the apex + www domains, consumed by the CloudFront distribution (24.6)."
  value = var.create_dns_validation ? (
    aws_acm_certificate_validation.cloudfront[0].certificate_arn
  ) : aws_acm_certificate.cloudfront.arn
}

output "acm_alb_certificate_arn" {
  description = "ARN of the regional ACM certificate for api.nnmadvisory.com, consumed by the ALB HTTPS listener (24.7)."
  value = var.create_dns_validation ? (
    aws_acm_certificate_validation.alb[0].certificate_arn
  ) : aws_acm_certificate.alb.arn
}
