# ---------------------------------------------------------------------------
# Application Load Balancer (Task 24.7 / Requirements 13.4, 13.7)
# ---------------------------------------------------------------------------
# Internet-facing ALB that terminates TLS for api.nnmadvisory.com and forwards
# plaintext traffic to the ECS Fargate tasks on port 8080:
#
#   Internet ──443/HTTPS──▶ ALB ──8080/HTTP──▶ ECS target group (/health)
#
#   * Internet-facing, placed in both public subnets (aws_subnet.public[*])
#     for multi-AZ availability; attached to sg-alb (public HTTPS ingress).
#   * HTTPS:443 listener ONLY. There is deliberately NO HTTP:80 listener —
#     the API rejects plaintext (Requirement 13.7), so the ALB offers no
#     plaintext entry point at all. Edge HTTP→HTTPS redirection for the
#     browser-facing site is handled by CloudFront (24.6); the API host is
#     HTTPS-only end to end.
#   * The HTTPS listener uses the regional ACM certificate for
#     api.nnmadvisory.com (acm_alb_certificate_arn from acm.tf) and a modern
#     TLS 1.3 security policy.
#   * Target group uses target_type "ip" — required for Fargate tasks, which
#     run in awsvpc mode and register by ENI IP rather than instance ID.
#     Health checks hit the API's /health endpoint (HealthController),
#     expecting HTTP 200.
#
# Outputs feed:
#   * Route 53 alias record (24.10)  — alb_dns_name, alb_zone_id
#   * ECS service load_balancer block (24.8) — target_group_arn
# ---------------------------------------------------------------------------

# --- Variables --------------------------------------------------------------
# Declared here so this task's inputs stay self-contained alongside the
# resources that use them, matching the pattern used by rds.tf / acm.tf.

variable "alb_target_port" {
  description = "Port the ECS Fargate tasks listen on and the ALB forwards to."
  type        = number
  default     = 8080
}

variable "alb_health_check_path" {
  description = "HTTP path the ALB polls for target health. Served by the API's HealthController (returns 200)."
  type        = string
  default     = "/health"
}

variable "alb_ssl_policy" {
  description = "ELB security policy for the HTTPS listener. Defaults to a modern TLS 1.3 policy."
  type        = string
  default     = "ELBSecurityPolicy-TLS13-1-2-2021-06"
}

variable "alb_deletion_protection" {
  description = "Whether to enable deletion protection on the load balancer. Recommended for production."
  type        = bool
  default     = false
}

# --- Load balancer ----------------------------------------------------------
# Internet-facing, spread across both public subnets, fronted by sg-alb.
resource "aws_lb" "main" {
  name               = "${local.name_prefix}-alb"
  load_balancer_type = "application"
  internal           = false

  subnets         = aws_subnet.public[*].id
  security_groups = [aws_security_group.alb.id]

  enable_deletion_protection = var.alb_deletion_protection

  tags = {
    Name = "${local.name_prefix}-alb"
    Tier = "alb"
  }
}

# --- Target group -----------------------------------------------------------
# target_type "ip" is mandatory for Fargate (awsvpc networking): tasks register
# by their ENI IP address. The ECS service (24.8) attaches tasks to this group
# via its load_balancer block.
resource "aws_lb_target_group" "api" {
  name        = "${local.name_prefix}-tg"
  target_type = "ip"
  port        = var.alb_target_port
  protocol    = "HTTP"
  vpc_id      = aws_vpc.main.id

  # Drain in-flight requests for 30s before deregistering a task.
  deregistration_delay = 30

  health_check {
    enabled             = true
    path                = var.alb_health_check_path
    protocol            = "HTTP"
    port                = "traffic-port"
    matcher             = "200"
    interval            = 30
    timeout             = 5
    healthy_threshold   = 2
    unhealthy_threshold = 3
  }

  tags = {
    Name = "${local.name_prefix}-tg"
    Tier = "ecs"
  }

  # New target group must exist before the old one is removed so the listener
  # default_action never points at a destroyed group.
  lifecycle {
    create_before_destroy = true
  }
}

# --- HTTPS listener ---------------------------------------------------------
# Port 443 only. TLS terminates here using the regional ACM certificate for
# api.nnmadvisory.com; forwards decrypted traffic to the target group.
resource "aws_lb_listener" "https" {
  load_balancer_arn = aws_lb.main.arn
  port              = 443
  protocol          = "HTTPS"
  ssl_policy        = var.alb_ssl_policy

  # Regional ACM cert for api.nnmadvisory.com. Mirrors the acm_alb_certificate_arn
  # output logic in acm.tf: depend on the validated ARN when DNS validation is
  # enabled (so the listener waits for issuance), otherwise the raw cert ARN.
  certificate_arn = var.create_dns_validation ? aws_acm_certificate_validation.alb[0].certificate_arn : aws_acm_certificate.alb.arn

  default_action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.api.arn
  }

  tags = {
    Name = "${local.name_prefix}-alb-https"
  }
}

# NOTE: no aws_lb_listener on port 80. Requirement 13.7 — the ALB exposes no
# plaintext HTTP entry point.

# --- Outputs ----------------------------------------------------------------
# Consumed by Route 53 (24.10) and the ECS service (24.8).

output "alb_dns_name" {
  description = "Public DNS name of the ALB, used as the Route 53 alias target (24.10)."
  value       = aws_lb.main.dns_name
}

output "alb_zone_id" {
  description = "Canonical hosted zone ID of the ALB, used for the Route 53 alias record (24.10)."
  value       = aws_lb.main.zone_id
}

output "alb_arn" {
  description = "ARN of the Application Load Balancer."
  value       = aws_lb.main.arn
}

output "target_group_arn" {
  description = "ARN of the ECS target group, attached by the ECS service's load_balancer block (24.8)."
  value       = aws_lb_target_group.api.arn
}
