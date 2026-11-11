# ---------------------------------------------------------------------------
# CloudFront distribution for the static frontend SPA (Task 24.6)
# ---------------------------------------------------------------------------
# Serves the compiled React SPA from the private frontend S3 bucket
# (aws_s3_bucket.frontend, defined in s3.tf) via Origin Access Control (OAC).
# The bucket stays fully private; CloudFront is the only reader.
#
# Requirement 13.2 — CloudFront in front of the OAC-only S3 origin, with SPA
#   client-side routing support: 403/404 responses are rewritten to
#   /index.html with an HTTP 200 so deep links resolve to the SPA shell.
# Requirement 13.3 — viewers are forced onto HTTPS (HTTP is redirected), using
#   the us-east-1 ACM certificate for the apex + www domains.
#
# The S3 bucket policy granting CloudFront OAC read access lives here (deferred
# from Task 24.4) because it must reference this distribution's ARN in its
# AWS:SourceArn condition, scoping access to this distribution only.
# ---------------------------------------------------------------------------

# --- Origin Access Control -------------------------------------------------
# Modern replacement for the legacy Origin Access Identity. Signs every origin
# request with SigV4 so S3 can authorize CloudFront as the sole principal.

resource "aws_cloudfront_origin_access_control" "frontend" {
  name                              = "${local.name_prefix}-frontend-oac"
  description                       = "OAC for the ${local.name_prefix} frontend S3 origin"
  origin_access_control_origin_type = "s3"
  signing_behavior                  = "always"
  signing_protocol                  = "sigv4"
}

# --- Managed cache policy --------------------------------------------------
# AWS-managed "CachingOptimized" policy: long TTLs, gzip/brotli enabled, no
# forwarding of cookies/headers/query strings. Ideal for immutable static SPA
# assets. Looked up by name so we don't hard-code the well-known policy id.

data "aws_cloudfront_cache_policy" "caching_optimized" {
  name = "Managed-CachingOptimized"
}

# --- Distribution ----------------------------------------------------------

resource "aws_cloudfront_distribution" "frontend" {
  enabled             = true
  is_ipv6_enabled     = true
  comment             = "${local.name_prefix} frontend SPA"
  default_root_object = "index.html"
  price_class         = "PriceClass_All"

  # Apex + www served by this distribution. Matches the SANs on the
  # us-east-1 ACM certificate (see acm.tf).
  aliases = [var.domain_name, var.www_subdomain]

  origin {
    origin_id                = "s3-frontend"
    domain_name              = aws_s3_bucket.frontend.bucket_regional_domain_name
    origin_access_control_id = aws_cloudfront_origin_access_control.frontend.id
  }

  default_cache_behavior {
    target_origin_id       = "s3-frontend"
    viewer_protocol_policy = "redirect-to-https" # Req 13.3: force HTTPS
    allowed_methods        = ["GET", "HEAD", "OPTIONS"]
    cached_methods         = ["GET", "HEAD"]
    compress               = true
    cache_policy_id        = data.aws_cloudfront_cache_policy.caching_optimized.id
  }

  # Req 13.2: SPA client-side routing. S3 returns 403 (OAC, object missing)
  # or 404 for unknown keys; rewrite both to the SPA shell with a 200 so the
  # React router can resolve deep links.
  custom_error_response {
    error_code            = 403
    response_code         = 200
    response_page_path    = "/index.html"
    error_caching_min_ttl = 0
  }

  custom_error_response {
    error_code            = 404
    response_code         = 200
    response_page_path    = "/index.html"
    error_caching_min_ttl = 0
  }

  viewer_certificate {
    # Mirror the conditional in acm.tf's acm_cloudfront_certificate_arn output:
    # use the validated ARN when DNS validation is enabled (so the distribution
    # depends on successful issuance), otherwise the raw certificate ARN.
    acm_certificate_arn = var.create_dns_validation ? (
      aws_acm_certificate_validation.cloudfront[0].certificate_arn
    ) : aws_acm_certificate.cloudfront.arn
    ssl_support_method       = "sni-only"
    minimum_protocol_version = "TLSv1.2_2021"
  }

  restrictions {
    geo_restriction {
      restriction_type = "none"
    }
  }

  tags = merge(
    local.common_tags,
    {
      Name = "${local.name_prefix}-frontend-cdn"
    },
  )
}

# --- Frontend bucket policy (OAC-only read) --------------------------------
# Grants the CloudFront service principal s3:GetObject on the frontend bucket,
# scoped by AWS:SourceArn to THIS distribution only. The bucket remains private
# to everyone else (public access block in s3.tf stays in force). This is the
# policy Task 24.4 deferred, since it needs the distribution ARN.

data "aws_iam_policy_document" "frontend_oac" {
  statement {
    sid    = "AllowCloudFrontServicePrincipalReadOnly"
    effect = "Allow"

    principals {
      type        = "Service"
      identifiers = ["cloudfront.amazonaws.com"]
    }

    actions   = ["s3:GetObject"]
    resources = ["${aws_s3_bucket.frontend.arn}/*"]

    condition {
      test     = "StringEquals"
      variable = "AWS:SourceArn"
      values   = [aws_cloudfront_distribution.frontend.arn]
    }
  }
}

resource "aws_s3_bucket_policy" "frontend" {
  bucket = aws_s3_bucket.frontend.id
  policy = data.aws_iam_policy_document.frontend_oac.json

  # The public access block (block_public_policy = true) must already exist so
  # this non-public policy is accepted without a race on bucket creation.
  depends_on = [aws_s3_bucket_public_access_block.frontend]
}

# --- Outputs ---------------------------------------------------------------
# Consumed by Route 53 (24.10, A-alias target) and by CI cache invalidations.

output "cloudfront_distribution_id" {
  description = "ID of the CloudFront distribution serving the frontend SPA (used for cache invalidations)."
  value       = aws_cloudfront_distribution.frontend.id
}

output "cloudfront_domain_name" {
  description = "CloudFront distribution domain name (e.g. dxxxx.cloudfront.net); the Route 53 A-alias target for the apex + www records (24.10)."
  value       = aws_cloudfront_distribution.frontend.domain_name
}

output "cloudfront_distribution_arn" {
  description = "ARN of the CloudFront distribution (referenced by the frontend bucket's OAC policy condition)."
  value       = aws_cloudfront_distribution.frontend.arn
}

# The hosted-zone ID of CloudFront is a global constant; exposed so the Route 53
# alias record (24.10) can set zone_id without another data lookup.
output "cloudfront_hosted_zone_id" {
  description = "CloudFront's fixed Route 53 hosted zone ID, for A-alias records (24.10)."
  value       = aws_cloudfront_distribution.frontend.hosted_zone_id
}
