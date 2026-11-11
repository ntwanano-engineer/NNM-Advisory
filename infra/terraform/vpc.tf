# ---------------------------------------------------------------------------
# VPC networking (Task 24.1 / Requirement 13.1)
# ---------------------------------------------------------------------------
# Separates public-facing concerns (ALB, NAT Gateway) from private concerns
# (ECS Fargate tasks, RDS) across two availability zones.
#
#   VPC                 10.0.0.0/16
#   Public Subnet A     10.0.1.0/24   (AZ-a) — ALB, NAT Gateway
#   Public Subnet B     10.0.2.0/24   (AZ-b) — ALB (multi-AZ)
#   Private Subnet A    10.0.11.0/24  (AZ-a) — ECS tasks, RDS primary
#   Private Subnet B    10.0.12.0/24  (AZ-b) — ECS tasks, RDS standby
# ---------------------------------------------------------------------------

# --- VPC --------------------------------------------------------------------
resource "aws_vpc" "main" {
  cidr_block           = var.vpc_cidr
  enable_dns_support   = true
  enable_dns_hostnames = true

  tags = {
    Name = "${local.name_prefix}-vpc"
  }
}

# --- Internet Gateway -------------------------------------------------------
# Provides public internet ingress/egress for the public subnets.
resource "aws_internet_gateway" "main" {
  vpc_id = aws_vpc.main.id

  tags = {
    Name = "${local.name_prefix}-igw"
  }
}

# --- Public subnets ---------------------------------------------------------
# One per AZ. Instances/ENIs launched here receive public IPs (used by the ALB
# and NAT Gateway).
resource "aws_subnet" "public" {
  count = var.az_count

  vpc_id                  = aws_vpc.main.id
  cidr_block              = var.public_subnet_cidrs[count.index]
  availability_zone       = local.azs[count.index]
  map_public_ip_on_launch = true

  tags = {
    Name = "${local.name_prefix}-public-${local.azs[count.index]}"
    Tier = "public"
  }
}

# --- Private subnets --------------------------------------------------------
# One per AZ. No direct internet route; egress flows through the NAT Gateway.
resource "aws_subnet" "private" {
  count = var.az_count

  vpc_id            = aws_vpc.main.id
  cidr_block        = var.private_subnet_cidrs[count.index]
  availability_zone = local.azs[count.index]

  tags = {
    Name = "${local.name_prefix}-private-${local.azs[count.index]}"
    Tier = "private"
  }
}

# --- NAT Gateway ------------------------------------------------------------
# Elastic IP for the NAT Gateway, which lives in the first public subnet and
# gives private-subnet workloads (ECS tasks) outbound internet access.
resource "aws_eip" "nat" {
  domain = "vpc"

  tags = {
    Name = "${local.name_prefix}-nat-eip"
  }

  # The NAT Gateway's EIP depends on the IGW being attached to the VPC.
  depends_on = [aws_internet_gateway.main]
}

resource "aws_nat_gateway" "main" {
  allocation_id = aws_eip.nat.id
  subnet_id     = aws_subnet.public[0].id

  tags = {
    Name = "${local.name_prefix}-nat"
  }

  depends_on = [aws_internet_gateway.main]
}

# --- Public route table -----------------------------------------------------
# Default route to the internet via the Internet Gateway.
resource "aws_route_table" "public" {
  vpc_id = aws_vpc.main.id

  route {
    cidr_block = "0.0.0.0/0"
    gateway_id = aws_internet_gateway.main.id
  }

  tags = {
    Name = "${local.name_prefix}-public-rt"
  }
}

resource "aws_route_table_association" "public" {
  count = var.az_count

  subnet_id      = aws_subnet.public[count.index].id
  route_table_id = aws_route_table.public.id
}

# --- Private route table ----------------------------------------------------
# Default route to the internet via the NAT Gateway (outbound only).
resource "aws_route_table" "private" {
  vpc_id = aws_vpc.main.id

  route {
    cidr_block     = "0.0.0.0/0"
    nat_gateway_id = aws_nat_gateway.main.id
  }

  tags = {
    Name = "${local.name_prefix}-private-rt"
  }
}

resource "aws_route_table_association" "private" {
  count = var.az_count

  subnet_id      = aws_subnet.private[count.index].id
  route_table_id = aws_route_table.private.id
}
