# ---------------------------------------------------------------------------
# Shared locals
# ---------------------------------------------------------------------------
# Naming prefix and common tag set reused by every resource in this stack.
# ---------------------------------------------------------------------------

locals {
  name_prefix = "${var.project_name}-${var.environment}"

  common_tags = merge(
    {
      Project     = var.project_name
      Environment = var.environment
      ManagedBy   = "terraform"
    },
    var.additional_tags,
  )

  # Names of the availability zones selected for this deployment, trimmed to az_count.
  azs = slice(data.aws_availability_zones.available.names, 0, var.az_count)
}
