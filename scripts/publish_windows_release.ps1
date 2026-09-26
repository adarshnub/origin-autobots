param(
    [Parameter(Mandatory=$true)][Alias('ZipPath')][string]$PackagePath,
    [Parameter(Mandatory=$true)][string]$NotesPath
)
$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$terraform = Join-Path $repoRoot ".tools\terraform\terraform.exe"
$tfDir = Join-Path $repoRoot "infra\aws\website"
$package = (Resolve-Path -LiteralPath $PackagePath).Path
$notesFile = (Resolve-Path -LiteralPath $NotesPath).Path
$artifactRoot = Join-Path $repoRoot "artifacts"
if (-not $package.StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Release package must be a workspace artifact." }
$extension = [IO.Path]::GetExtension($package).ToLowerInvariant()
if ($extension -notin @('.zip', '.exe')) { throw 'Release package must be a ZIP or installer EXE.' }
$assetType = if ($extension -eq '.exe') { 'installer' } else { 'zip' }
if (-not $notesFile.StartsWith((Join-Path $repoRoot "releases\windows") + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Release notes must come from releases/windows." }
$notes = Get-Content -LiteralPath $notesFile -Raw | ConvertFrom-Json
$version = [string]$notes.version
if ($version -notmatch '^\d+\.\d+\.\d+$' -or $notes.platform -ne "windows" -or @($notes.changes).Count -lt 1) { throw "Release metadata is invalid." }
$expectedFileName = if ($assetType -eq 'installer') { "Autobots-Setup-$version.exe" } else { "Autobots-Windows-$version.zip" }
if ([IO.Path]::GetFileName($package) -ne $expectedFileName) { throw "Release package must be named $expectedFileName." }
$hash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
$sidecar = "$package.sha256"
if (-not (Test-Path -LiteralPath $sidecar) -or ((Get-Content -LiteralPath $sidecar -Raw).Split(' ')[0].Trim().ToLowerInvariant() -ne $hash)) { throw "Reviewed SHA-256 sidecar does not match the package." }
$expected = & $terraform "-chdir=$tfDir" output -raw verified_account_id
$active = & aws sts get-caller-identity --query Account --output text
if ($LASTEXITCODE -ne 0 -or $active.Trim() -ne $expected.Trim()) { throw "AWS account mismatch." }
$bucket = & $terraform "-chdir=$tfDir" output -raw origin_bucket
$distribution = & $terraform "-chdir=$tfDir" output -raw distribution_id
if ($LASTEXITCODE -ne 0) { throw "Website storage outputs unavailable." }
$key = "downloads/windows/$version/$expectedFileName"
$existing = & aws s3api list-objects-v2 --bucket $bucket --prefix $key --query 'Contents[].Key' --output json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw "Could not inspect existing release objects." }
if (@($existing) -contains $key) {
    $remoteHash = & aws s3api head-object --bucket $bucket --key $key --query 'Metadata.sha256' --output text
    if ($LASTEXITCODE -ne 0 -or $remoteHash -ne $hash) { throw "Immutable version already exists with a different or unverifiable hash." }
} else {
    $contentType = if ($assetType -eq 'installer') { 'application/octet-stream' } else { 'application/zip' }
    & aws s3 cp $package "s3://$bucket/$key" --content-type $contentType --content-disposition "attachment; filename=$expectedFileName" --cache-control "public,max-age=31536000,immutable" --metadata "sha256=$hash" --only-show-errors
    if ($LASTEXITCODE -ne 0) { throw "Versioned package upload failed." }
}
$catalogKey = "downloads/releases.json"
$listed = & aws s3api list-objects-v2 --bucket $bucket --prefix $catalogKey --query 'Contents[].Key' --output json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw "Could not inspect release catalog." }
$catalogPath = Join-Path $artifactRoot "windows-releases-catalog.json"
if (@($listed) -contains $catalogKey) {
    & aws s3 cp "s3://$bucket/$catalogKey" $catalogPath --only-show-errors
    if ($LASTEXITCODE -ne 0) { throw "Could not read release catalog." }
    $catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json
    if ($catalog.schemaVersion -ne 1) { throw "Unknown release catalog schema." }
    $releases = @($catalog.releases)
} else { $releases = @() }
$previous = @($releases | Where-Object { $_.version -eq $version })
if ($previous.Count -ne 0 -and $(if ($previous[0].assetType) { [string]$previous[0].assetType } else { 'zip' }) -ne $assetType) {
    throw 'The existing version is already published in a different package format.'
}
if ($previous.Count -eq 0) {
    $release = [ordered]@{ version=$version; platform="windows"; assetType=$assetType; publishedAt=[string]$notes.publishedAt; headline=[string]$notes.headline; changes=@($notes.changes); sizeBytes=(Get-Item -LiteralPath $package).Length }
    $releases = @($release) + $releases
}
$publicReleases = @($releases | ForEach-Object {
    [ordered]@{ version=[string]$_.version; platform=[string]$_.platform; assetType=$(if ($_.assetType) { [string]$_.assetType } else { 'zip' }); publishedAt=[string]$_.publishedAt; headline=[string]$_.headline; changes=@($_.changes); sizeBytes=[long]$_.sizeBytes }
})
$catalogJson = @{ schemaVersion=1; releases=$publicReleases } | ConvertTo-Json -Depth 8
[IO.File]::WriteAllText($catalogPath, $catalogJson + "`n", [Text.UTF8Encoding]::new($false))
& aws s3 cp $catalogPath "s3://$bucket/$catalogKey" --content-type "application/json" --cache-control "no-cache,max-age=0" --only-show-errors
if ($LASTEXITCODE -ne 0) { throw "Catalog upload failed." }
$latestAlias = if ($assetType -eq 'installer') { 'downloads/Autobots-Setup.exe' } else { 'downloads/Autobots-Windows-Pilot.zip' }
$contentType = if ($assetType -eq 'installer') { 'application/octet-stream' } else { 'application/zip' }
& aws s3 cp "s3://$bucket/$key" "s3://$bucket/$latestAlias" --metadata-directive REPLACE --content-type $contentType --content-disposition "attachment; filename=$expectedFileName" --cache-control "no-cache,max-age=0" --only-show-errors
if ($LASTEXITCODE -ne 0) { throw "Latest-download alias update failed." }
& aws cloudfront create-invalidation --distribution-id $distribution --paths "/downloads/releases.json" "/$latestAlias" --query 'Invalidation.Id' --output text | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Release catalog invalidation failed." }
Write-Output "Release v${version}: https://autobots.origin-studio.in/$key"
Write-Output "SHA-256: $hash"
