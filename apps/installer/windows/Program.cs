using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using Microsoft.Win32;

namespace Autobots.Setup;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new SetupWindow());
    }
}

internal sealed class SetupWindow : Form
{
    private readonly Label _status = new()
    {
        AutoSize = false,
        Left = 28,
        Top = 93,
        Width = 430,
        Height = 54,
        Text = "Preparing your installation…",
        ForeColor = Color.FromArgb(185, 201, 211),
        Font = new Font("Segoe UI", 10)
    };
    private readonly ProgressBar _progress = new()
    {
        Left = 28,
        Top = 160,
        Width = 430,
        Height = 16,
        Style = ProgressBarStyle.Marquee,
        MarqueeAnimationSpeed = 24
    };
    private bool _installing = true;

    public SetupWindow()
    {
        Text = "Install Autobots";
        ClientSize = new Size(486, 207);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(13, 24, 32);
        ForeColor = Color.White;
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty);
        Controls.Add(new Label
        {
            Text = "Autobots is moving in.",
            Left = 28,
            Top = 28,
            Width = 430,
            Height = 42,
            ForeColor = Color.FromArgb(219, 255, 112),
            Font = new Font("Segoe UI", 19, FontStyle.Bold)
        });
        Controls.Add(_status);
        Controls.Add(_progress);
        Shown += async (_, _) => await InstallOnOpenAsync();
        FormClosing += (_, e) => { if (_installing) e.Cancel = true; };
    }

    private async Task InstallOnOpenAsync()
    {
        try
        {
            var executable = await Task.Run(Install);
            _status.Text = "Installed. Opening Autobots…";
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Value = 100;
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
            _installing = false;
            Close();
        }
        catch (Exception error)
        {
            _installing = false;
            _progress.Visible = false;
            _status.Text = "Installation could not finish. " + error.Message;
            MessageBox.Show(this, _status.Text, "Autobots setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string Install()
    {
        if (Process.GetProcessesByName("Autobots.Desktop").Length != 0)
            throw new InvalidOperationException("Close Autobots from its tray menu, then open this installer again.");

        var parent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Origin Studios");
        var installDir = Path.Combine(parent, "Autobots");
        Directory.CreateDirectory(parent);
        var stageDir = Path.Combine(parent, ".Autobots-stage-" + Guid.NewGuid().ToString("N"));
        var backupDir = Path.Combine(parent, ".Autobots-backup-" + Guid.NewGuid().ToString("N"));
        var hadPrevious = Directory.Exists(installDir);
        var installedNew = false;
        try
        {
            Directory.CreateDirectory(stageDir);
            using var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("Autobots.Setup.payload.zip")
                ?? throw new InvalidOperationException("The installer payload is missing. Download the installer again.");
            using var archive = new ZipArchive(payload, ZipArchiveMode.Read);
            var stagePrefix = Path.GetFullPath(stageDir) + Path.DirectorySeparatorChar;
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                var destination = Path.GetFullPath(Path.Combine(stageDir, entry.FullName));
                if (!destination.StartsWith(stagePrefix, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The installer contains an invalid file path.");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination);
            }
            if (!File.Exists(Path.Combine(stageDir, "Autobots.Desktop.exe")))
                throw new InvalidDataException("The Autobots application is missing from this installer.");

            if (hadPrevious) Directory.Move(installDir, backupDir);
            Directory.Move(stageDir, installDir);
            installedNew = true;
            var appPath = Path.Combine(installDir, "Autobots.Desktop.exe");
            WriteUninstaller(installDir);
            CreateStartMenuShortcut(appPath);
            RegisterInstalledApp(installDir, appPath);
            if (Directory.Exists(backupDir)) Directory.Delete(backupDir, true);
            return appPath;
        }
        catch
        {
            if (installedNew && Directory.Exists(installDir)) Directory.Delete(installDir, true);
            if (Directory.Exists(backupDir)) Directory.Move(backupDir, installDir);
            throw;
        }
        finally
        {
            if (Directory.Exists(stageDir)) Directory.Delete(stageDir, true);
        }
    }

    private static void CreateStartMenuShortcut(string appPath)
    {
        var menu = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        var shortcutPath = Path.Combine(menu, "Autobots.lnk");
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows could not create the Start menu shortcut.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = appPath;
        shortcut.WorkingDirectory = Path.GetDirectoryName(appPath)!;
        shortcut.IconLocation = appPath + ",0";
        shortcut.Description = "Autobots by Origin Studios";
        shortcut.Save();
    }

    private static void RegisterInstalledApp(string installDir, string appPath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\OriginStudios.Autobots", true)
            ?? throw new InvalidOperationException("Windows could not register Autobots for uninstall.");
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        key.SetValue("DisplayName", "Autobots by Origin Studios");
        key.SetValue("DisplayVersion", $"{version?.Major ?? 0}.{version?.Minor ?? 0}.{version?.Build ?? 0}");
        key.SetValue("Publisher", "Origin Studios");
        key.SetValue("InstallLocation", installDir);
        key.SetValue("DisplayIcon", appPath);
        key.SetValue("UninstallString", $"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"{Path.Combine(installDir, "Uninstall-Autobots.ps1")}\"");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
    }

    private static void WriteUninstaller(string installDir)
    {
        const string script = """
            $ErrorActionPreference = 'Stop'
            Add-Type -AssemblyName System.Windows.Forms
            $expected = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\Origin Studios\Autobots')).TrimEnd([IO.Path]::DirectorySeparatorChar)
            $actual = [IO.Path]::GetFullPath((Split-Path -Parent $MyInvocation.MyCommand.Path)).TrimEnd([IO.Path]::DirectorySeparatorChar)
            if (-not [string]::Equals($expected, $actual, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected Autobots install location.' }
            if (Get-Process -Name Autobots.Desktop -ErrorAction SilentlyContinue) {
                [Windows.Forms.MessageBox]::Show('Close Autobots from its tray menu before uninstalling.', 'Autobots') | Out-Null
                exit 1
            }
            Remove-Item -LiteralPath (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)) 'Autobots.lnk') -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\OriginStudios.Autobots' -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $actual -Recurse -Force
            [Windows.Forms.MessageBox]::Show('Autobots was removed.', 'Autobots') | Out-Null
            """;
        File.WriteAllText(Path.Combine(installDir, "Uninstall-Autobots.ps1"), script);
    }
}
