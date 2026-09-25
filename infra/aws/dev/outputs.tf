output "verified_account_id" {
  description = "The AWS account resolved by the active AWS credentials."
  value       = data.aws_caller_identity.current.account_id
}

output "aws_region" {
  value = var.aws_region
}

output "api_base_url" {
  description = "TLS API endpoint for the Autobots dev backend."
  value       = "https://api.${aws_eip.api.public_ip}.${var.api_domain_suffix}"
}

output "api_instance_id" {
  value = aws_instance.api.id
}

output "api_public_ip" {
  value = aws_eip.api.public_ip
}

output "api_runtime_role_arn" {
  value = aws_iam_role.api.arn
}

output "cognito_user_pool_id" {
  value = aws_cognito_user_pool.owner.id
}

output "cognito_app_client_id" {
  value = aws_cognito_user_pool_client.desktop.id
}

output "cognito_hosted_ui_base_url" {
  value = "https://${aws_cognito_user_pool_domain.hosted_ui.domain}.auth.${var.aws_region}.amazoncognito.com"
}

output "cognito_callback_url" {
  value = "http://127.0.0.1:53682/auth/callback"
}

output "artifact_bucket_name" {
  value = aws_s3_bucket.artifacts.bucket
}
