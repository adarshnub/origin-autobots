using Avalonia;

namespace Autobots.Desktop;

internal static class Program
{
    /// <summary>
    /// Developer UI preview state (<c>--ui-preview working|listening|confirm|result</c>). It only shows the
    /// pilot bar with sample text; no task starts and input stays disarmed.
    /// </summary>
    public static string? UiPreview { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (args[index] == "--ui-preview")
                UiPreview = args[index + 1];
#if WINDOWS
            if (args[index] == "--render-pointer-preview")
            {
                Autobots.Platform.Windows.WindowsPointerOverlay.SavePreview(args[index + 1]);
                return;
            }
#endif
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
