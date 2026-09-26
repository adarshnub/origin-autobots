using Autobots.Platform;

namespace Autobots.Desktop;

/// <summary>
/// Creates the OS services for the current platform. Only Windows has native capture, input, pointer
/// indicator, microphone and global shortcut implementations today; other platforms report them missing.
/// </summary>
public sealed class DesktopServices : IAsyncDisposable
{
    private DesktopServices(
        IDesktopAutomation? desktop,
        IAgentPointerOverlay? pointerOverlay,
        IMicrophoneRecorder? microphone,
        IGlobalShortcut? stopShortcut,
        IGlobalShortcut? talkShortcut)
    {
        Desktop = desktop;
        PointerOverlay = pointerOverlay;
        Microphone = microphone;
        StopShortcut = stopShortcut;
        TalkShortcut = talkShortcut;
    }

    public IDesktopAutomation? Desktop { get; }
    public IAgentPointerOverlay? PointerOverlay { get; }
    public IMicrophoneRecorder? Microphone { get; }
    public IGlobalShortcut? StopShortcut { get; }
    public IGlobalShortcut? TalkShortcut { get; }

    public string StopShortcutText => StopShortcut?.DisplayName ?? "Ctrl+Alt+Shift+S";
    public string TalkShortcutText => TalkShortcut?.DisplayName ?? "Ctrl+Alt+Space";

    public static DesktopServices Create(Func<CancellationToken, ValueTask<bool>> isTaskCaptureAuthorized, IAssistantSurfaces surfaces)
    {
#if WINDOWS
        IAgentPointerOverlay? overlay = null;
        try
        {
            overlay = new Autobots.Platform.Windows.WindowsPointerOverlay();
        }
        catch (PlatformCapabilityUnavailableException)
        {
            // Tasks still run; the real pointer remains visible without the indicator.
        }
        return new DesktopServices(
            new Autobots.Platform.Windows.WindowsDesktopAdapter(isTaskCaptureAuthorized, surfaces, overlay),
            overlay,
            new Autobots.Platform.Windows.WindowsMicrophoneRecorder(),
            new Autobots.Platform.Windows.WindowsStopShortcut(),
            new Autobots.Platform.Windows.WindowsTalkShortcut());
#else
        _ = isTaskCaptureAuthorized;
        _ = surfaces;
        return new DesktopServices(null, null, null, null, null);
#endif
    }

    public async ValueTask DisposeAsync()
    {
        if (Desktop is not null)
        {
            try
            {
                await Desktop.StopInputAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception error) when (error is InvalidOperationException or NotSupportedException)
            {
                // Nothing is held when the adapter cannot be reached.
            }
        }
        PointerOverlay?.Dispose();
        if (StopShortcut is not null)
            await StopShortcut.DisposeAsync().ConfigureAwait(false);
        if (TalkShortcut is not null)
            await TalkShortcut.DisposeAsync().ConfigureAwait(false);
    }
}
