using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;

namespace Autobots.Desktop;

public sealed class App : Application
{
    private TrayIcon? _trayIcon;

    public override void OnFrameworkInitializationCompleted()
    {
        Ui.ApplyApplicationResources(Resources);
        Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The window can close to the tray so push-to-talk keeps working; Quit ends the process.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var window = new ShellWindow(AppSettings.Load(), () => desktop.TryShutdown());
            desktop.MainWindow = window;
            _trayIcon = CreateTrayIcon(window);
            if (_trayIcon is not null)
                TrayIcon.SetIcons(this, [_trayIcon]);
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static TrayIcon? CreateTrayIcon(ShellWindow window)
    {
        using var iconStream = typeof(App).Assembly.GetManifestResourceStream("Autobots.Desktop.Assets.AutobotsIcon.ico");
        if (iconStream is null)
            return null;

        var talk = new NativeMenuItem("Talk to Autobots\tCtrl+Alt+Space");
        talk.Click += (_, _) => window.TalkFromTray();
        var open = new NativeMenuItem("Open Autobots");
        open.Click += (_, _) => window.ShowFromTray();
        var stop = new NativeMenuItem("Stop current task\tCtrl+Alt+Shift+S");
        stop.Click += (_, _) => window.StopFromTray();
        var quit = new NativeMenuItem("Quit Autobots");
        quit.Click += (_, _) => window.QuitApplication();

        var tray = new TrayIcon
        {
            Icon = new WindowIcon(iconStream),
            ToolTipText = "Autobots — press Ctrl+Alt+Space to talk",
            Menu = new NativeMenu { Items = { talk, open, stop, new NativeMenuItemSeparator(), quit } }
        };
        tray.Clicked += (_, _) => window.ShowFromTray();
        return tray;
    }
}
