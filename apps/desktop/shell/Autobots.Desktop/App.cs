using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace Autobots.Desktop;

public sealed class App : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new ShellWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
