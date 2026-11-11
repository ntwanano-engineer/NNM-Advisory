# ---------------------------------------------------------------------------
# CloudWatch monitoring, alarms & ECS auto-scaling (Task 24.11 / Requirement 13.1)
# ---------------------------------------------------------------------------
# Wires the three operational signals called for by the design:
#
#   1. ALB 5xx error RATE > 1% over 5 minutes  ──▶ SNS alarm topic
#        Expressed as a metric-math alarm: (ELB 5XX + Target 5XX) / Requests
#        * 100. A plain count threshold would misfire under load and stay
#        silent under light traffic, so the ratio is what we alarm on.
#
#   2. ECS service CPU > 80% over 5 minutes     ──▶ auto-scaling trigger
#        Implemented with Application Auto Scaling target tracking on
#        ECSServiceAverageCPUUtilization at a target of 80%. Target tracking
#        provisions and manages its own pair of CloudWatch alarms (high/low)
#        internally, scaling DesiredCount out when average CPU rises above the
#        target for the sustained window and back in when it falls. This is
#        the robust form of "CPU > 80% over 5 min → scale" — ECS owns the
#        alarms so there is no hand-wired alarm fighting the scaler.
#        (The ECS service sets ignore_changes = [desired_count] so Terraform
#        does not revert the scaler's adjustments — see ecs.tf.)
#
#   3. RDS FreeStorageSpace < 20% of allocated  ──▶ SNS alarm topic
#        FreeStorageSpace is reported in bytes, so the threshold is computed
#        as 0.20 * db_allocated_storage (GiB) * 1024^3.
#
# All SNS notifications go to one alarm topic. An optional email subscription
# is created only when var.alarm_email is set (guarded by count), so the stack
# applies cleanly with no email configured.
#
# Reuses: aws_lb.main (arn_suffix), aws_lb_target_group.api (arn_suffix),
#         aws_ecs_cluster.main, aws_ecs_service.api, aws_db_instance.main,
#         var.db_allocated_storage, local.name_prefix / local.common_tags.
# ---------------------------------------------------------------------------

# --- Variables --------------------------------------------------------------
# Declared here so this task's inputs stay self-contained alongside the
# resources that use them (matches rds.tf / alb.tf / ecs.tf / ssm.tf).

variable "alarm_email" {
  description = "Email address subscribed to the CloudWatch alarm SNS topic. When set, an email subscription is created (confirm via the AWS subscription email). Leave empty to create the topic with no subscription."
  type        = string
  default     = ""
}

variable "ecs_min_capacity" {
  description = "Minimum number of ECS tasks the auto-scaler may scale in to."
  type        = number
  default     = 1

  validation {
    condition     = var.ecs_min_capacity >= 1
    error_message = "ecs_min_capacity must be at least 1 (the service always runs one task)."
  }
}

variable "ecs_max_capacity" {
  description = "Maximum number of ECS tasks the auto-scaler may scale out to."
  type        = number
  default     = 4

  validation {
    condition     = var.ecs_max_capacity >= 1
    error_message = "ecs_max_capacity must be at least 1."
  }
}

variable "ecs_cpu_target" {
  description = "Target average ECS service CPU utilisation (percent) for target-tracking auto-scaling. Scaling is triggered when sustained CPU crosses this value."
  type        = number
  default     = 80

  validation {
    condition     = var.ecs_cpu_target > 0 && var.ecs_cpu_target <= 100
    error_message = "ecs_cpu_target must be between 1 and 100."
  }
}

# --- SNS alarm topic --------------------------------------------------------
# Single destination for alarm-state notifications. The optional email
# subscription is created only when var.alarm_email is non-empty.
resource "aws_sns_topic" "alarms" {
  name = "${local.name_prefix}-alarms"

  tags = merge(
    local.common_tags,
    {
      Name = "${local.name_prefix}-alarms"
    },
  )
}

resource "aws_sns_topic_subscription" "alarms_email" {
  count = var.alarm_email != "" ? 1 : 0

  topic_arn = aws_sns_topic.alarms.arn
  protocol  = "email"
  endpoint  = var.alarm_email
}

# --- Alarm 1: ALB 5xx error rate > 1% over 5 minutes ------------------------
# Metric-math alarm. m_elb and m_target are fetched with no label so they do
# not appear on the alarm; e_rate is the expression that is evaluated against
# the threshold. RequestCount can be 0 in a quiet period, so the denominator
# is clamped to at least 1 to avoid a divide-by-zero producing NaN (which
# would put the alarm in INSUFFICIENT_DATA rather than OK).
resource "aws_cloudwatch_metric_alarm" "alb_5xx_rate" {
  alarm_name        = "${local.name_prefix}-alb-5xx-rate"
  alarm_description = "ALB 5xx error rate exceeded 1% over 5 minutes (ELB + target 5xx responses / total requests)."

  comparison_operator = "GreaterThanThreshold"
  threshold           = 1
  evaluation_periods  = 1
  treat_missing_data  = "notBreaching"

  alarm_actions = [aws_sns_topic.alarms.arn]
  ok_actions    = [aws_sns_topic.alarms.arn]

  metric_query {
    id          = "e_rate"
    expression  = "(m_elb + m_target) / MAX([m_requests, 1]) * 100"
    label       = "ALB 5xx error rate (%)"
    return_data = true
  }

  metric_query {
    id = "m_elb"
    metric {
      namespace   = "AWS/ApplicationELB"
      metric_name = "HTTPCode_ELB_5XX_Count"
      stat        = "Sum"
      period      = 300
      dimensions = {
        LoadBalancer = aws_lb.main.arn_suffix
      }
    }
  }

  metric_query {
    id = "m_target"
    metric {
      namespace   = "AWS/ApplicationELB"
      metric_name = "HTTPCode_Target_5XX_Count"
      stat        = "Sum"
      period      = 300
      dimensions = {
        LoadBalancer = aws_lb.main.arn_suffix
      }
    }
  }

  metric_query {
    id = "m_requests"
    metric {
      namespace   = "AWS/ApplicationELB"
      metric_name = "RequestCount"
      stat        = "Sum"
      period      = 300
      dimensions = {
        LoadBalancer = aws_lb.main.arn_suffix
      }
    }
  }

  tags = merge(
    local.common_tags,
    {
      Name = "${local.name_prefix}-alb-5xx-rate"
    },
  )
}

# --- ECS auto-scaling: CPU > 80% over 5 minutes -----------------------------
# Register the ECS service's DesiredCount as a scalable target, then attach a
# target-tracking policy on average CPU utilisation. ECS creates and manages
# the underlying high/low CloudWatch alarms for this policy automatically.
resource "aws_appautoscaling_target" "ecs_api" {
  service_namespace  = "ecs"
  resource_id        = "service/${aws_ecs_cluster.main.name}/${aws_ecs_service.api.name}"
  scalable_dimension = "ecs:service:DesiredCount"
  min_capacity       = var.ecs_min_capacity
  max_capacity       = var.ecs_max_capacity
}

resource "aws_appautoscaling_policy" "ecs_api_cpu" {
  name               = "${local.name_prefix}-ecs-cpu-target-tracking"
  policy_type        = "TargetTrackingScaling"
  service_namespace  = aws_appautoscaling_target.ecs_api.service_namespace
  resource_id        = aws_appautoscaling_target.ecs_api.resource_id
  scalable_dimension = aws_appautoscaling_target.ecs_api.scalable_dimension

  target_tracking_scaling_policy_configuration {
    predefined_metric_specification {
      predefined_metric_type = "ECSServiceAverageCPUUtilization"
    }

    target_value = var.ecs_cpu_target

    # 5-minute (300s) cooldowns so a sustained-CPU condition drives one
    # scaling action per window rather than oscillating.
    scale_in_cooldown  = 300
    scale_out_cooldown = 300
  }
}

# --- Alarm 3: RDS free storage < 20% of allocated over 5 minutes ------------
# FreeStorageSpace is in bytes. Threshold = 0.20 * allocated GiB * 1024^3.
resource "aws_cloudwatch_metric_alarm" "rds_free_storage" {
  alarm_name        = "${local.name_prefix}-rds-free-storage"
  alarm_description = "RDS free storage dropped below 20% of allocated storage over 5 minutes."

  namespace           = "AWS/RDS"
  metric_name         = "FreeStorageSpace"
  statistic           = "Average"
  comparison_operator = "LessThanThreshold"
  threshold           = 0.20 * var.db_allocated_storage * pow(1024, 3)
  period              = 300
  evaluation_periods  = 1
  treat_missing_data  = "notBreaching"

  dimensions = {
    DBInstanceIdentifier = aws_db_instance.main.identifier
  }

  alarm_actions = [aws_sns_topic.alarms.arn]
  ok_actions    = [aws_sns_topic.alarms.arn]

  tags = merge(
    local.common_tags,
    {
      Name = "${local.name_prefix}-rds-free-storage"
    },
  )
}

# --- Outputs ----------------------------------------------------------------

output "sns_alarm_topic_arn" {
  description = "ARN of the SNS topic that receives CloudWatch alarm notifications."
  value       = aws_sns_topic.alarms.arn
}
