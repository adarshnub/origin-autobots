param(
    [Parameter(Mandatory = $true)][int]$AppProcessId,
    [string]$LogPath
)

# Read only the test app's exposed UI. Never inspect browser sessions or inject input.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$condition = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $AppProcessId)
$windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
    [System.Windows.Automation.TreeScope]::Children, $condition)
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
