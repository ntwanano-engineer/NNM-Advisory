# ---------------------------------------------------------------------------
# Data sources
# ---------------------------------------------------------------------------

# Discover the availability zones available in the configured region so the
# subnets land in real AZs without hard-coding AZ names per region.
data "aws_availability_zones" "available" {
  state = "available"
}
