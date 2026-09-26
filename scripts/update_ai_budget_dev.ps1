param(
    [Parameter(Mandatory = $true)][decimal]$PerTaskUsd,
    [Parameter(Mandatory = $true)][decimal]$PerDayUsd,
    [Parameter(Mandatory = $true)][decimal]$PerMonthUsd
)

$ErrorActionPreference = 'Stop'
if ($PerTaskUsd -le 0 -or $PerDayUsd -lt $PerTaskUsd -or $PerMonthUsd -lt $PerDayUsd) {
    throw 'Require positive model limits with per-task <= per-day <= per-month.'
}
$culture = [System.Globalization.CultureInfo]::InvariantCulture
$taskValue = $PerTaskUsd.ToString('0.00', $culture)
$dayValue = $PerDayUsd.ToString('0.00', $culture)
$monthValue = $PerMonthUsd.ToString('0.00', $culture)

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$terraform = Join-Path $repoRoot '.tools\terraform\terraform.exe'
$awsDir = Join-Path $repoRoot 'infra\aws\dev'
$requestPath = Join-Path $repoRoot '.tools\autobots-budget-ssm-request.json'
if (-not (Test-Path -LiteralPath $terraform)) { throw 'Pinned Terraform is unavailable.' }
$outputsJson = & $terraform "-chdir=$awsDir" output -json
if ($LASTEXITCODE -ne 0) { throw 'AWS Terraform outputs are unavailable.' }
$outputs = $outputsJson | ConvertFrom-Json
$expectedAccount = [string]$outputs.verified_account_id.value
$instanceId = [string]$outputs.api_instance_id.value
$region = [string]$outputs.aws_region.value
$activeAccount = & aws sts get-caller-identity --query Account --output text
if ($LASTEXITCODE -ne 0 -or $activeAccount -ne $expectedAccount -or
    [string]::IsNullOrWhiteSpace($instanceId) -or [string]::IsNullOrWhiteSpace($region)) {
    throw 'AWS account or existing Autobots instance could not be verified.'
}

$remoteCommand = @'
set -euo pipefail
config=/etc/autobots/autobots.env
backup=$(mktemp /etc/autobots/autobots.env.budget.XXXXXX)
cp -p "$config" "$backup"
trap 'status=$?; if [ "$status" -ne 0 ]; then cp -p "$backup" "$config"; systemctl restart autobots-api || true; fi; rm -f "$backup"' EXIT
python3 - "$config" <<'PY'
from pathlib import Path
import os
import stat
import sys
import tempfile

path = Path(sys.argv[1])
updates = {
    'AUTOBOTS_MAX_MODEL_COST_USD_PER_TASK': '@@TASK@@',
    'AUTOBOTS_MAX_MODEL_COST_USD_PER_DAY': '@@DAY@@',
    'AUTOBOTS_MAX_MODEL_COST_USD_PER_MONTH': '@@MONTH@@',
}
lines = path.read_text(encoding='utf-8').splitlines(keepends=True)
seen = {key: 0 for key in updates}
for index, line in enumerate(lines):
    key = line.partition('=')[0]
    if key in updates:
        seen[key] += 1
        lines[index] = f'{key}={updates[key]}\n'
if any(count != 1 for count in seen.values()):
    raise RuntimeError('Expected exactly one entry for each Autobots model budget setting')
original = path.stat()
handle, temporary = tempfile.mkstemp(prefix='.autobots-budget-', dir=str(path.parent))
try:
    with os.fdopen(handle, 'w', encoding='utf-8') as output:
        output.writelines(lines)
        output.flush()
        os.fsync(output.fileno())
    os.chown(temporary, original.st_uid, original.st_gid)
    os.chmod(temporary, stat.S_IMODE(original.st_mode))
    os.replace(temporary, path)
finally:
    if os.path.exists(temporary):
        os.unlink(temporary)
PY
systemctl restart autobots-api
for attempt in $(seq 1 20); do
  if curl --fail --silent http://127.0.0.1:8765/healthz >/dev/null 2>&1; then break; fi
  sleep 1
done
curl --fail --silent http://127.0.0.1:8765/healthz >/dev/null
pid=$(systemctl show autobots-api -p MainPID --value)
python3 - "$pid" <<'PY'
from pathlib import Path
import sys

environment = dict(item.split('=', 1) for item in Path(f'/proc/{sys.argv[1]}/environ').read_bytes().decode().split('\0') if '=' in item)
expected = {
    'AUTOBOTS_MAX_MODEL_COST_USD_PER_TASK': '@@TASK@@',
    'AUTOBOTS_MAX_MODEL_COST_USD_PER_DAY': '@@DAY@@',
    'AUTOBOTS_MAX_MODEL_COST_USD_PER_MONTH': '@@MONTH@@',
}
if any(environment.get(key) != value for key, value in expected.items()):
    raise RuntimeError('Running API process does not have the requested model limits')
print('runtime_model_budget=' + '/'.join(expected.values()))
PY
'@
$remoteCommand = $remoteCommand.Replace('@@TASK@@', $taskValue).Replace('@@DAY@@', $dayValue).Replace('@@MONTH@@', $monthValue)
$request = @{
    DocumentName = 'AWS-RunShellScript'
    InstanceIds = @($instanceId)
    Comment = 'Update approved Autobots dev model inference budget limits only'
    TimeoutSeconds = 120
    Parameters = @{ commands = @($remoteCommand) }
} | ConvertTo-Json -Depth 5 -Compress
[IO.File]::WriteAllText($requestPath, $request, [Text.UTF8Encoding]::new($false))
$commandId = & aws ssm send-command --region $region --cli-input-json "file://$requestPath" --query Command.CommandId --output text
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($commandId)) { throw 'Could not start the Autobots budget update.' }
$invocation = $null
for ($attempt = 0; $attempt -lt 45; $attempt++) {
    Start-Sleep -Seconds 2
    $resultJson = & aws ssm get-command-invocation --region $region --command-id $commandId --instance-id $instanceId --query '{status:Status,stdout:StandardOutputContent}' --output json 2>&1
    if ($LASTEXITCODE -eq 0) {
        $invocation = $resultJson | ConvertFrom-Json
        if ($invocation.status -in @('Success', 'Failed', 'TimedOut', 'Cancelled')) { break }
    }
}
if ($null -eq $invocation -or $invocation.status -ne 'Success' -or
    $invocation.stdout -notmatch [regex]::Escape("runtime_model_budget=$taskValue/$dayValue/$monthValue")) {
    throw 'The live budget update did not verify. The remote command restores its previous file on failure.'
}
Write-Output "Live Autobots model budget verified: `$$taskValue per task / `$$dayValue per UTC day / `$$monthValue per UTC month."
