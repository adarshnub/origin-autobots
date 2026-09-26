# Public website infrastructure

This isolated Terraform root creates a private, versioned S3 origin and a public HTTPS CloudFront distribution. CloudFront uses origin access control; the bucket blocks all direct public access. No domain or certificate is purchased. The distribution's generated `cloudfront.net` address is the shareable URL.

Set `expected_account_id` in an ignored `website.auto.tfvars` file before planning. Review the complete plan and confirm it changes only the website bucket, its configuration, origin access control, distribution, and bucket policy before applying. Terraform state remains local and ignored in this pilot, like the existing dev stack.

Build the website separately in `apps/website`. Upload `dist/assets/` with long-lived cache metadata and both HTML files with `no-cache`, then invalidate `/*` on the distribution. The deployment script in `scripts/deploy_website_aws.ps1` performs those upload and invalidation steps using the current Terraform outputs.
