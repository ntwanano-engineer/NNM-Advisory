# ---------------------------------------------------------------------------
# RDS PostgreSQL (Task 24.3 / Requirement 13.5)
# ---------------------------------------------------------------------------
# A managed PostgreSQL 14+ instance for the .NET API, placed entirely inside
# the private subnets with no public accessibility:
#
#   * engine_version 14+        — satisfies "PostgreSQL 14+".
#   * db.t3.medium (default)    — adjustable via var.db_instance_class.
#   * gp3 storage               — allocated_storage with max_allocated_storage
#                                 set, which enables storage auto-scaling.
#   * db subnet group spanning  — aws_subnet.private[*] (both AZs), so the
#     the two private subnets     database only ever lands in private space.
#   * publicly_accessible=false — never reachable from the internet.
#   * vpc_security_group_ids     — sg-rds only; ingress 5432 from sg-ecs
#                                 (see security_groups.tf).
#   * storage_encrypted=true    — encryption at rest.
#   * multi_az (optional)       — off by default, recommended for production
#                                 (var.db_multi_az).
#
# The master password is NOT hard-coded. It is supplied through the sensitive
# var.db_password (no default) and, per task 24.9, the full connection string
# is additionally stored in SSM Parameter Store as a SecureString for the API
# to consume at runtime.
# ---------------------------------------------------------------------------

# --- Variables --------------------------------------------------------------
# Defined here (rather than variables.tf) so this task's inputs stay
# self-contained and don't collide with concurrently-authored infra files.

variable "db_name" {
  description = "Name of the initial PostgreSQL database created on the instance."
  type        = string
  default     = "nnmadvisory"
}

variable "db_username" {
  description = "Master username for the RDS PostgreSQL instance."
  type        = string
  default     = "nnmadmin"
}

variable "db_password" {
  description = "Master password for the RDS PostgreSQL instance. Supply via a secure channel (TF_VAR_db_password or a secret backend); never commit it. The connection string is also stored in SSM per task 24.9."
  type        = string
  sensitive   = true
  # No default: Terraform will require this to be provided explicitly.
}

variable "db_engine_version" {
  description = "PostgreSQL engine version (must be 14 or later)."
  type        = string
  default     = "14"
}

variable "db_instance_class" {
  description = "RDS instance class."
  type        = string
  default     = "db.t3.medium"
}

variable "db_allocated_storage" {
  description = "Initial allocated storage in GiB (gp3)."
  type        = number
  default     = 20
}

variable "db_max_allocated_storage" {
  description = "Upper bound in GiB for storage auto-scaling. Must be greater than db_allocated_storage to enable auto-scaling."
  type        = number
  default     = 100
}

variable "db_multi_az" {
  description = "Whether to deploy the RDS instance across multiple AZs. Optional; recommended for production."
  type        = bool
  default     = false
}

variable "db_backup_retention_period" {
  description = "Number of days to retain automated backups."
  type        = number
  default     = 7
}

variable "db_deletion_protection" {
  description = "Whether deletion protection is enabled on the RDS instance."
  type        = bool
  default     = true
}

variable "db_skip_final_snapshot" {
  description = "Whether to skip the final snapshot when the instance is destroyed. Keep false for production to retain a snapshot."
  type        = bool
  default     = false
}

# --- DB subnet group --------------------------------------------------------
# Spans both private subnets so RDS (and its optional Multi-AZ standby) can
# only be placed in private network space.
resource "aws_db_subnet_group" "main" {
  name_prefix = "${local.name_prefix}-db-"
  subnet_ids  = aws_subnet.private[*].id

  tags = {
    Name = "${local.name_prefix}-db-subnet-group"
  }

  lifecycle {
    create_before_destroy = true
  }
}

# --- RDS instance -----------------------------------------------------------
resource "aws_db_instance" "main" {
  identifier = "${local.name_prefix}-db"

  engine         = "postgres"
  engine_version = var.db_engine_version
  instance_class = var.db_instance_class

  # gp3 storage. Setting max_allocated_storage above allocated_storage turns on
  # RDS storage auto-scaling.
  storage_type          = "gp3"
  allocated_storage     = var.db_allocated_storage
  max_allocated_storage = var.db_max_allocated_storage
  storage_encrypted     = true

  db_name  = var.db_name
  username = var.db_username
  password = var.db_password
  port     = 5432

  # Network isolation: private subnet group + RDS SG only, never public.
  db_subnet_group_name   = aws_db_subnet_group.main.name
  vpc_security_group_ids = [aws_security_group.rds.id]
  publicly_accessible    = false

  multi_az = var.db_multi_az

  backup_retention_period = var.db_backup_retention_period
  deletion_protection     = var.db_deletion_protection
  skip_final_snapshot     = var.db_skip_final_snapshot
  final_snapshot_identifier = (
    var.db_skip_final_snapshot
    ? null
    : "${local.name_prefix}-db-final"
  )

  # Apply maintenance/parameter changes immediately only outside production if
  # desired; default (false) defers to the maintenance window to avoid
  # unexpected downtime.
  apply_immediately = false

  tags = {
    Name = "${local.name_prefix}-rds-postgres"
    Tier = "rds"
  }
}

# --- Outputs ----------------------------------------------------------------
# Consumed by task 24.9 (assembling the SSM connection string) and 24.11
# (CloudWatch free-storage alarm).

output "rds_endpoint" {
  description = "Connection endpoint of the RDS instance (address:port)."
  value       = aws_db_instance.main.endpoint
}

output "rds_address" {
  description = "Hostname (DNS address) of the RDS instance."
  value       = aws_db_instance.main.address
}

output "rds_port" {
  description = "Port the RDS instance listens on."
  value       = aws_db_instance.main.port
}

output "rds_identifier" {
  description = "Identifier of the RDS instance."
  value       = aws_db_instance.main.identifier
}

output "rds_db_name" {
  description = "Name of the initial database created on the instance."
  value       = aws_db_instance.main.db_name
}
