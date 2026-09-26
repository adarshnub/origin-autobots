param(
    [string]$TerraformPath = "infra/aws/website",
    [string]$DistPath = "apps/website/dist"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$terraform = Join-Path $repo ".tools/terraform/terraform.exe"
$tfDir = (Resolve-Path (Join-Path $repo $TerraformPath)).Path
$dist = (Resolve-Path (Join-Path $repo $DistPath)).Path

if (-not (Test-Path (Join-Path $dist "index.html"))) { throw "Built website index.html is missing." }
if (-not (Test-Path (Join-Path $dist "developer/index.html"))) { throw "Built developer page is missing." }
if (-not (Test-Path (Join-Path $dist "pilot/index.html"))) { throw "Built pilot portal is missing." }
if (-not (Test-Path (Join-Path $dist "downloads/index.html"))) { throw "Built downloads page is missing." }
if (-not (Test-Path (Join-Path $dist "assets"))) { throw "Built website assets are missing." }

$bucket = & $terraform "-chdir=$tfDir" output -raw origin_bucket
if ($LASTEXITCODE -ne 0) { throw "Could not read website origin bucket from Terraform." }
$distribution = & $terraform "-chdir=$tfDir" output -raw distribution_id
if ($LASTEXITCODE -ne 0) { throw "Could not read website distribution from Terraform." }
$expectedAccount = & $terraform "-chdir=$tfDir" output -raw verified_account_id
if ($LASTEXITCODE -ne 0) { throw "Could not read verified AWS account from Terraform." }
$currentAccount = aws sts get-caller-identity --query Account --output text
if ($LASTEXITCODE -ne 0 -or $currentAccount.Trim() -ne $expectedAccount.Trim()) { throw "AWS account does not match the planned website stack." }

aws s3 sync (Join-Path $dist "assets") "s3://$bucket/assets/" --cache-control "public,max-age=31536000,immutable" --only-show-errors
if ($LASTEXITCODE -ne 0) { throw "Asset upload failed." }
aws s3 cp (Join-Path $dist "index.html") "s3://$bucket/index.html" --content-type "text/html; charset=utf-8" --cache-control "no-cache,max-age=0" --only-show-errors
if ($LASTEXITCODE -ne 0) { throw "Landing page upload failed." }
aws s3 cp (Join-Path $dist "developer/index.html") "s3://$bucket/developer/index.html" --content-type "text/html; charset=utf-8" --cache-control "no-cache,max-age=0" --only-show-errors
if ($LASTEXITCODE -ne 0) { throw "Developer page upload failed." }
aws s3 cp (Join-Path $dist "pilot/index.html") "s3://$bucket/pilot/index.html" --content-type "text/html; charset=utf-8" --cache-control "no-cache,max-age=0" --only-show-errors
if ($LASTEXITCODE -ne 0) { throw "Pilot portal upload failed." }
aws s3 cp (Join-Path $dist "downloads/index.html") "s3://$bucket/downloads/index.html" --content-type "text/html; charset=utf-8" --cache-control "no-cache,max-age=0" --only-show-errors
if ($LASTEXITCODE -ne 0) { throw "Downloads page upload failed." }

aws cloudfront create-invalidation --distribution-id $distribution --paths "/*" --query "Invalidation.{Id:Id,Status:Status}" --output json
if ($LASTEXITCODE -ne 0) { throw "CloudFront invalidation failed." }

& $terraform "-chdir=$tfDir" output -raw website_url
if ($LASTEXITCODE -ne 0) { throw "Could not read website URL from Terraform." }
