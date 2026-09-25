variable "aws_region" {
  description = "Deployment region for the approved Autobots development account."
  type        = string
  default     = "ap-south-1"
}

variable "expected_account_id" {
  description = "Required AWS account ID guard."
  type        = string

  validation {
    condition     = can(regex("^[0-9]{12}$", var.expected_account_id))
    error_message = "Set the exact 12 digit AWS account ID before planning this environment."
  }
}

variable "owner_email" {
  description = "Owner tag and closed-account allowlist input."
  type        = string

  validation {
    condition     = can(regex("^[^@ ]+@[^@ ]+\\.[^@ ]+$", var.owner_email))
    error_message = "Set a valid owner email address before planning this environment."
  }
}

variable "gcp_project_id" {
  description = "Selected GCP project used by the server-side Gemini provider."
  type        = string

  validation {
    condition     = length(var.gcp_project_id) >= 6 && length(var.gcp_project_id) <= 30
    error_message = "Set the exact GCP project ID before planning the dev API."
  }
}

variable "gcp_project_number" {
  description = "Exact project number used in the workload identity configuration."
  type        = string

  validation {
    condition     = can(regex("^[0-9]+$", var.gcp_project_number))
    error_message = "Set the exact numeric GCP project number before planning the dev API."
  }
}

variable "model_location" {
  description = "Gemini model endpoint location. Global does not imply India-only processing."
  type        = string
  default     = "global"
}

variable "instance_type" {
  description = "Single small dev API instance type."
  type        = string
  default     = "t3.medium"
}

variable "monthly_budget_usd" {
  description = "AWS account-level email alert budget; it is not a provider-enforced spending cap."
  type        = number
  default     = 75

  validation {
    condition     = var.monthly_budget_usd > 0 && var.monthly_budget_usd <= 75
    error_message = "The Autobots dev budget must be no greater than the approved USD 75 per month."
  }
}

variable "api_domain_suffix" {
  description = "Dynamic TLS host suffix for the dev endpoint; no domain is purchased."
  type        = string
  default     = "sslip.io"
}
