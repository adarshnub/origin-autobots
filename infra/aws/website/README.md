# Public website infrastructure

This isolated Terraform root creates a private, versioned S3 origin and a public HTTPS CloudFront distribution. CloudFront uses origin access control; the bucket blocks all direct public access. The website hostname is `autobots.origin-studio.in`; the original `d39k5o9aaxn0fs.cloudfront.net` address remains available. DNS stays with GoDaddy, and the existing root-domain website is independent.

Set `expected_account_id` in an ignored `website.auto.tfvars` file before planning. Both AWS providers restrict operations to that account. Review the complete plan and confirm it changes only the website resources before applying. Terraform state remains local and ignored in this pilot, like the existing dev stack.

Build the website separately in `apps/website`. Upload `dist/assets/` with long-lived cache metadata and both HTML files with `no-cache`, then invalidate `/*` on the distribution. The deployment script in `scripts/deploy_website_aws.ps1` performs those upload and invalidation steps using the current Terraform outputs.

## Custom hostname and HTTPS

The optional `custom_domain` variable attaches one hostname to the existing distribution. An empty value retains the AWS hostname alone. For this deployment the local variables are:

```hcl
expected_account_id = "214422569127"
aws_region          = "ap-south-1"
custom_domain       = "autobots.origin-studio.in"
```

The `certificates` provider requests a DNS-validated, non-exportable ACM certificate in `us-east-1`, as [required for CloudFront viewer certificates](https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/cnames-and-https-requirements.html). The origin remains in `ap-south-1`. No hosted zone, nameserver delegation, domain purchase or paid dedicated-IP certificate support is configured.

For an initial hostname attachment, run these stages from the repository root using the existing AWS login:

1. Save a certificate-only bootstrap plan: `.tools/terraform/terraform.exe -chdir=infra/aws/website plan -target=aws_acm_certificate.site -out=certificate-bootstrap.tfplan -input=false`. Inspect its resource changes before applying the saved plan.
2. Read `terraform output -json certificate_dns_records` in this root. Add those public CNAME records in the existing registrar's DNS panel. In GoDaddy the name is relative to `origin-studio.in`, so remove that suffix from the record name. Retain the validation record for [ACM automatic renewal](https://docs.aws.amazon.com/acm/latest/userguide/dns-validation.html).
3. Save and review a full plan without targeting: `.tools/terraform/terraform.exe -chdir=infra/aws/website plan -out=custom-domain.tfplan -input=false`. Apply the reviewed saved plan. It waits for certificate validation and CloudFront deployment before completing. Targeting is only for the initial DNS bootstrap; routine operations use full plans.
4. Add the `website_dns_record` CNAME in GoDaddy: name `autobots`, value `d39k5o9aaxn0fs.cloudfront.net`. Keep the existing `@`, `www`, nameserver, mail and other unrelated records intact.

`website_url` returns the configured public hostname; `cloudfront_url` preserves the AWS address. Public DNS records are not credentials. Never put AWS credentials or registrar sessions into Terraform variables, source files or deployment output.
