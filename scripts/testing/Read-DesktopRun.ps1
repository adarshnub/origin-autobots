param(
    [Parameter(Mandatory = $true)][int]$AppProcessId,
    [string]$LogPath,
    [switch]$IncludeHidden
)

# Read only the test app's exposed UI. Never inspect browser sessions or inject input.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$condition = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $AppProcessId)
$windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
    [System.Windows.Automation.TreeScope]::Children, $condition)
$windows = @($windows)
if ($IncludeHidden) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class QaOwnedComposer {
    public delegate bool EnumProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr parameter);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr window, StringBuilder text, int length);
    public static IntPtr Find(int processId) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((window, parameter) => {
            uint process; GetWindowThreadProcessId(window, out process);
            if (process != processId) return true;
            var text = new StringBuilder(128); GetWindowText(window, text, text.Capacity);
            if (text.ToString() == "Autobots") { found = window; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
'@
    $composerHandle = [QaOwnedComposer]::Find($AppProcessId)
    if ($composerHandle -ne [IntPtr]::Zero -and -not @($windows | Where-Object { $_.Current.Name -eq 'Autobots' }).Count) {
        $windows += [System.Windows.Automation.AutomationElement]::FromHandle($composerHandle)
    }
}
$textCondition = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Text)
$observed = @($windows | ForEach-Object {
    $window = $_
    [pscustomobject]@{
        window = $window.Current.Name
        text = @($window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCondition) |
            ForEach-Object { $_.Current.Name })
    }
})
$record = [pscustomobject]@{ observedAt = [DateTimeOffset]::UtcNow.ToString('o'); windows = $observed }
$json = $record | ConvertTo-Json -Depth 5 -Compress
if ($LogPath) {
    [IO.File]::AppendAllText($LogPath, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
}
$json
