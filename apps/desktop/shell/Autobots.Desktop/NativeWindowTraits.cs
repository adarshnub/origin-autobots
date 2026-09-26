namespace Autobots.Desktop;

/// <summary>OS window traits for Autobots' own surfaces; no-ops where the platform adapter is absent.</summary>
internal static class NativeWindowTraits
{
    public static void MakeFloatingPanel(nint handle)
    {
#if WINDOWS
        Autobots.Platform.Windows.WindowsWindowStyles.MakeNonActivatingToolWindow(handle);
#endif
    }

    public static void UseDarkFrame(nint handle)
    {
#if WINDOWS
        Autobots.Platform.Windows.WindowsWindowStyles.UseDarkFrame(handle, captionRgb: 0x0C1117, textRgb: 0xE6EDF3, borderRgb: 0x1E2A36);
#endif
    }

    public static void FlushComposition()
    {
#if WINDOWS
        Autobots.Platform.Windows.WindowsWindowStyles.FlushComposition();
#endif
    }
}
