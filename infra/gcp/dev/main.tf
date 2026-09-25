data "google_project" "selected" {
  project_id = var.expected_project_id
}

check "expected_project_number" {
  assert {
    condition     = data.google_project.selected.number == var.expected_project_number
    error_message = "Refusing to manage GCP resources: project number does not match expected_project_number."
  }
}

locals {
  required_apis = toset([
    "aiplatform.googleapis.com",
    "cloudresourcemanager.googleapis.com",
    "iam.googleapis.com",
    "iamcredentials.googleapis.com",
    "serviceusage.googleapis.com",
    "sts.googleapis.com",
  ])
  aws_assumed_role_arn_prefix = "arn:aws:sts::${var.aws_account_id}:assumed-role/${var.aws_runtime_role_name}/"
}

resource "google_project_service" "required" {
  for_each           = local.required_apis
  project            = var.expected_project_id
  service            = each.value
  disable_on_destroy = false
}

resource "google_service_account" "inference" {
  project      = var.expected_project_id
  account_id   = var.inference_service_account_id
  display_name = "Autobots dev Gemini inference"
  description  = "Used only by the AWS Autobots dev runtime through workload identity federation."

  depends_on = [google_project_service.required]
}

resource "google_project_iam_member" "inference" {
  project = var.expected_project_id
  role    = "roles/aiplatform.user"
  member  = "serviceAccount:${google_service_account.inference.email}"
}

resource "google_iam_workload_identity_pool" "aws" {
  project                   = var.expected_project_id
  workload_identity_pool_id = var.workload_pool_id
  display_name              = "Autobots dev AWS runtime"
  description               = "Federation restricted to the Autobots dev API EC2 role."
  disabled                  = false

  depends_on = [google_project_service.required]
}

resource "google_iam_workload_identity_pool_provider" "aws" {
  project                            = var.expected_project_id
  workload_identity_pool_id          = google_iam_workload_identity_pool.aws.workload_identity_pool_id
  workload_identity_pool_provider_id = var.workload_provider_id
  display_name                       = "Autobots dev API EC2 role"
  description                        = "Accepts only the named Autobots dev API role in the expected AWS account."
  attribute_condition                = "assertion.arn.startsWith('${local.aws_assumed_role_arn_prefix}')"

  attribute_mapping = {
    "google.subject"        = "assertion.arn"
    "attribute.aws_account" = "assertion.account"
    "attribute.aws_role"    = "assertion.arn"
  }

  aws {
    account_id = var.aws_account_id
  }
}

resource "google_service_account_iam_member" "aws_runtime" {
  service_account_id = google_service_account.inference.name
  role               = "roles/iam.workloadIdentityUser"
  member             = "principalSet://iam.googleapis.com/projects/${data.google_project.selected.number}/locations/global/workloadIdentityPools/${google_iam_workload_identity_pool.aws.workload_identity_pool_id}/attribute.aws_account/${var.aws_account_id}"
}

output "verified_project_id" {
  description = "The project resolved by the configured Google credentials."
  value       = data.google_project.selected.project_id
}

output "verified_project_number" {
  value = data.google_project.selected.number
}

output "inference_service_account_email" {
  value = google_service_account.inference.email
}

output "workload_identity_provider_name" {
  value = "projects/${data.google_project.selected.number}/locations/global/workloadIdentityPools/${google_iam_workload_identity_pool.aws.workload_identity_pool_id}/providers/${google_iam_workload_identity_pool_provider.aws.workload_identity_pool_provider_id}"
}
