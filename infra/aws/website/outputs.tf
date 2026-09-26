output "website_url" {
  value       = "https://${var.custom_domain == "" ? aws_cloudfront_distribution.site.domain_name : var.custom_domain}"
  description = "Public HTTPS URL for the Autobots website."
}

output "cloudfront_url" {
  value       = "https://${aws_cloudfront_distribution.site.domain_name}"
  description = "Original AWS hostname; remains available after custom-domain attachment."
}

output "certificate_dns_records" {
  value = var.custom_domain == "" ? [] : [for record in aws_acm_certificate.site[0].domain_validation_options : {
    name  = record.resource_record_name
    type  = record.resource_record_type
    value = record.resource_record_value
  }]
  description = "Public certificate validation CNAMEs to retain at the existing DNS provider."
}

output "website_dns_record" {
  value = var.custom_domain == "" ? null : {
    name  = var.custom_domain
    type  = "CNAME"
    value = aws_cloudfront_distribution.site.domain_name
  }
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
