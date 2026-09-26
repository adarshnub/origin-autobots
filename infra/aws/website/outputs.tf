output "website_url" {
  value       = "https://${aws_cloudfront_distribution.site.domain_name}"
  description = "Public HTTPS URL for the Autobots website."
}

output "distribution_id" {
  value = aws_cloudfront_distribution.site.id
}

output "origin_bucket" {
  value = aws_s3_bucket.site.bucket
}

output "verified_account_id" {
  value = data.aws_caller_identity.current.account_id
}
