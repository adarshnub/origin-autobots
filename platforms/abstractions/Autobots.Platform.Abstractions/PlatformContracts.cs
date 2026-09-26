namespace Autobots.Platform;

using Autobots.Contracts;

public sealed record DesktopCapabilities(
    string Platform,
    bool ScreenCapture,
    bool NativeInput,
    bool GlobalHotkeys,
    bool SecureCredentialStore,
    string[] Limitations);

/// <summary>
/// One observation of the display. <see cref="PixelWidth"/>/<see cref="PixelHeight"/> are the physical
/// display pixels that normalized coordinates map onto; <see cref="ImageWidth"/>/<see cref="ImageHeight"/>
/// describe the (possibly downscaled) image uploaded to the model.
/// </summary>
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
    int ImageWidth,
    int ImageHeight,
    string ImageMimeType,
    byte[] ImageBytes)
{
    public string ForegroundProcessName { get; init; } = string.Empty;
}

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

/// <summary>
/// The capture and input services a desktop task needs. Implementations stay disarmed until the local
/// supervisor issues a one-use capability for an owner-submitted task.
/// </summary>
public interface IDesktopAutomation : IPlatformAutomation, IInputController
{
    double PointerSpeed { get; set; }
    ValueTask WaitForVisualSettleAsync(TimeSpan minimum, TimeSpan maximum, CancellationToken cancellationToken);
}

/// <summary>An OS-wide shortcut registered by the signed-in user's Autobots process.</summary>
public interface IGlobalShortcut : IAsyncDisposable
{
    string DisplayName { get; }
    ValueTask RegisterAsync(Func<CancellationToken, ValueTask> pressed, CancellationToken cancellationToken);
}

public interface IStopShortcut : IGlobalShortcut
{
}

/// <summary>
/// A visible, click-through indicator anchored to the real pointer while a task runs. It never
/// receives input and is hidden while Autobots captures its own observations.
/// </summary>
public interface IAgentPointerOverlay : IDisposable
{
    void Show();
    void Hide();
    void SetCaption(string? caption);
    void SetActivity(PointerActivity activity);
    void PulseClick();
    ValueTask SetHiddenForCaptureAsync(bool hidden, CancellationToken cancellationToken);
}

/// <summary>
/// Autobots' own on-screen surfaces (task HUD and pointer indicator). The desktop adapter asks the shell
/// to hide them for the instant of a screenshot and to move them away from a pointer target, so they
/// never appear in observations or intercept an action.
/// </summary>
public interface IAssistantSurfaces
{
    ValueTask SetHiddenForCaptureAsync(bool hidden, CancellationToken cancellationToken);
    ValueTask EnsurePointClearAsync(int desktopX, int desktopY, CancellationToken cancellationToken);
}

public enum PointerActivity
{
    Idle,
    Thinking,
    Moving,
    Clicking,
    Typing,
    Scrolling,
    Dragging,
    Waiting
}

public sealed record MicrophoneRecordingOptions(TimeSpan MaxDuration, TimeSpan TrailingSilence, bool StopOnSilence);

public sealed record RecordedAudio(byte[] WavBytes, TimeSpan Duration, bool SpeechDetected);

/// <summary>
/// Push-to-talk microphone capture. Recording happens only between an explicit owner start and the
/// stop token, a trailing-silence stop or the maximum duration; audio stays in memory.
/// </summary>
public interface IMicrophoneRecorder
{
    bool IsAvailable { get; }
    ValueTask<RecordedAudio> RecordAsync(
        MicrophoneRecordingOptions options,
        IProgress<double>? level,
        CancellationToken stopToken,
        CancellationToken cancellationToken);
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

/// <summary>
/// The proposed action was not sent because a pre-dispatch check failed. No input reached the OS,
/// so the task can safely re-observe and continue.
/// </summary>
public sealed class ActionNotDispatchedException(string message, Exception? innerException = null)
    : UnauthorizedAccessException(message, innerException)
{
}

/// <summary>
/// The desktop is in a state Autobots must hand back to the owner, such as the lock screen, a UAC secure
/// prompt or Autobots' own window being active.
/// </summary>
public sealed class DesktopHandoffRequiredException(string message) : InvalidOperationException(message)
{
}

/// <summary>
/// Input stopped part-way through an action after some of it reached the OS. The outcome is uncertain,
/// so the task must stop and report it instead of retrying.
/// </summary>
public sealed class ActionInterruptedException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException)
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

    /// <summary>
    /// Returns the largest image size that fits within the bounds while preserving the display aspect ratio.
    /// Normalized model coordinates are independent of this scale.
    /// </summary>
    public static (int Width, int Height) FitWithin(int pixelWidth, int pixelHeight, int maxWidth, int maxHeight)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelWidth), "Display dimensions must be positive.");
        if (maxWidth <= 0 || maxHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxWidth), "Image bounds must be positive.");
        var scale = Math.Min(1d, Math.Min(maxWidth / (double)pixelWidth, maxHeight / (double)pixelHeight));
        return (Math.Max(1, (int)Math.Round(pixelWidth * scale)), Math.Max(1, (int)Math.Round(pixelHeight * scale)));
    }
}
