resource "aws_acm_certificate" "site" {
  count             = var.custom_domain == "" ? 0 : 1
  provider          = aws.certificates
  domain_name       = var.custom_domain
  validation_method = "DNS"
  key_algorithm     = "RSA_2048"

  options {
    certificate_transparency_logging_preference = "ENABLED"
    export                                     = "DISABLED"
  }

  lifecycle { create_before_destroy = true }
  tags = { Name = "autobots-public-website-tls" }
}

# DNS validation CNAMEs are entered in the existing GoDaddy zone. No hosted zone
# or nameserver delegation is created by this website root.
resource "aws_acm_certificate_validation" "site" {
  count           = var.custom_domain == "" ? 0 : 1
  provider        = aws.certificates
  certificate_arn = aws_acm_certificate.site[0].arn
  validation_record_fqdns = [
    for record in aws_acm_certificate.site[0].domain_validation_options : record.resource_record_name
  ]
}
