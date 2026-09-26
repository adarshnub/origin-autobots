param([Parameter(Mandatory=$true)][string]$ZipPath)
$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$terraform = Join-Path $repoRoot ".tools\terraform\terraform.exe"
$tfDir = Join-Path $repoRoot "infra\aws\website"
$absoluteZip = (Resolve-Path -LiteralPath $ZipPath).Path
if (-not $absoluteZip.StartsWith((Join-Path $repoRoot "artifacts") + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "The pilot bundle must be a reviewed artifact under this workspace."
}
if ([IO.Path]::GetExtension($absoluteZip) -ne ".zip") { throw "Expected a ZIP artifact." }
$hash = (Get-FileHash -LiteralPath $absoluteZip -Algorithm SHA256).Hash.ToLowerInvariant()
$hashPath = "$absoluteZip.sha256"
if (-not (Test-Path -LiteralPath $hashPath)) { throw "The bundle SHA-256 sidecar is missing." }
$recorded = (Get-Content -LiteralPath $hashPath -Raw).Split(' ')[0].Trim().ToLowerInvariant()
if ($hash -ne $recorded) { throw "The bundle SHA-256 does not match its reviewed sidecar." }
$expected = & $terraform "-chdir=$tfDir" output -raw verified_account_id
$active = & aws sts get-caller-identity --query Account --output text
if ($LASTEXITCODE -ne 0 -or $expected.Trim() -ne $active.Trim()) { throw "AWS account mismatch." }
$bucket = & $terraform "-chdir=$tfDir" output -raw origin_bucket
if ($LASTEXITCODE -ne 0) { throw "Website bucket output unavailable." }
& aws s3 cp $absoluteZip "s3://$bucket/downloads/Autobots-Windows-Pilot.zip" --content-type "application/zip" --cache-control "no-cache,max-age=0" --only-show-errors
if ($LASTEXITCODE -ne 0) { throw "Pilot ZIP upload failed." }
& aws s3 cp $hashPath "s3://$bucket/downloads/Autobots-Windows-Pilot.zip.sha256" --content-type "text/plain" --cache-control "no-cache,max-age=0" --only-show-errors
if ($LASTEXITCODE -ne 0) { throw "Pilot SHA-256 upload failed." }
Write-Output "Published https://autobots.origin-studio.in/downloads/Autobots-Windows-Pilot.zip"
Write-Output "SHA-256 $hash"
