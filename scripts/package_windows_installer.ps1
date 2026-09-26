param(
    [Parameter(Mandatory=$true)][string]$BundleDir,
    [Parameter(Mandatory=$true)][string]$Version,
    [switch]$Rebuild
)

$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must be major.minor.patch.' }
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$artifactRoot = Join-Path $repoRoot 'artifacts'
$bundle = (Resolve-Path -LiteralPath $BundleDir).Path
if (-not $bundle.StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The Windows bundle must be a workspace artifact.'
}
$app = Join-Path $bundle 'Autobots.Desktop.exe'
if (-not (Test-Path -LiteralPath $app)) { throw 'The Windows bundle is missing Autobots.Desktop.exe.' }
$dotnet = Join-Path $repoRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { throw 'The pinned .NET SDK is unavailable.' }
$project = Join-Path $repoRoot 'apps\installer\windows\Autobots.Setup.csproj'
$payload = Join-Path $artifactRoot "Autobots-Setup-$Version-payload.zip"
$output = Join-Path $artifactRoot "Autobots-Setup-$Version-build"
$installer = Join-Path $artifactRoot "Autobots-Setup-$Version.exe"
if (-not $Rebuild -and ((Test-Path -LiteralPath $installer) -or (Test-Path -LiteralPath $payload) -or (Test-Path -LiteralPath $output))) {
    throw 'The local versioned build already exists. Use -Rebuild only before publication.'
}

# Only the executable and user-facing install guide ship. Debug symbols stay local.
$files = @($app)
$guide = Join-Path $bundle 'INSTALL_WINDOWS.md'
if (Test-Path -LiteralPath $guide) { $files += $guide }
Compress-Archive -LiteralPath $files -DestinationPath $payload -CompressionLevel Optimal -Force
& $dotnet publish $project --configuration Release --runtime win-x64 --self-contained true `
    "-p:AutobotsPayloadZip=$payload" "-p:Version=$Version" `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    --output $output
if ($LASTEXITCODE -ne 0) { throw 'The Windows installer publish failed.' }
$built = Join-Path $output 'Autobots.Setup.exe'
if (-not (Test-Path -LiteralPath $built)) { throw 'The installer executable was not produced.' }
Copy-Item -LiteralPath $built -Destination $installer -Force
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$installer.sha256" -Value "$hash  $(Split-Path $installer -Leaf)" -Encoding ascii
Write-Output "Windows installer: $installer"
Write-Output "SHA-256: $hash"
Write-Output 'The installer adds Autobots to the current Windows user, Start menu and Installed Apps, then opens it.'
