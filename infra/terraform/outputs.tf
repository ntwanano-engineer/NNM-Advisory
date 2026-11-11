# ---------------------------------------------------------------------------
# Outputs
# ---------------------------------------------------------------------------
# Exposed for consumption by subsequent 24.x infra tasks (security groups,
# RDS, ALB, ECS, etc.) that reference this networking foundation.
# ---------------------------------------------------------------------------

output "vpc_id" {
  description = "ID of the main VPC."
  value       = aws_vpc.main.id
}

output "vpc_cidr" {
  description = "CIDR block of the main VPC."
  value       = aws_vpc.main.cidr_block
}

output "public_subnet_ids" {
  description = "IDs of the public subnets (ALB, NAT Gateway)."
  value       = aws_subnet.public[*].id
}

output "private_subnet_ids" {
  description = "IDs of the private subnets (ECS tasks, RDS)."
  value       = aws_subnet.private[*].id
}

output "internet_gateway_id" {
  description = "ID of the Internet Gateway."
  value       = aws_internet_gateway.main.id
}

output "nat_gateway_id" {
  description = "ID of the NAT Gateway."
  value       = aws_nat_gateway.main.id
}

output "availability_zones" {
  description = "Availability zones the subnets are spread across."
  value       = local.azs
}
