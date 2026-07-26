# =============================================================================
# Outbox Worker ECS Task Definitions (Reference Template)
# =============================================================================
# This file provides a reference Terraform configuration for deploying the
# Outbox Worker as ECS Fargate tasks. Two variants are defined:
# - Ledger Outbox Worker
# - Product Outbox Worker
# =============================================================================

variable "environment" {
  description = "Deployment environment (dev, staging, prod)"
  type        = string
}

variable "aws_region" {
  description = "AWS region"
  type        = string
  default     = "us-east-1"
}

variable "ecr_repository_url" {
  description = "ECR repository URL for the outbox worker image"
  type        = string
}

variable "image_tag" {
  description = "Docker image tag to deploy"
  type        = string
  default     = "latest"
}

variable "private_subnet_ids" {
  description = "Private subnet IDs for ECS tasks"
  type        = list(string)
}

variable "vpc_id" {
  description = "VPC ID"
  type        = string
}

variable "ledger_sns_topic_arn" {
  description = "ARN of the ledger events SNS topic"
  type        = string
}

variable "product_sns_topic_arn" {
  description = "ARN of the product events SNS topic"
  type        = string
}

# =============================================================================
# IAM Execution Role (shared by both workers)
# =============================================================================

resource "aws_iam_role" "outbox_worker_execution_role" {
  name = "${var.environment}-outbox-worker-execution-role"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Action = "sts:AssumeRole"
        Effect = "Allow"
        Principal = {
          Service = "ecs-tasks.amazonaws.com"
        }
      }
    ]
  })
}

resource "aws_iam_role_policy_attachment" "outbox_worker_execution_base" {
  role       = aws_iam_role.outbox_worker_execution_role.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
}

resource "aws_iam_policy" "outbox_worker_ssm_read" {
  name = "${var.environment}-outbox-worker-ssm-read"

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Action = [
          "ssm:GetParameter",
          "ssm:GetParameters",
          "ssm:GetParametersByPath"
        ]
        Resource = "arn:aws:ssm:${var.aws_region}:*:parameter/${var.environment}/outbox-worker/*"
      }
    ]
  })
}

resource "aws_iam_role_policy_attachment" "outbox_worker_ssm" {
  role       = aws_iam_role.outbox_worker_execution_role.name
  policy_arn = aws_iam_policy.outbox_worker_ssm_read.arn
}

# =============================================================================
# IAM Task Role - Ledger Worker
# =============================================================================

resource "aws_iam_role" "ledger_outbox_worker_task_role" {
  name = "${var.environment}-ledger-outbox-worker-task-role"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Action = "sts:AssumeRole"
        Effect = "Allow"
        Principal = {
          Service = "ecs-tasks.amazonaws.com"
        }
      }
    ]
  })
}

resource "aws_iam_policy" "ledger_outbox_worker_sns_publish" {
  name = "${var.environment}-ledger-outbox-worker-sns-publish"

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect   = "Allow"
        Action   = "sns:Publish"
        Resource = var.ledger_sns_topic_arn
      }
    ]
  })
}

resource "aws_iam_role_policy_attachment" "ledger_outbox_worker_sns" {
  role       = aws_iam_role.ledger_outbox_worker_task_role.name
  policy_arn = aws_iam_policy.ledger_outbox_worker_sns_publish.arn
}

# =============================================================================
# IAM Task Role - Product Worker
# =============================================================================

resource "aws_iam_role" "product_outbox_worker_task_role" {
  name = "${var.environment}-product-outbox-worker-task-role"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Action = "sts:AssumeRole"
        Effect = "Allow"
        Principal = {
          Service = "ecs-tasks.amazonaws.com"
        }
      }
    ]
  })
}

resource "aws_iam_policy" "product_outbox_worker_sns_publish" {
  name = "${var.environment}-product-outbox-worker-sns-publish"

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect   = "Allow"
        Action   = "sns:Publish"
        Resource = var.product_sns_topic_arn
      }
    ]
  })
}

resource "aws_iam_role_policy_attachment" "product_outbox_worker_sns" {
  role       = aws_iam_role.product_outbox_worker_task_role.name
  policy_arn = aws_iam_policy.product_outbox_worker_sns_publish.arn
}

# =============================================================================
# ECS Task Definition - Ledger Outbox Worker
# =============================================================================

resource "aws_ecs_task_definition" "ledger_outbox_worker" {
  family                   = "${var.environment}-ledger-outbox-worker"
  network_mode             = "awsvpc"
  requires_compatibilities = ["FARGATE"]
  cpu                      = "256"  # 0.25 vCPU
  memory                   = "512"  # 0.5 GB
  execution_role_arn       = aws_iam_role.outbox_worker_execution_role.arn
  task_role_arn            = aws_iam_role.ledger_outbox_worker_task_role.arn

  container_definitions = jsonencode([
    {
      name      = "ledger-outbox-worker"
      image     = "${var.ecr_repository_url}:${var.image_tag}"
      essential = true

      environment = [
        {
          name  = "OUTBOX_POLLING_INTERVAL_SECONDS"
          value = "5"
        },
        {
          name  = "OUTBOX_BATCH_SIZE"
          value = "50"
        },
        {
          name  = "OUTBOX_SNS_TOPIC_ARN"
          value = var.ledger_sns_topic_arn
        },
        {
          name  = "OUTBOX_MAX_RETRY_ATTEMPTS"
          value = "3"
        },
        {
          name  = "OUTBOX_HEALTH_PORT"
          value = "8080"
        },
        {
          name  = "OUTBOX_SHUTDOWN_TIMEOUT_SECONDS"
          value = "30"
        }
      ]

      secrets = [
        {
          name      = "DATABASE_CONNECTION_STRING"
          valueFrom = "arn:aws:ssm:${var.aws_region}:*:parameter/${var.environment}/outbox-worker/ledger-connection-string"
        }
      ]

      portMappings = [
        {
          containerPort = 8080
          protocol      = "tcp"
        }
      ]

      healthCheck = {
        command     = ["CMD-SHELL", "curl -f http://localhost:8080/health || exit 1"]
        interval    = 30
        timeout     = 5
        retries     = 3
        startPeriod = 10
      }

      logConfiguration = {
        logDriver = "awslogs"
        options = {
          "awslogs-group"         = "/ecs/${var.environment}/ledger-outbox-worker"
          "awslogs-region"        = var.aws_region
          "awslogs-stream-prefix" = "ecs"
        }
      }
    }
  ])
}

# =============================================================================
# ECS Task Definition - Product Outbox Worker
# =============================================================================

resource "aws_ecs_task_definition" "product_outbox_worker" {
  family                   = "${var.environment}-product-outbox-worker"
  network_mode             = "awsvpc"
  requires_compatibilities = ["FARGATE"]
  cpu                      = "256"  # 0.25 vCPU
  memory                   = "512"  # 0.5 GB
  execution_role_arn       = aws_iam_role.outbox_worker_execution_role.arn
  task_role_arn            = aws_iam_role.product_outbox_worker_task_role.arn

  container_definitions = jsonencode([
    {
      name      = "product-outbox-worker"
      image     = "${var.ecr_repository_url}:${var.image_tag}"
      essential = true

      environment = [
        {
          name  = "OUTBOX_POLLING_INTERVAL_SECONDS"
          value = "5"
        },
        {
          name  = "OUTBOX_BATCH_SIZE"
          value = "50"
        },
        {
          name  = "OUTBOX_SNS_TOPIC_ARN"
          value = var.product_sns_topic_arn
        },
        {
          name  = "OUTBOX_MAX_RETRY_ATTEMPTS"
          value = "3"
        },
        {
          name  = "OUTBOX_HEALTH_PORT"
          value = "8080"
        },
        {
          name  = "OUTBOX_SHUTDOWN_TIMEOUT_SECONDS"
          value = "30"
        }
      ]

      secrets = [
        {
          name      = "DATABASE_CONNECTION_STRING"
          valueFrom = "arn:aws:ssm:${var.aws_region}:*:parameter/${var.environment}/outbox-worker/product-connection-string"
        }
      ]

      portMappings = [
        {
          containerPort = 8080
          protocol      = "tcp"
        }
      ]

      healthCheck = {
        command     = ["CMD-SHELL", "curl -f http://localhost:8080/health || exit 1"]
        interval    = 30
        timeout     = 5
        retries     = 3
        startPeriod = 10
      }

      logConfiguration = {
        logDriver = "awslogs"
        options = {
          "awslogs-group"         = "/ecs/${var.environment}/product-outbox-worker"
          "awslogs-region"        = var.aws_region
          "awslogs-stream-prefix" = "ecs"
        }
      }
    }
  ])
}

# =============================================================================
# ECS Service - Ledger Outbox Worker
# =============================================================================

resource "aws_ecs_service" "ledger_outbox_worker" {
  name            = "${var.environment}-ledger-outbox-worker"
  cluster         = "${var.environment}-ecs-cluster"
  task_definition = aws_ecs_task_definition.ledger_outbox_worker.arn
  desired_count   = 1
  launch_type     = "FARGATE"

  network_configuration {
    subnets          = var.private_subnet_ids
    assign_public_ip = false
  }
}

# =============================================================================
# ECS Service - Product Outbox Worker
# =============================================================================

resource "aws_ecs_service" "product_outbox_worker" {
  name            = "${var.environment}-product-outbox-worker"
  cluster         = "${var.environment}-ecs-cluster"
  task_definition = aws_ecs_task_definition.product_outbox_worker.arn
  desired_count   = 1
  launch_type     = "FARGATE"

  network_configuration {
    subnets          = var.private_subnet_ids
    assign_public_ip = false
  }
}

# =============================================================================
# CloudWatch Log Groups
# =============================================================================

resource "aws_cloudwatch_log_group" "ledger_outbox_worker" {
  name              = "/ecs/${var.environment}/ledger-outbox-worker"
  retention_in_days = 30
}

resource "aws_cloudwatch_log_group" "product_outbox_worker" {
  name              = "/ecs/${var.environment}/product-outbox-worker"
  retention_in_days = 30
}
