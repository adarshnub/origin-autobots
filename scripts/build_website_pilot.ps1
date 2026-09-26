$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$terraform = Join-Path $repoRoot ".tools\terraform\terraform.exe"
$dev = Join-Path $repoRoot "infra\aws\dev"
$env:VITE_AUTOBOTS_API_URL = & $terraform "-chdir=$dev" output -raw api_base_url
if ($LASTEXITCODE -ne 0) { throw "Could not read API URL." }
$env:VITE_COGNITO_HOSTED_UI_URL = & $terraform "-chdir=$dev" output -raw cognito_hosted_ui_base_url
if ($LASTEXITCODE -ne 0) { throw "Could not read Cognito domain." }
$env:VITE_COGNITO_CLIENT_ID = & $terraform "-chdir=$dev" output -raw cognito_app_client_id
if ($LASTEXITCODE -ne 0) { throw "Could not read Cognito app client." }
Push-Location (Join-Path $repoRoot "apps\website")
try {
    & npm.cmd run build
    if ($LASTEXITCODE -ne 0) { throw "Website build failed." }
} finally { Pop-Location }
