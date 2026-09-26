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
  allowed_account_ids = [var.expected_account_id]

  default_tags {
    tags = {
      Application = "autobots"
      Environment = "dev"
      ManagedBy   = "terraform"
      Component   = "public-website"
    }
  }
}

provider "aws" {
  alias               = "certificates"
  region              = "us-east-1"
  allowed_account_ids = [var.expected_account_id]

  default_tags {
    tags = {
      Application = "autobots"
      Environment = "dev"
      ManagedBy   = "terraform"
      Component   = "public-website"
    }
  }
}

variable "custom_domain" {
  description = "Optional fully qualified website hostname; DNS stays with the existing registrar."
  type        = string
  default     = ""

  validation {
    condition     = var.custom_domain == "" || can(regex("^[a-z0-9-]+(\\.[a-z0-9-]+)+$", var.custom_domain))
    error_message = "Use a lowercase hostname without a scheme or path."
  }
}

data "aws_caller_identity" "current" {}

check "expected_account" {
  assert {
    condition     = data.aws_caller_identity.current.account_id == var.expected_account_id
    error_message = "Refusing to manage website resources in an unexpected AWS account."
  }
}
