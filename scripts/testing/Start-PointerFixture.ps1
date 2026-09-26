param([Parameter(Mandatory = $true)][string]$OutputPath)

# Synthetic interactive target. It never sends input or touches user files/accounts.
$ErrorActionPreference = 'Stop'
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FixtureDpi {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
}
'@
[FixtureDpi]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()
$script:fixturePath = [IO.Path]::GetFullPath($OutputPath)
$script:clicks = [Collections.Generic.List[string]]::new()
$form = New-Object Windows.Forms.Form
$form.Text = 'Autobots pointer calibration — synthetic test'
$form.WindowState = 'Maximized'
$form.BackColor = [Drawing.Color]::FromArgb(245,247,252)
$form.Font = New-Object Drawing.Font('Segoe UI',14)
$form.AutoScaleMode = 'Dpi'
$title = New-Object Windows.Forms.Label
$title.Text = 'Pointer calibration · Click Target 1 through Target 9, then set Status to Ready and Finish.'
$title.Dock = 'Top'; $title.Height = 70; $title.TextAlign = 'MiddleCenter'
$form.Controls.Add($title)
$grid = New-Object Windows.Forms.TableLayoutPanel
$grid.Dock = 'Fill'; $grid.ColumnCount = 3; $grid.RowCount = 3; $grid.Padding = New-Object Windows.Forms.Padding(45,90,45,160)
for ($index=0; $index -lt 3; $index++) {
  $grid.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle('Percent',33.333))) | Out-Null
  $grid.RowStyles.Add((New-Object Windows.Forms.RowStyle('Percent',33.333))) | Out-Null
}
for ($index=1; $index -le 9; $index++) {
  $button = New-Object Windows.Forms.Button
  $button.Text = "Target $index"; $button.Name = "Target$index"; $button.Anchor = 'None'
  $button.Size = New-Object Drawing.Size(150,42)
  $button.Add_MouseDown({
    param($sender,$event)
    $point = $sender.PointToScreen($event.Location)
    $bounds = $sender.RectangleToScreen($sender.ClientRectangle)
    $record = @{at=[DateTimeOffset]::UtcNow.ToString('o');kind='target';target=$sender.Name;x=$point.X;y=$point.Y;left=$bounds.Left;top=$bounds.Top;width=$bounds.Width;height=$bounds.Height}
    [IO.File]::AppendAllText($script:fixturePath,($record | ConvertTo-Json -Compress)+[Environment]::NewLine)
    $script:clicks.Add($sender.Name)
    $sender.BackColor = [Drawing.Color]::LightGreen
  })
  $grid.Controls.Add($button, (($index-1)%3), [int][Math]::Floor(($index-1)/3))
}
$form.Controls.Add($grid); $grid.SendToBack()
$bottom = New-Object Windows.Forms.FlowLayoutPanel
$bottom.Dock='Bottom'; $bottom.Height=105; $bottom.Padding=New-Object Windows.Forms.Padding(35)
$label = New-Object Windows.Forms.Label; $label.Text='Status:'; $label.AutoSize=$true
$script:statusBox = New-Object Windows.Forms.TextBox; $script:statusBox.Text='Draft'; $script:statusBox.Width=220; $script:statusBox.AccessibleName='Status'
$finish = New-Object Windows.Forms.Button; $finish.Text='Finish'; $finish.Size=New-Object Drawing.Size(150,38)
$script:outcome = New-Object Windows.Forms.Label; $script:outcome.AutoSize=$true
$finish.Add_Click({
  $correct = (($script:clicks -join ',') -eq ((1..9 | ForEach-Object {"Target$_"}) -join ',')) -and $script:statusBox.Text -eq 'Ready'
  $script:outcome.Text = if ($correct) {'PASS — 9 ordered targets and Status Ready'} else {'INCOMPLETE — check target order and Status'}
  [IO.File]::AppendAllText($script:fixturePath,(@{at=[DateTimeOffset]::UtcNow.ToString('o');kind='finished';success=$correct;targets=@($script:clicks.ToArray());status=$script:statusBox.Text} | ConvertTo-Json -Compress)+[Environment]::NewLine)
})
$bottom.Controls.AddRange(@($label,$script:statusBox,$finish,$script:outcome)); $form.Controls.Add($bottom); $bottom.BringToFront()
[void]$form.ShowDialog()
