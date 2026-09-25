terraform {
  required_version = ">= 1.9.0, < 2.0.0"

  required_providers {
    google = {
      source  = "hashicorp/google"
      version = "= 8.2.0"
    }
  }
}

provider "google" {
  project = var.expected_project_id
  region  = var.model_location
}
