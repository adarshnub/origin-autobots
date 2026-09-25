namespace Autobots.Platform;

using Autobots.Contracts;

public sealed record DesktopCapabilities(
    string Platform,
    bool ScreenCapture,
    bool NativeInput,
    bool GlobalHotkeys,
    bool SecureCredentialStore,
    string[] Limitations);

public sealed record CapturedFrame(
    string ObservationId,
    DateTimeOffset CapturedAt,
    string MonitorId,
    int PixelWidth,
    int PixelHeight,
    int DesktopOriginX,
    int DesktopOriginY,
    int VirtualDesktopOriginX,
    int VirtualDesktopOriginY,
    int VirtualDesktopWidth,
    int VirtualDesktopHeight,
    double DpiX,
    double DpiY,
    nint ForegroundWindowHandle,
    int? ForegroundProcessId,
    string ForegroundWindowTitle,
    long LayoutGeneration,
    byte[] PngBytes);

/// <summary>
/// A one-use action capability created by the local supervisor from the active task grant.
/// It is intentionally not deserializable from provider or cloud data.
/// </summary>
public sealed class AuthorizedAction
{
    internal AuthorizedAction(ActionEnvelope envelope, CapturedFrame observation, DateTimeOffset authorizedAt, DateTimeOffset expiresAt)
    {
        Envelope = envelope;
        ObservationId = Guid.Parse(observation.ObservationId);
        CapturedAt = observation.CapturedAt;
        ForegroundWindowHandle = observation.ForegroundWindowHandle;
        ForegroundProcessId = observation.ForegroundProcessId;
        ForegroundWindowTitle = observation.ForegroundWindowTitle;
        LayoutGeneration = observation.LayoutGeneration;
        AuthorizedAt = authorizedAt;
        ExpiresAt = expiresAt;
    }

    public ActionEnvelope Envelope { get; }
    public Guid ObservationId { get; }
    public DateTimeOffset CapturedAt { get; }
    public nint ForegroundWindowHandle { get; }
    public int? ForegroundProcessId { get; }
    public string ForegroundWindowTitle { get; }
    public long LayoutGeneration { get; }
    public DateTimeOffset AuthorizedAt { get; }
    public DateTimeOffset ExpiresAt { get; }
}

public interface IPlatformAutomation
{
    DesktopCapabilities GetCapabilities();
    ValueTask<CapturedFrame> CapturePrimaryDisplayAsync(CancellationToken cancellationToken);
    ValueTask StopInputAsync(CancellationToken cancellationToken);
}

/// <summary>
/// OS input primitives. Only the local supervisor may translate a validated,
/// authorized action into these calls; model/provider code must not receive this service.
/// Concrete implementations stay disarmed until the local grant path arms a task.
/// </summary>
public interface IInputController
{
    bool IsArmed { get; }
    ValueTask ExecuteAuthorizedActionAsync(AuthorizedAction authorization, CapturedFrame observation, CancellationToken cancellationToken);
    ValueTask ReleaseAllAsync(CancellationToken cancellationToken);
}

public interface IStopShortcut : IAsyncDisposable
{
    ValueTask RegisterAsync(Func<CancellationToken, ValueTask> stop, CancellationToken cancellationToken);
}

public interface ISecureCredentialStore
{
    ValueTask StoreAsync(string name, ReadOnlyMemory<byte> secret, CancellationToken cancellationToken);
    ValueTask<ReadOnlyMemory<byte>?> ReadAsync(string name, CancellationToken cancellationToken);
    ValueTask DeleteAsync(string name, CancellationToken cancellationToken);
}

public interface IInteractiveSessionGate
{
    ValueTask<bool> IsInteractiveAndUnlockedAsync(CancellationToken cancellationToken);
}

public sealed class PlatformCapabilityUnavailableException(string message) : NotSupportedException(message)
{
}

public static class DisplayCoordinateTransform
{
    public static (int X, int Y) ToDesktopPixels(
        int normalizedX,
        int normalizedY,
        int pixelWidth,
        int pixelHeight,
        int desktopOriginX,
        int desktopOriginY)
    {
        if (normalizedX is < 0 or > 999)
            throw new ArgumentOutOfRangeException(nameof(normalizedX), "Normalized coordinates must be between 0 and 999.");
        if (normalizedY is < 0 or > 999)
            throw new ArgumentOutOfRangeException(nameof(normalizedY), "Normalized coordinates must be between 0 and 999.");
        if (pixelWidth <= 0 || pixelHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelWidth), "Display dimensions must be positive.");

        var x = (int)Math.Round(normalizedX * (pixelWidth - 1) / 999d, MidpointRounding.AwayFromZero);
        var y = (int)Math.Round(normalizedY * (pixelHeight - 1) / 999d, MidpointRounding.AwayFromZero);
        return (desktopOriginX + x, desktopOriginY + y);
    }
}
