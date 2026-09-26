# Prepare an owner-reviewed WhatsApp message for a manual paste.
# No desktop input is injected outside Autobots' local supervisor and task lease.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$Recipient,

    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$MessageFile
)

$ErrorActionPreference = 'Stop'

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT -or -not [Environment]::UserInteractive) {
    throw 'This helper requires an interactive Windows desktop session.'
}
if ($Recipient.Length -gt 100 -or $Recipient -match '[\r\n]') {
    throw 'Recipient must be one line and at most 100 characters.'
}

$message = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $MessageFile).Path, [Text.Encoding]::UTF8).TrimEnd("`r", "`n")
if ([string]::IsNullOrWhiteSpace($message) -or $message.Length -gt 4096) {
    throw 'Message must contain 1 to 4096 nonblank characters.'
}

Write-Host ''
Write-Host "Recipient to verify in WhatsApp Desktop: $Recipient"
Write-Host 'Message to copy:'
Write-Host '------------------------------------------------------------'
Write-Host $message
Write-Host '------------------------------------------------------------'
Write-Host 'After copying, open the intended WhatsApp Desktop chat, click its composer and press Ctrl+V yourself.'
Write-Host 'Review the pasted text and recipient there before pressing Send.'
Write-Host ''

$confirmation = Read-Host 'Type COPY to put this exact message on the clipboard, or anything else to cancel'
if ($confirmation -cne 'COPY') {
    Write-Host 'Cancelled. Clipboard and WhatsApp were not changed.'
    return
}

Set-Clipboard -Value $message
Write-Host 'Message copied. Paste and review it in the intended WhatsApp chat; this helper did not touch WhatsApp.'
