# ---------------------------------------------------------------------------
# Route 53 public DNS alias records (Task 24.10 / Requirement 13.1)
# ---------------------------------------------------------------------------
# Publishes the three public hostnames as A-alias records into the apex
# domain's public hosted zone:
#
#   nnmadvisory.com       (apex) ─▶ CloudFront distribution (frontend SPA)
#   www.nnmadvisory.com          ─▶ CloudFront distribution (frontend SPA)
#   api.nnmadvisory.com          ─▶ Application Load Balancer (API)
#
# Hosted zone ownership
# ---------------------
# The public hosted zone for the apex domain is assumed to already exist
# (registered/delegated out-of-band, or pre-created). acm.tf already looks it
# up via a count-guarded data source:
#
#   data "aws_route53_zone" "primary" { count = var.create_dns_validation ? 1 : 0 ... }
#
# To avoid two sources of truth for the same zone, this file does NOT
# redeclare that data source and does NOT create an aws_route53_zone resource.
# It reuses the existing lookup. Alias records require the target zone to
# exist regardless, so resolving the zone id through the shared data source is
# both correct and the simplest path.
#
# Because the lookup is gated on var.create_dns_validation, these records are
# likewise created only when that flag is true (the records' zone id resolves
# from data.aws_route53_zone.primary[0]). Enable create_dns_validation once the
# hosted zone is provisioned so ACM validation and these alias records are
# reconciled together.
#
# Alias targets are referenced directly off their resources (CloudFront
# distribution / ALB) rather than through module outputs, keeping all
# references within-module and avoiding output indirection.
# ---------------------------------------------------------------------------

locals {
  # Resolved id of the apex public hosted zone, sourced from the shared
  # count-guarded data lookup in acm.tf. Empty when create_dns_validation is
  # false (no records are created in that case — see the count guards below).
  hosted_zone_id = var.create_dns_validation ? data.aws_route53_zone.primary[0].zone_id : ""
}

# --- Apex: nnmadvisory.com -> CloudFront ------------------------------------
resource "aws_route53_record" "apex" {
  count = var.create_dns_validation ? 1 : 0

  zone_id = local.hosted_zone_id
  name    = var.domain_name
  type    = "A"

  alias {
    name                   = aws_cloudfront_distribution.frontend.domain_name
    zone_id                = aws_cloudfront_distribution.frontend.hosted_zone_id
    evaluate_target_health = false
  }
}

# --- www.nnmadvisory.com -> CloudFront --------------------------------------
resource "aws_route53_record" "www" {
  count = var.create_dns_validation ? 1 : 0

  zone_id = local.hosted_zone_id
  name    = var.www_subdomain
  type    = "A"

  alias {
    name                   = aws_cloudfront_distribution.frontend.domain_name
    zone_id                = aws_cloudfront_distribution.frontend.hosted_zone_id
    evaluate_target_health = false
  }
}

# --- api.nnmadvisory.com -> Application Load Balancer ------------------------
# evaluate_target_health is true so Route 53 reflects ALB target health; the
# ALB only has an HTTPS:443 listener (see alb.tf).
resource "aws_route53_record" "api" {
  count = var.create_dns_validation ? 1 : 0

  zone_id = local.hosted_zone_id
  name    = var.api_subdomain
  type    = "A"

  alias {
    name                   = aws_lb.main.dns_name
    zone_id                = aws_lb.main.zone_id
    evaluate_target_health = true
  }
}

# --- Outputs ----------------------------------------------------------------

output "hosted_zone_id" {
  description = "Resolved id of the apex public hosted zone the alias records are written into (empty when create_dns_validation is false)."
  value       = local.hosted_zone_id
}

output "apex_record_fqdn" {
  description = "FQDN of the apex A-alias record pointing at CloudFront (null when create_dns_validation is false)."
  value       = var.create_dns_validation ? aws_route53_record.apex[0].fqdn : null
}

output "www_record_fqdn" {
  description = "FQDN of the www A-alias record pointing at CloudFront (null when create_dns_validation is false)."
  value       = var.create_dns_validation ? aws_route53_record.www[0].fqdn : null
}

output "api_record_fqdn" {
  description = "FQDN of the api A-alias record pointing at the ALB (null when create_dns_validation is false)."
  value       = var.create_dns_validation ? aws_route53_record.api[0].fqdn : null
}
