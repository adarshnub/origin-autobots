$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$terraform = Join-Path $repoRoot ".tools\terraform\terraform.exe"
$awsDir = Join-Path $repoRoot "infra\aws\dev"
$awsOutput = & $terraform "-chdir=$awsDir" output -json
if ($LASTEXITCODE -ne 0) { throw "The AWS Terraform state is unavailable." }
$outputs = $awsOutput | ConvertFrom-Json
$instanceId = $outputs.api_instance_id.value
$region = $outputs.aws_region.value
if ([string]::IsNullOrWhiteSpace($instanceId) -or [string]::IsNullOrWhiteSpace($region)) { throw "The deployed API instance outputs are incomplete." }

$pythonCode = 'import google.auth; from google.auth.transport.requests import Request; c,p=google.auth.default(scopes=["https://www.googleapis.com/auth/cloud-platform"]); c.refresh(Request()); print("wif_token_refresh=ok")'
$commandText = "set -a; source /etc/autobots/autobots.env; set +a; /opt/autobots/venv/bin/python -c '$pythonCode'"
$request = @{
    DocumentName = "AWS-RunShellScript"
    InstanceIds = @($instanceId)
    Comment = "Autobots read-only GCP workload identity check"
    TimeoutSeconds = 60
    Parameters = @{ commands = @($commandText) }
} | ConvertTo-Json -Depth 5 -Compress
$requestPath = Join-Path $repoRoot ".tools\autobots-wif-ssm-request.json"
[System.IO.File]::WriteAllText($requestPath, $request, [System.Text.UTF8Encoding]::new($false))
$commandId = & aws ssm send-command --region $region --cli-input-json "file://$requestPath" --query Command.CommandId --output text
if ($LASTEXITCODE -ne 0) { throw "SSM could not start the read-only workload identity check." }

$invocation = $null
for ($attempt = 0; $attempt -lt 30; $attempt++) {
    Start-Sleep -Seconds 3
    $json = & aws ssm get-command-invocation --region $region --command-id $commandId --instance-id $instanceId --query "{status:Status,stdout:StandardOutputContent}" --output json 2>&1
    if ($LASTEXITCODE -eq 0) {
        $invocation = $json | ConvertFrom-Json
        if ($invocation.status -in @("Success", "Failed", "TimedOut", "Cancelled")) { break }
    }
}
if ($null -eq $invocation) { throw "SSM did not return a workload identity check result." }
if ($invocation.status -ne "Success" -or $invocation.stdout -notmatch "wif_token_refresh=ok") {
    throw "The runtime workload identity check failed. Review the Autobots instance SSM invocation details."
}
Write-Output "EC2 role exchanged AWS runtime credentials for a short-lived GCP token successfully."
