# Paste a reviewed message into the already open WhatsApp Desktop composer.
# This script never presses Enter or clicks Send. It is intentionally interactive:
# a provider's require_confirmation response must never launch it automatically.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$Recipient,

    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$MessageFile,

    [ValidateRange(5, 120)]
    [int]$TimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'

if (-not $IsWindows -and $PSVersionTable.PSEdition -eq 'Core') {
    throw 'This helper requires an interactive Windows desktop session.'
}
if (-not [Environment]::UserInteractive) {
    throw 'This helper requires an interactive owner session.'
}
if ($Recipient.Length -gt 100 -or $Recipient -match '[\r\n]') {
    throw 'Recipient must be one line and at most 100 characters.'
}

$message = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $MessageFile).Path, [Text.Encoding]::UTF8).TrimEnd("`r", "`n")
if ([string]::IsNullOrWhiteSpace($message) -or $message.Length -gt 4096) {
    throw 'Message must contain 1 to 4096 nonblank characters.'
}

# A native hotkey is used because the owner must focus the intended chat's composer
# after reviewing the text. No target window is selected by a model or by this script.
if (-not ('AutobotsReviewedPaste' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

public static class AutobotsReviewedPaste
{
    private const int InputKeyboard = 1;
    private const uint KeyUp = 0x0002;
    private const ushort Ctrl = 0x11;
    private const ushort Alt = 0x12;
    private const ushort Shift = 0x10;
    private const ushort V = 0x56;
    private const ushort F12 = 0x7B;

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort Key;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    private static bool Down(int key) { return (GetAsyncKeyState(key) & 0x8000) != 0; }

    public static bool PasteHotkeyDown { get { return Down(Ctrl) && Down(Alt) && Down(Shift) && Down(F12); } }
    public static bool PasteHotkeyReleased { get { return !Down(Ctrl) && !Down(Alt) && !Down(Shift) && !Down(F12); } }

    public static bool TryGetWhatsAppForeground(out IntPtr window)
    {
        window = GetForegroundWindow();
        if (window == IntPtr.Zero) return false;
        uint processId;
        GetWindowThreadProcessId(window, out processId);
        if (processId == 0) return false;
        try
        {
            string name = Process.GetProcessById((int)processId).ProcessName;
            return name.Equals("WhatsApp", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("WhatsAppDesktop", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    private static Input Key(ushort code, bool up)
    {
        return new Input
        {
            Type = InputKeyboard,
            Data = new InputUnion { Keyboard = new KeyboardInput { Key = code, Flags = up ? KeyUp : 0 } }
        };
    }

    public static bool PasteIntoSameForegroundWindow(IntPtr expectedWindow)
    {
        IntPtr current;
        if (!TryGetWhatsAppForeground(out current) || current != expectedWindow) return false;
        Input[] inputs = { Key(Ctrl, false), Key(V, false), Key(V, true), Key(Ctrl, true) };
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Input))) == inputs.Length;
    }
}
'@
}

Write-Host ''
Write-Host "Recipient to verify in WhatsApp Desktop: $Recipient"
Write-Host 'Message to paste:'
Write-Host '------------------------------------------------------------'
Write-Host $message
Write-Host '------------------------------------------------------------'
Write-Host 'This helper only pastes. You must inspect the composer and press Send yourself.'
Write-Host 'The chat name is displayed for your review; WhatsApp does not expose it to this helper.'
Write-Host ''

$confirmation = Read-Host 'Type PASTE to continue, or anything else to cancel'
if ($confirmation -cne 'PASTE') {
    Write-Host 'Cancelled. Clipboard and WhatsApp were not changed.'
    return
}

Set-Clipboard -Value $message
Write-Host "Open the $Recipient chat in WhatsApp Desktop, click its message composer, then press Ctrl+Alt+Shift+F12 within $TimeoutSeconds seconds."
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
$targetWindow = [IntPtr]::Zero
while ([DateTime]::UtcNow -lt $deadline) {
    if ([AutobotsReviewedPaste]::PasteHotkeyDown) {
        if (-not [AutobotsReviewedPaste]::TryGetWhatsAppForeground([ref]$targetWindow)) {
            throw 'Paste cancelled: WhatsApp Desktop was not the foreground app.'
        }
        break
    }
    Start-Sleep -Milliseconds 40
}

if ($targetWindow -eq [IntPtr]::Zero) {
    throw 'Paste timed out. No keys were sent.'
}

$releaseDeadline = [DateTime]::UtcNow.AddSeconds(5)
while (-not [AutobotsReviewedPaste]::PasteHotkeyReleased -and [DateTime]::UtcNow -lt $releaseDeadline) {
    Start-Sleep -Milliseconds 40
}
if (-not [AutobotsReviewedPaste]::PasteHotkeyReleased) {
    throw 'Paste cancelled: release the hotkey before trying again.'
}
if (-not [AutobotsReviewedPaste]::PasteIntoSameForegroundWindow($targetWindow)) {
    throw 'Paste cancelled or Windows did not accept all input events. Check the composer before retrying.'
}

Write-Host 'Paste keys sent. Check the recipient and pasted text in WhatsApp, then press Send there.'
