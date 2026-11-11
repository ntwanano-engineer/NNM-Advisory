# ---------------------------------------------------------------------------
# Security groups (Task 24.2 / Requirements 13.1, 13.5)
# ---------------------------------------------------------------------------
# Three-tier network isolation inside aws_vpc.main. Each tier only accepts
# traffic from the tier directly in front of it:
#
#   Internet ──443──▶ sg-alb ──8080──▶ sg-ecs ──5432──▶ sg-rds
#
#   sg-alb : public HTTPS entry point (ALB).            inbound 443 from 0.0.0.0/0
#   sg-ecs : Fargate tasks behind the ALB.              inbound 8080 from sg-alb
#   sg-rds : PostgreSQL, reachable only from the tasks. inbound 5432 from sg-ecs
#
# Ingress/egress rules are declared as standalone aws_security_group_rule
# resources rather than inline blocks. sg-alb and sg-ecs reference each other
# (ALB egress -> ECS, ECS ingress <- ALB); inline rules would create a cyclic
# dependency between the two aws_security_group resources. Standalone rules
# depend only on the group IDs, which Terraform can resolve without a cycle.
# ---------------------------------------------------------------------------

# --- Security group shells --------------------------------------------------

resource "aws_security_group" "alb" {
  name_prefix = "${local.name_prefix}-alb-"
  description = "ALB: public HTTPS ingress, forwards to ECS tasks on 8080."
  vpc_id      = aws_vpc.main.id

  tags = {
    Name = "${local.name_prefix}-sg-alb"
    Tier = "alb"
  }

  # name_prefix-based SGs must be recreated before the old one is destroyed so
  # dependent resources (ALB, rules) never lose their reference.
  lifecycle {
    create_before_destroy = true
  }
}

resource "aws_security_group" "ecs" {
  name_prefix = "${local.name_prefix}-ecs-"
  description = "ECS Fargate tasks: ingress from ALB on 8080, egress to RDS + HTTPS."
  vpc_id      = aws_vpc.main.id

  tags = {
    Name = "${local.name_prefix}-sg-ecs"
    Tier = "ecs"
  }

  lifecycle {
    create_before_destroy = true
  }
}

resource "aws_security_group" "rds" {
  name_prefix = "${local.name_prefix}-rds-"
  description = "RDS PostgreSQL: ingress from ECS tasks on 5432 only, no public access."
  vpc_id      = aws_vpc.main.id

  tags = {
    Name = "${local.name_prefix}-sg-rds"
    Tier = "rds"
  }

  lifecycle {
    create_before_destroy = true
  }
}

# --- sg-alb rules -----------------------------------------------------------

# Public HTTPS ingress. TLS terminates at the ALB.
resource "aws_security_group_rule" "alb_ingress_https" {
  type              = "ingress"
  description       = "HTTPS from the internet"
  security_group_id = aws_security_group.alb.id
  protocol          = "tcp"
  from_port         = 443
  to_port           = 443
  cidr_blocks       = ["0.0.0.0/0"]
}

# Egress scoped to the ECS tier on the application port only.
resource "aws_security_group_rule" "alb_egress_to_ecs" {
  type                     = "egress"
  description              = "Forward to ECS tasks on 8080"
  security_group_id        = aws_security_group.alb.id
  protocol                 = "tcp"
  from_port                = 8080
  to_port                  = 8080
  source_security_group_id = aws_security_group.ecs.id
}

# --- sg-ecs rules -----------------------------------------------------------

# Only the ALB may reach the tasks, and only on the app port.
resource "aws_security_group_rule" "ecs_ingress_from_alb" {
  type                     = "ingress"
  description              = "App traffic from the ALB on 8080"
  security_group_id        = aws_security_group.ecs.id
  protocol                 = "tcp"
  from_port                = 8080
  to_port                  = 8080
  source_security_group_id = aws_security_group.alb.id
}

# Egress to the database tier on the PostgreSQL port.
resource "aws_security_group_rule" "ecs_egress_to_rds" {
  type                     = "egress"
  description              = "PostgreSQL to RDS on 5432"
  security_group_id        = aws_security_group.ecs.id
  protocol                 = "tcp"
  from_port                = 5432
  to_port                  = 5432
  source_security_group_id = aws_security_group.rds.id
}

# Outbound HTTPS for AWS service endpoints (SES, SSM, S3, CloudWatch) and image
# pulls via the NAT Gateway.
resource "aws_security_group_rule" "ecs_egress_https" {
  type              = "egress"
  description       = "HTTPS to AWS service endpoints (SES, SSM, S3, CloudWatch)"
  security_group_id = aws_security_group.ecs.id
  protocol          = "tcp"
  from_port         = 443
  to_port           = 443
  cidr_blocks       = ["0.0.0.0/0"]
}

# --- sg-rds rules -----------------------------------------------------------

# PostgreSQL reachable only from the ECS task tier. No public inbound, and no
# egress rules are defined (the database never initiates outbound connections).
resource "aws_security_group_rule" "rds_ingress_from_ecs" {
  type                     = "ingress"
  description              = "PostgreSQL from ECS tasks on 5432"
  security_group_id        = aws_security_group.rds.id
  protocol                 = "tcp"
  from_port                = 5432
  to_port                  = 5432
  source_security_group_id = aws_security_group.ecs.id
}

# --- Outputs ----------------------------------------------------------------
# Consumed by later tasks: ALB (24.7), RDS (24.3), ECS (24.8).

output "sg_alb_id" {
  description = "ID of the ALB security group (public HTTPS ingress)."
  value       = aws_security_group.alb.id
}

output "sg_ecs_id" {
  description = "ID of the ECS task security group."
  value       = aws_security_group.ecs.id
}

output "sg_rds_id" {
  description = "ID of the RDS security group (PostgreSQL, ECS-only ingress)."
  value       = aws_security_group.rds.id
}
