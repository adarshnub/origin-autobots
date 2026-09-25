# Infrastructure scaffold

AWS remains the control plane and Google Cloud remains the model provider. The approved account IDs, billing project, owner email, domains and allowed cloud spend are currently unset.

The current `infra/aws/dev` and `infra/gcp/dev` configurations are read-only identity preflights with pinned providers and explicit expected-account/project inputs. They create no resources. Copy the example inputs to a local ignored tfvars file with real values, then inspect `terraform plan` before adding the billable owner-only dev stack.

The next infrastructure milestone adds private networking, SSM-only EC2 administration, encrypted database storage, Cognito, private object storage, GCP workload identity federation and budget alarms. Require expected-account checks and inspect the full plan before any apply. Remote state/backends must be reviewed before the first real apply.

No Terraform is installed in the current environment, no credentials have been examined, and no cloud resources have been created or modified. Do not place real values in committed tfvars.
