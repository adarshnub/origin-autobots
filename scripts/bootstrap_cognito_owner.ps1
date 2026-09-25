$ErrorActionPreference = "Stop"
Set-Variable -Name PSNativeCommandUseErrorActionPreference -Value $false -Scope Local -ErrorAction SilentlyContinue
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$terraform = Join-Path $repoRoot ".tools\terraform\terraform.exe"
$awsDir = Join-Path $repoRoot "infra\aws\dev"
$variablesPath = Join-Path $awsDir "dev.auto.tfvars"
$ownerMatch = Select-String -LiteralPath $variablesPath -Pattern '^owner_email\s*=\s*"([^"]+)"$'
if (-not $ownerMatch) { throw "The private AWS dev variables do not contain the configured owner email." }
$ownerEmail = $ownerMatch.Matches[0].Groups[1].Value
$poolId = & $terraform "-chdir=$awsDir" output -raw cognito_user_pool_id
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($poolId)) { throw "The Cognito user pool is not deployed." }
$region = & $terraform "-chdir=$awsDir" output -raw aws_region
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($region)) { throw "The AWS region output is unavailable." }

$ErrorActionPreference = "Continue"
$existingOutput = & aws cognito-idp admin-get-user --user-pool-id $poolId --username $ownerEmail --region $region --query Username --output text 2>&1
$getExitCode = $LASTEXITCODE
if ($getExitCode -ne 0) {
    $null = & aws cognito-idp admin-create-user --user-pool-id $poolId --username $ownerEmail `
        --user-attributes "Name=email,Value=$ownerEmail" "Name=email_verified,Value=true" `
        --desired-delivery-mediums EMAIL --region $region --query User.Enabled --output text 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Cognito could not create the owner account or send its email invitation. Check AWS permissions and Cognito email delivery." }
    $invitationSent = $true
} else {
    $invitationSent = $false
}

$groupsJson = & aws cognito-idp admin-list-groups-for-user --user-pool-id $poolId --username $ownerEmail --region $region --output json 2>&1
if ($LASTEXITCODE -ne 0) { throw "Cognito could not read owner group membership." }
$groupNames = @(($groupsJson | ConvertFrom-Json).Groups | ForEach-Object { $_.GroupName })
if ($groupNames -notcontains "autobots-owners") {
    $null = & aws cognito-idp admin-add-user-to-group --user-pool-id $poolId --username $ownerEmail --group-name autobots-owners --region $region 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Cognito could not assign the owner-only API group." }
}
$verifiedGroupsJson = & aws cognito-idp admin-list-groups-for-user --user-pool-id $poolId --username $ownerEmail --region $region --output json 2>&1
$verifiedGroupsExitCode = $LASTEXITCODE
$verifiedGroupNames = @(($verifiedGroupsJson | ConvertFrom-Json).Groups | ForEach-Object { $_.GroupName })
if ($verifiedGroupsExitCode -ne 0 -or $verifiedGroupNames -notcontains "autobots-owners") {
    throw "Cognito did not confirm the owner-only API group assignment."
}

if ($invitationSent) {
    Write-Output "Owner account created and invitation sent to the configured email address. Choose a new password at first sign-in."
} else {
    Write-Output "Owner account already exists and is in the owner-only API group."
}
