$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$terraform = Join-Path $repoRoot ".tools\terraform\terraform.exe"
$awsDir = Join-Path $repoRoot "infra\aws\dev"
$requestPath = Join-Path $repoRoot ".tools\autobots-api-deploy-ssm-request.json"

if (-not (Test-Path -LiteralPath $terraform)) { throw "Pinned Terraform was not found at $terraform." }
$outputsJson = & $terraform "-chdir=$awsDir" output -json
if ($LASTEXITCODE -ne 0) { throw "AWS Terraform outputs are unavailable." }
$outputs = $outputsJson | ConvertFrom-Json
$expectedAccount = $outputs.verified_account_id.value
$instanceId = $outputs.api_instance_id.value
$region = $outputs.aws_region.value
$bucket = $outputs.artifact_bucket_name.value
$apiUrl = $outputs.api_base_url.value
if ([string]::IsNullOrWhiteSpace($instanceId) -or [string]::IsNullOrWhiteSpace($region) -or [string]::IsNullOrWhiteSpace($bucket) -or [string]::IsNullOrWhiteSpace($apiUrl)) {
    throw "AWS Terraform outputs are incomplete."
}

$activeAccount = & aws sts get-caller-identity --query Account --output text
if ($LASTEXITCODE -ne 0 -or $activeAccount -ne $expectedAccount) {
    throw "The active AWS identity does not match the deployed Autobots account."
}

$remoteCommand = @'
set -euo pipefail
bucket="@@BUCKET@@"
region="@@REGION@@"
stamp=$(date -u +%Y%m%dT%H%M%SZ)
backup="/var/lib/autobots/deploy-backup-$stamp"
stage="/var/tmp/autobots-deploy-$stamp"
mkdir -p "$backup" "$stage/source"
cp -a /opt/autobots/source/. "$backup/"
aws s3 cp "s3://$bucket/releases/autobots-api.zip" "$stage/api.zip" --region "$region" --no-progress
unzip -oq "$stage/api.zip" -d "$stage/source"
/opt/autobots/venv/bin/python -m compileall -q "$stage/source/services" "$stage/source/packages"
systemctl stop autobots-api
cp -a "$stage/source/." /opt/autobots/source/
chown -R autobots:autobots /opt/autobots/source
if systemctl start autobots-api; then
  for attempt in $(seq 1 20); do
    if curl --fail --silent http://127.0.0.1:8765/healthz >/dev/null 2>&1; then
      echo "deployment=ok local_health=ok"
      exit 0
    fi
    sleep 1
  done
fi
echo "deployment=failed; restoring previous source" >&2
systemctl stop autobots-api || true
cp -a "$backup/." /opt/autobots/source/
chown -R autobots:autobots /opt/autobots/source
systemctl start autobots-api
for attempt in $(seq 1 20); do
  if curl --fail --silent http://127.0.0.1:8765/healthz >/dev/null 2>&1; then break; fi
  sleep 1
done
exit 1
'@
$remoteCommand = $remoteCommand.Replace("@@BUCKET@@", $bucket).Replace("@@REGION@@", $region)

$request = @{
    DocumentName = "AWS-RunShellScript"
    InstanceIds = @($instanceId)
    Comment = "Deploy Autobots dev API source from the reviewed private S3 release"
    TimeoutSeconds = 150
    Parameters = @{ commands = @($remoteCommand) }
} | ConvertTo-Json -Depth 5 -Compress
[System.IO.File]::WriteAllText($requestPath, $request, [System.Text.UTF8Encoding]::new($false))
$commandId = & aws ssm send-command --region $region --cli-input-json "file://$requestPath" --query Command.CommandId --output text
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($commandId)) { throw "SSM could not start the API deployment." }

$invocation = $null
for ($attempt = 0; $attempt -lt 50; $attempt++) {
    Start-Sleep -Seconds 3
    $json = & aws ssm get-command-invocation --region $region --command-id $commandId --instance-id $instanceId --query "{status:Status,stdout:StandardOutputContent,stderr:StandardErrorContent}" --output json 2>&1
    if ($LASTEXITCODE -eq 0) {
        $invocation = $json | ConvertFrom-Json
        if ($invocation.status -in @("Success", "Failed", "TimedOut", "Cancelled")) { break }
    }
}
if ($null -eq $invocation) { throw "SSM did not return the API deployment result." }
if ($invocation.status -ne "Success" -or $invocation.stdout -notmatch "deployment=ok local_health=ok") {
    throw "API deployment did not pass its remote health check. Review the SSM invocation and preserved local backup."
}

$health = Invoke-RestMethod -Uri "$apiUrl/healthz" -Method Get -TimeoutSec 20
if ($health.status -ne "ok" -or $health.mode -ne "live") { throw "The deployed public API health check did not report live mode." }
Write-Output "API source deployment and public live health check succeeded."
