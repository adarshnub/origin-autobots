locals {
  api_release_path    = "${path.root}/../../../artifacts/autobots-api.zip"
  gcp_credential_path = "${path.root}/../../../artifacts/google-aws-wif.json"
}

resource "aws_s3_bucket" "artifacts" {
  bucket_prefix = "autobots-dev-${var.expected_account_id}-"
  force_destroy = false
  tags          = { Name = "autobots-dev-artifacts-and-backups" }
}

resource "aws_s3_bucket_public_access_block" "artifacts" {
  bucket                  = aws_s3_bucket.artifacts.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_server_side_encryption_configuration" "artifacts" {
  bucket = aws_s3_bucket.artifacts.id
  rule {
    apply_server_side_encryption_by_default { sse_algorithm = "AES256" }
  }
}

resource "aws_s3_bucket_versioning" "artifacts" {
  bucket = aws_s3_bucket.artifacts.id
  versioning_configuration { status = "Enabled" }
}

resource "aws_s3_bucket_lifecycle_configuration" "artifacts" {
  bucket = aws_s3_bucket.artifacts.id

  rule {
    id     = "release-objects"
    status = "Enabled"
    filter { prefix = "releases/" }
    expiration { days = 30 }
    noncurrent_version_expiration { noncurrent_days = 30 }
  }
  rule {
    id     = "daily-database-backups"
    status = "Enabled"
    filter { prefix = "backups/" }
    expiration { days = 30 }
    noncurrent_version_expiration { noncurrent_days = 30 }
  }
  rule {
    id     = "bootstrap-configuration"
    status = "Enabled"
    filter { prefix = "config/" }
    expiration { days = 30 }
    noncurrent_version_expiration { noncurrent_days = 30 }
  }
}

resource "aws_s3_bucket_policy" "artifacts_tls_only" {
  bucket = aws_s3_bucket.artifacts.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Sid       = "DenyInsecureTransport"
      Effect    = "Deny"
      Principal = "*"
      Action    = "s3:*"
      Resource  = [aws_s3_bucket.artifacts.arn, "${aws_s3_bucket.artifacts.arn}/*"]
      Condition = { Bool = { "aws:SecureTransport" = "false" } }
    }]
  })
}

resource "aws_s3_object" "api_release" {
  bucket       = aws_s3_bucket.artifacts.id
  key          = "releases/autobots-api.zip"
  source       = local.api_release_path
  source_hash  = filebase64sha256(local.api_release_path)
  content_type = "application/zip"
  depends_on   = [aws_s3_bucket_server_side_encryption_configuration.artifacts]
}

resource "aws_s3_object" "google_credentials" {
  bucket       = aws_s3_bucket.artifacts.id
  key          = "config/google-aws-wif.json"
  source       = local.gcp_credential_path
  source_hash  = filebase64sha256(local.gcp_credential_path)
  content_type = "application/json"
  depends_on   = [aws_s3_bucket_server_side_encryption_configuration.artifacts]
}
