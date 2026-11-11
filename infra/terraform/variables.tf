# ---------------------------------------------------------------------------
# Input variables
# ---------------------------------------------------------------------------
# Shared across all infra .tf files. Keep environment- and account-specific
# values out of the HCL and pass them via *.tfvars or CI variables.
# ---------------------------------------------------------------------------

variable "aws_region" {
  description = "AWS region all production infrastructure is deployed into."
  type        = string
  default     = "af-south-1"
}

variable "project_name" {
  description = "Short project identifier used to name and tag resources."
  type        = string
  default     = "nnm-advisory"
}

variable "environment" {
  description = "Deployment environment (e.g. prod, staging). Used in tags and names."
  type        = string
  default     = "prod"
}

variable "vpc_cidr" {
  description = "CIDR block for the main VPC."
  type        = string
  default     = "10.0.0.0/16"
}

variable "public_subnet_cidrs" {
  description = "CIDR blocks for the public subnets, one per availability zone (AZ-a, AZ-b)."
  type        = list(string)
  default     = ["10.0.1.0/24", "10.0.2.0/24"]
}

variable "private_subnet_cidrs" {
  description = "CIDR blocks for the private subnets, one per availability zone (AZ-a, AZ-b)."
  type        = list(string)
  default     = ["10.0.11.0/24", "10.0.12.0/24"]
}

variable "az_count" {
  description = "Number of availability zones to spread subnets across."
  type        = number
  default     = 2
}

variable "additional_tags" {
  description = "Optional extra tags merged into every resource's default tags."
  type        = map(string)
  default     = {}
}
