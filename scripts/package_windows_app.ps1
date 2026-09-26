param(
    [string]$ReleaseName = "Autobots-Windows",
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$terraform = Join-Path $repoRoot ".tools\terraform\terraform.exe"
$dotnet = Join-Path $repoRoot ".tools\dotnet\dotnet.exe"
$awsDir = Join-Path $repoRoot "infra\aws\dev"
$project = Join-Path $repoRoot "apps\desktop\shell\Autobots.Desktop\Autobots.Desktop.csproj"

if (-not (Test-Path -LiteralPath $terraform)) { throw "Pinned Terraform was not found at $terraform." }
if (-not (Test-Path -LiteralPath $dotnet)) { throw "Pinned .NET SDK was not found at $dotnet." }

$outputJson = & $terraform "-chdir=$awsDir" output -json
if ($LASTEXITCODE -ne 0) { throw "Terraform outputs are unavailable. Apply the reviewed dev plan first." }
$outputs = $outputJson | ConvertFrom-Json
$apiUrl = $outputs.api_base_url.value
$hostedUiUrl = $outputs.cognito_hosted_ui_base_url.value
$clientId = $outputs.cognito_app_client_id.value
if ([string]::IsNullOrWhiteSpace($apiUrl) -or [string]::IsNullOrWhiteSpace($hostedUiUrl) -or [string]::IsNullOrWhiteSpace($clientId)) {
    throw "Terraform did not return the API and Cognito settings required by the desktop app."
}

$artifactRoot = Join-Path $repoRoot "artifacts"
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
$bundleDir = Join-Path $artifactRoot ("{0}-{1}" -f $ReleaseName, (Get-Date -Format "yyyyMMdd-HHmmss"))
New-Item -ItemType Directory -Path $bundleDir | Out-Null

[string[]]$restoreArguments = @()
if ($NoRestore) { $restoreArguments += "--no-restore" }
& $dotnet publish $project --configuration Release --runtime win-x64 --self-contained true @restoreArguments `
    -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:IncludeNativeLibrariesForSelfExtract=true `
    --output $bundleDir
if ($LASTEXITCODE -ne 0) { throw "The Windows desktop publish failed." }

$desktopConfig = [ordered]@{
    apiBaseUrl = $apiUrl
    cognitoHostedUiBaseUrl = $hostedUiUrl
    cognitoClientId = $clientId
}
$desktopConfig | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $bundleDir "autobots.dev.json") -Encoding utf8
Copy-Item -LiteralPath (Join-Path $repoRoot "docs\INSTALL_WINDOWS.md") -Destination $bundleDir

$zipPath = Join-Path $artifactRoot ("{0}-{1}.zip" -f $ReleaseName, (Get-Date -Format "yyyyMMdd-HHmmss"))
Compress-Archive -Path (Join-Path $bundleDir "*") -DestinationPath $zipPath -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$zipPath.sha256" -Value "$hash  $(Split-Path $zipPath -Leaf)" -Encoding ascii
Write-Output "Windows bundle: $zipPath"
Write-Output "SHA-256: $hash"
Write-Output "Runtime settings contain public API and Cognito identifiers only. Publish reviewed builds through scripts/publish_windows_release.ps1."
