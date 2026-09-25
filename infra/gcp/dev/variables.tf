variable "expected_project_id" {
  description = "Exact GCP project approved for the Autobots dev workload."
  type        = string

  validation {
    condition     = length(var.expected_project_id) >= 6 && length(var.expected_project_id) <= 30
    error_message = "Set the exact GCP project ID before planning this environment."
  }
}

variable "expected_project_number" {
  description = "Project number guard for the selected project."
  type        = string

  validation {
    condition     = can(regex("^[0-9]+$", var.expected_project_number))
    error_message = "Set the exact numeric project number before planning this environment."
  }
}

variable "model_location" {
  description = "Managed Gemini model endpoint location. Global is not an India-only residency claim."
  type        = string
  default     = "global"
}

variable "aws_account_id" {
  description = "AWS account trusted by the dev workload identity provider."
  type        = string

  validation {
    condition     = can(regex("^[0-9]{12}$", var.aws_account_id))
    error_message = "Set the exact 12 digit AWS account ID before planning federation."
  }
}

variable "aws_runtime_role_name" {
  description = "Exact AWS runtime role permitted to exchange credentials with GCP."
  type        = string
  default     = "autobots-dev-api"

  validation {
    condition     = can(regex("^[A-Za-z0-9+=,.@_-]{1,64}$", var.aws_runtime_role_name))
    error_message = "AWS runtime role name contains unsupported characters."
  }
}

variable "workload_pool_id" {
  type    = string
  default = "autobots-dev-aws"
}

variable "workload_provider_id" {
  type    = string
  default = "autobots-aws-runtime"
}

variable "inference_service_account_id" {
  type    = string
  default = "autobots-inference"
}
