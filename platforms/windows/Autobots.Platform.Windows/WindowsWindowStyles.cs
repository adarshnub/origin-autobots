using System.Runtime.InteropServices;

namespace Autobots.Platform.Windows;

/// <summary>Window traits for Autobots' own surfaces.</summary>
public static class WindowsWindowStyles
{
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExNoActivate = 0x08000000;
    private const long WsExAppWindow = 0x00040000;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    /// <summary>
    /// Keeps a floating Autobots panel from taking keyboard focus away from the app being controlled.
    /// Its buttons still receive clicks.
    /// </summary>
    public static void MakeNonActivatingToolWindow(nint window)
    {
        if (window == 0)
            return;
        var style = (long)GetWindowLongPtr(window, GwlExStyle);
        style = (style | WsExToolWindow | WsExNoActivate) & ~WsExAppWindow;
        _ = SetWindowLongPtr(window, GwlExStyle, (nint)style);
    }

    /// <summary>
    /// Gives the native title bar Autobots' dark colors (independent of the Windows accent-color setting)
    /// while keeping the system caption buttons and snap layouts. Colors are 0xRRGGBB.
    /// </summary>
    public static void UseDarkFrame(nint window, int captionRgb, int textRgb, int borderRgb)
    {
        if (window == 0)
            return;
        var enabled = 1;
        _ = DwmSetWindowAttribute(window, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
        var corners = DwmwcpRound;
        _ = DwmSetWindowAttribute(window, DwmwaWindowCornerPreference, ref corners, sizeof(int));
        var caption = ToColorRef(captionRgb);
        _ = DwmSetWindowAttribute(window, DwmwaCaptionColor, ref caption, sizeof(int));
        var text = ToColorRef(textRgb);
        _ = DwmSetWindowAttribute(window, DwmwaTextColor, ref text, sizeof(int));
        var border = ToColorRef(borderRgb);
        _ = DwmSetWindowAttribute(window, DwmwaBorderColor, ref border, sizeof(int));
    }

    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    private static int ToColorRef(int rgb) => ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF);

    /// <summary>Waits for the desktop compositor to present the next frame.</summary>
    public static void FlushComposition() => _ = DwmFlush();
}
