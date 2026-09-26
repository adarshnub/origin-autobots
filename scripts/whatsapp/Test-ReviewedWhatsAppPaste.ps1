# Local-only checks. Clipboard and desktop input are never touched.
$ErrorActionPreference = 'Stop'
$helper = Join-Path $PSScriptRoot 'Invoke-ReviewedWhatsAppPaste.ps1'
$tempFile = [IO.Path]::GetTempFileName()
$expected = "Autobots draft test $([char]0x2014) do not send.`nSecond line: https://example.com/test?a=1&b=2"
$global:autobotsTestAnswer = 'NO'
$global:autobotsTestClipboardValues = @()

function Read-Host { param([string]$Prompt) return $global:autobotsTestAnswer }
function Set-Clipboard { param([string]$Value) $global:autobotsTestClipboardValues += $Value }

try {
    $tokens = $null
    $parseErrors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($helper, [ref]$tokens, [ref]$parseErrors) | Out-Null
    if ($parseErrors.Count -ne 0) { throw "Script parse failed: $($parseErrors[0].Message)" }

    [IO.File]::WriteAllText($tempFile, $expected + "`n", [Text.UTF8Encoding]::new($false))

    & $helper -Recipient 'Message yourself' -MessageFile $tempFile 6>$null | Out-Null
    if ($global:autobotsTestClipboardValues.Count -ne 0) { throw 'Cancel path changed the clipboard.' }

    $global:autobotsTestAnswer = 'COPY'
    & $helper -Recipient 'Message yourself' -MessageFile $tempFile 6>$null | Out-Null
    if ($global:autobotsTestClipboardValues.Count -ne 1 -or $global:autobotsTestClipboardValues[0] -cne $expected) {
        throw 'Confirmed path did not copy the exact UTF-8 multiline message.'
    }

    $invalidRecipientFailed = $false
    try { & $helper -Recipient "Wrong`nchat" -MessageFile $tempFile 6>$null | Out-Null }
    catch { $invalidRecipientFailed = $_.Exception.Message -match 'Recipient must be one line' }
    if (-not $invalidRecipientFailed) { throw 'Invalid recipient was accepted.' }

    [IO.File]::WriteAllText($tempFile, '   ', [Text.UTF8Encoding]::new($false))
    $blankMessageFailed = $false
    try { & $helper -Recipient 'Message yourself' -MessageFile $tempFile 6>$null | Out-Null }
    catch { $blankMessageFailed = $_.Exception.Message -match 'Message must contain' }
    if (-not $blankMessageFailed) { throw 'Blank message was accepted.' }

    [IO.File]::WriteAllText($tempFile, ('x' * 4097), [Text.UTF8Encoding]::new($false))
    $longMessageFailed = $false
    try { & $helper -Recipient 'Message yourself' -MessageFile $tempFile 6>$null | Out-Null }
    catch { $longMessageFailed = $_.Exception.Message -match 'Message must contain' }
    if (-not $longMessageFailed) { throw 'Oversize message was accepted.' }

    $missingFileFailed = $false
    try { & $helper -Recipient 'Message yourself' -MessageFile ($tempFile + '.missing') 6>$null | Out-Null }
    catch { $missingFileFailed = $true }
    if (-not $missingFileFailed) { throw 'Missing file was accepted.' }

    if ($global:autobotsTestClipboardValues.Count -ne 1) { throw 'A rejected input changed the clipboard.' }
    Write-Output 'PASS: parse, cancel, exact UTF-8 multiline copy, invalid recipient, blank and oversize message, missing file. Clipboard and WhatsApp untouched.'
}
finally {
    Remove-Item -LiteralPath $tempFile -ErrorAction SilentlyContinue
    Remove-Variable -Name autobotsTestAnswer,autobotsTestClipboardValues -Scope Global -ErrorAction SilentlyContinue
}
