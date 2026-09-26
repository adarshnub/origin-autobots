terraform {
  required_version = ">= 1.9.0, < 2.0.0"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "= 6.62.0"
    }
  }
}

variable "aws_region" {
  description = "Region for the private website origin bucket."
  type        = string
  default     = "ap-south-1"
}

variable "expected_account_id" {
  description = "The approved Autobots AWS account."
  type        = string

  validation {
    condition     = can(regex("^[0-9]{12}$", var.expected_account_id))
    error_message = "Set the exact 12 digit AWS account ID."
  }
}

provider "aws" {
  region = var.aws_region

  default_tags {
    tags = {
      Application = "autobots"
      Environment = "dev"
      ManagedBy   = "terraform"
      Component   = "public-website"
    }
  }
}

data "aws_caller_identity" "current" {}

check "expected_account" {
  assert {
    condition     = data.aws_caller_identity.current.account_id == var.expected_account_id
    error_message = "Refusing to manage website resources in an unexpected AWS account."
  }
}
