param(
    [Parameter(Mandatory=$true)][string]$ZipPath,
    [Parameter(Mandatory=$true)][string]$NotesPath
)
$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$terraform = Join-Path $repoRoot ".tools\terraform\terraform.exe"
$tfDir = Join-Path $repoRoot "infra\aws\website"
$zip = (Resolve-Path -LiteralPath $ZipPath).Path
$notesFile = (Resolve-Path -LiteralPath $NotesPath).Path
$artifactRoot = Join-Path $repoRoot "artifacts"
if (-not $zip.StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Release ZIP must be a workspace artifact." }
if ([IO.Path]::GetExtension($zip) -ne ".zip") { throw "Release file must be a ZIP." }
if (-not $notesFile.StartsWith((Join-Path $repoRoot "releases\windows") + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Release notes must come from releases/windows." }
$notes = Get-Content -LiteralPath $notesFile -Raw | ConvertFrom-Json
$version = [string]$notes.version
if ($version -notmatch '^\d+\.\d+\.\d+$' -or $notes.platform -ne "windows" -or @($notes.changes).Count -lt 1) { throw "Release metadata is invalid." }
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$sidecar = "$zip.sha256"
if (-not (Test-Path -LiteralPath $sidecar) -or ((Get-Content -LiteralPath $sidecar -Raw).Split(' ')[0].Trim().ToLowerInvariant() -ne $hash)) { throw "Reviewed SHA-256 sidecar does not match the ZIP." }
$expected = & $terraform "-chdir=$tfDir" output -raw verified_account_id
$active = & aws sts get-caller-identity --query Account --output text
if ($LASTEXITCODE -ne 0 -or $active.Trim() -ne $expected.Trim()) { throw "AWS account mismatch." }
$bucket = & $terraform "-chdir=$tfDir" output -raw origin_bucket
$distribution = & $terraform "-chdir=$tfDir" output -raw distribution_id
if ($LASTEXITCODE -ne 0) { throw "Website storage outputs unavailable." }
$key = "downloads/windows/$version/Autobots-Windows-$version.zip"
$existing = & aws s3api list-objects-v2 --bucket $bucket --prefix $key --query 'Contents[].Key' --output json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw "Could not inspect existing release objects." }
if (@($existing) -contains $key) {
    $remoteHash = & aws s3api head-object --bucket $bucket --key $key --query 'Metadata.sha256' --output text
    if ($LASTEXITCODE -ne 0 -or $remoteHash -ne $hash) { throw "Immutable version already exists with a different or unverifiable hash." }
} else {
    & aws s3 cp $zip "s3://$bucket/$key" --content-type "application/zip" --cache-control "public,max-age=31536000,immutable" --metadata "sha256=$hash" --only-show-errors
    if ($LASTEXITCODE -ne 0) { throw "Versioned ZIP upload failed." }
}
$stableSidecar = Join-Path $artifactRoot "Autobots-Windows-$version.zip.sha256"
[IO.File]::WriteAllText($stableSidecar, "$hash  Autobots-Windows-$version.zip`n", [Text.UTF8Encoding]::new($false))
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
if ($previous.Count -eq 0) {
    $release = [ordered]@{ version=$version; platform="windows"; publishedAt=[string]$notes.publishedAt; headline=[string]$notes.headline; changes=@($notes.changes); sizeBytes=(Get-Item -LiteralPath $zip).Length }
    $releases = @($release) + $releases
}
$publicReleases = @($releases | ForEach-Object {
    [ordered]@{ version=[string]$_.version; platform=[string]$_.platform; publishedAt=[string]$_.publishedAt; headline=[string]$_.headline; changes=@($_.changes); sizeBytes=[long]$_.sizeBytes }
})
$catalogJson = @{ schemaVersion=1; releases=$publicReleases } | ConvertTo-Json -Depth 8
[IO.File]::WriteAllText($catalogPath, $catalogJson + "`n", [Text.UTF8Encoding]::new($false))
& aws s3 cp $catalogPath "s3://$bucket/$catalogKey" --content-type "application/json" --cache-control "no-cache,max-age=0" --only-show-errors
if ($LASTEXITCODE -ne 0) { throw "Catalog upload failed." }
& aws s3 cp "s3://$bucket/$key" "s3://$bucket/downloads/Autobots-Windows-Pilot.zip" --metadata-directive REPLACE --content-type "application/zip" --cache-control "no-cache,max-age=0" --only-show-errors
if ($LASTEXITCODE -ne 0) { throw "Latest-download alias update failed." }
& aws cloudfront create-invalidation --distribution-id $distribution --paths "/downloads/releases.json" "/downloads/Autobots-Windows-Pilot.zip" --query 'Invalidation.Id' --output text | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Release catalog invalidation failed." }
Write-Output "Release v${version}: https://autobots.origin-studio.in/$key"
Write-Output "SHA-256: $hash"
