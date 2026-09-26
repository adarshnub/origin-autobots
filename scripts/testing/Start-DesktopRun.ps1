param(
    [Parameter(Mandatory = $true)][int]$AppProcessId,
    [Parameter(Mandatory = $true)][string]$InstructionPath,
    [switch]$ConfirmLive
)

# Operator-only helper for an explicitly authorized live QA session. Default is disarmed.
# Uses the product composer/Start control: authentication, local grant, lease and STOP still apply.
$ErrorActionPreference = 'Stop'
if (-not $ConfirmLive) { throw 'Disarmed. A live QA session must be explicitly authorized and -ConfirmLive supplied.' }
if ((Get-Process -Id $AppProcessId).ProcessName -ne 'Autobots.Desktop') { throw 'The target is not Autobots.' }
$instruction = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $InstructionPath).Path).Trim()
if ([string]::IsNullOrWhiteSpace($instruction) -or $instruction.Length -gt 4000) { throw 'Invalid task instruction length.' }
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$appWindow = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
    [System.Windows.Automation.TreeScope]::Children,
    [System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $AppProcessId),
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, 'Autobots')))
if ($null -eq $appWindow) { throw 'Open the idle Autobots composer before submitting a task.' }
function Find-AppControl([string]$name) {
    $control = $appWindow.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $name))
    if ($null -eq $control) { throw "Missing product control: $name" }
    return $control
}
$start = Find-AppControl 'Start task'
if (-not $start.Current.IsEnabled) { throw 'Start is disabled. Do not interrupt an existing task.' }
$composer = Find-AppControl 'Task instruction'
$value = [System.Windows.Automation.ValuePattern]$composer.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
$value.SetValue($instruction)
if ($value.Current.Value -ne $instruction) { throw 'Composer did not accept the complete instruction.' }
try {
    ([System.Windows.Automation.InvokePattern]$start.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Write-Output 'Submitted once through the product UI. Observe the task log for execution/result.'
} catch {
    throw 'Start returned an automation error. Inspect the app/task log before retrying; the task may already have started.'
}
