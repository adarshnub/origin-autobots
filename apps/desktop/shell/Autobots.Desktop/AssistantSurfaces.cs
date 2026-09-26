using Autobots.Platform;
using Avalonia.Threading;

namespace Autobots.Desktop;

/// <summary>
/// Coordinates Autobots' own on-screen surfaces with the desktop adapter: the pilot bar and pointer
/// indicator are hidden for the instant of each screenshot and moved away from pointer targets.
/// </summary>
public sealed class AssistantSurfaces(PilotHudWindow hud, Func<IAgentPointerOverlay?> pointerOverlay) : IAssistantSurfaces
{
    public async ValueTask SetHiddenForCaptureAsync(bool hidden, CancellationToken cancellationToken)
    {
        await Dispatcher.UIThread.InvokeAsync(() => hud.SetHiddenForCapture(hidden));
        if (pointerOverlay() is { } overlay)
            await overlay.SetHiddenForCaptureAsync(hidden, cancellationToken).ConfigureAwait(false);
        if (hidden)
        {
            // Two compositor frames: one for the moved bar, one for the hidden pointer indicator.
            NativeWindowTraits.FlushComposition();
            NativeWindowTraits.FlushComposition();
        }
    }

    public async ValueTask EnsurePointClearAsync(int desktopX, int desktopY, CancellationToken cancellationToken)
    {
        var moved = await Dispatcher.UIThread.InvokeAsync(() => hud.MoveAwayFrom(desktopX, desktopY));
        if (moved)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NativeWindowTraits.FlushComposition();
            NativeWindowTraits.FlushComposition();
        }
    }
}
