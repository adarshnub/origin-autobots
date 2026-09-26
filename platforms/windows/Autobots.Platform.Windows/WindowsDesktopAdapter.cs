using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Autobots.Contracts;
using Autobots.Platform;
using DragAction = Autobots.Contracts.DragAction;

namespace Autobots.Platform.Windows;

/// <summary>
/// Windows capture and input for an owner-submitted task. A task may use any normal-integrity app on the
/// interactive desktop: each step observes the display and whichever window is active, and the resulting
/// one-use action is bound to that observation's foreground window and display layout.
/// </summary>
public sealed class WindowsDesktopAdapter(
    Func<CancellationToken, ValueTask<bool>> isTaskCaptureAuthorized,
    IAssistantSurfaces assistantSurfaces,
    IAgentPointerOverlay? pointerOverlay = null) : IDesktopAutomation
{
    /// <summary>Uploaded observations fit within the size recommended for Gemini computer use.</summary>
    public const int MaxImageWidth = 1440;
    public const int MaxImageHeight = 900;
    private const long JpegQuality = 85;
    private const int ThumbnailWidth = 64;
    private const int ThumbnailHeight = 36;

    private readonly object _layoutGate = new();
    private readonly WindowsInputController _inputController = new(pointerOverlay);
    private string? _layoutFingerprint;
    private long _layoutGeneration;

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(nint window, StringBuilder text, int maxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowTextLength(nint window);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint window, nint dc);

    [DllImport("gdi32.dll")]
    private static extern int SetStretchBltMode(nint dc, int mode);

    [DllImport("gdi32.dll")]
    private static extern bool StretchBlt(nint destination, int x, int y, int width, int height, nint source, int sourceX, int sourceY, int sourceWidth, int sourceHeight, uint operation);

    public bool IsArmed => _inputController.IsArmed;
    public event Action<PointerTargetDiagnostic>? PointerTargetObserved
    {
        add => _inputController.PointerTargetObserved += value;
        remove => _inputController.PointerTargetObserved -= value;
    }

    /// <summary>Pointer travel speed multiplier; see <see cref="WindowsInputController.PointerSpeed"/>.</summary>
    public double PointerSpeed
    {
        get => _inputController.PointerSpeed;
        set => _inputController.PointerSpeed = value;
    }

    public DesktopCapabilities GetCapabilities() => new(
        Platform: "windows",
        ScreenCapture: true,
        NativeInput: true,
        GlobalHotkeys: true,
        SecureCredentialStore: false,
        Limitations: [
            "Input runs at the signed-in user's normal integrity; apps running as administrator, UAC and the lock screen are handed back to the owner.",
            "Each action is bound to the window that was active in its screenshot and is rejected if focus or the display layout changes first.",
            "Text entry is blocked when the focused control is a password field or cannot be verified as a text field.",
            "Observations cover the primary display only."]);

    public async ValueTask<CapturedFrame> CapturePrimaryDisplayAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await isTaskCaptureAuthorized(cancellationToken).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Screen capture requires an active owner-submitted task grant.");
        EnsureDesktopAvailable();
        var (foregroundWindow, foregroundProcessId) = await WaitForForegroundWindowAsync(cancellationToken).ConfigureAwait(false);
        if (foregroundProcessId == Environment.ProcessId)
            throw new DesktopHandoffRequiredException("The Autobots window was brought to the front, so the task paused. Start again when you're ready.");

        var bounds = Screen.PrimaryScreen?.Bounds ?? throw new PlatformCapabilityUnavailableException("No primary display is available.");
        var virtualLeft = GetSystemMetrics(76);
        var virtualTop = GetSystemMetrics(77);
        var virtualWidth = GetSystemMetrics(78);
        var virtualHeight = GetSystemMetrics(79);
        var capturedAt = DateTimeOffset.UtcNow;
        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
        await assistantSurfaces.SetHiddenForCaptureAsync(true, cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-read the active window immediately before the copy; the action is bound to this window.
            foregroundWindow = GetForegroundWindow();
            _ = GetWindowThreadProcessId(foregroundWindow, out var processId);
            foregroundProcessId = (int)processId;
            capturedAt = DateTimeOffset.UtcNow;
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
        }
        finally
        {
            await assistantSurfaces.SetHiddenForCaptureAsync(false, CancellationToken.None).ConfigureAwait(false);
        }
        if (foregroundWindow == 0 || foregroundProcessId == Environment.ProcessId)
            throw new DesktopHandoffRequiredException("The active window changed to Autobots during capture, so the task paused.");

        var dpi = GetDpiForWindow(foregroundWindow);
        if (dpi == 0)
            dpi = 96;
        var fingerprint = $"{Screen.PrimaryScreen?.DeviceName}|{bounds.Left}|{bounds.Top}|{bounds.Width}|{bounds.Height}|{virtualLeft}|{virtualTop}|{virtualWidth}|{virtualHeight}|{dpi}";
        var (imageWidth, imageHeight) = DisplayCoordinateTransform.FitWithin(bounds.Width, bounds.Height, MaxImageWidth, MaxImageHeight);
        var image = EncodeJpeg(bitmap, imageWidth, imageHeight);

        return new CapturedFrame(
            ObservationId: Guid.NewGuid().ToString("D"),
            CapturedAt: capturedAt,
            MonitorId: Screen.PrimaryScreen?.DeviceName ?? "primary",
            PixelWidth: bounds.Width,
            PixelHeight: bounds.Height,
            DesktopOriginX: bounds.Left,
            DesktopOriginY: bounds.Top,
            VirtualDesktopOriginX: virtualLeft,
            VirtualDesktopOriginY: virtualTop,
            VirtualDesktopWidth: virtualWidth,
            VirtualDesktopHeight: virtualHeight,
            DpiX: dpi,
            DpiY: dpi,
            ForegroundWindowHandle: foregroundWindow,
            ForegroundProcessId: foregroundProcessId,
            ForegroundWindowTitle: WindowTitle(foregroundWindow),
            LayoutGeneration: GetLayoutGeneration(fingerprint),
            ImageWidth: imageWidth,
            ImageHeight: imageHeight,
            ImageMimeType: "image/jpeg",
            ImageBytes: image)
        {
            ForegroundProcessName = WindowsProcessIntegrity.FriendlyName(foregroundProcessId)
        };
    }

    /// <summary>
    /// Waits until the display stops changing (a readiness hint, not proof an action succeeded).
    /// Uses tiny in-memory thumbnails that are never uploaded.
    /// </summary>
    public async ValueTask WaitForVisualSettleAsync(TimeSpan minimum, TimeSpan maximum, CancellationToken cancellationToken)
    {
        await Task.Delay(minimum, cancellationToken).ConfigureAwait(false);
        var clock = Stopwatch.StartNew();
        byte[]? previous = null;
        while (clock.Elapsed < maximum)
        {
            var current = CaptureThumbnail();
            if (previous is not null && MeanDifference(previous, current) < 1.5)
                return;
            previous = current;
            await Task.Delay(180, cancellationToken).ConfigureAwait(false);
        }
    }

    public ValueTask StopInputAsync(CancellationToken cancellationToken) =>
        _inputController.ReleaseAllAsync(cancellationToken);

    public async ValueTask ExecuteAuthorizedActionAsync(AuthorizedAction authorization, CapturedFrame observation, CancellationToken cancellationToken)
    {
        foreach (var (x, y) in PointerTargets(authorization.Envelope.Action))
        {
            var point = DisplayCoordinateTransform.ToDesktopPixels(x, y, observation.PixelWidth, observation.PixelHeight, observation.DesktopOriginX, observation.DesktopOriginY);
            await assistantSurfaces.EnsurePointClearAsync(point.X, point.Y, cancellationToken).ConfigureAwait(false);
        }
        await _inputController.ExecuteAuthorizedActionAsync(authorization, observation, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask ReleaseAllAsync(CancellationToken cancellationToken) =>
        _inputController.ReleaseAllAsync(cancellationToken);

    private static IEnumerable<(int X, int Y)> PointerTargets(ProposedAction action) => action switch
    {
        ClickAction click => [(click.X, click.Y)],
        MoveAction move => [(move.X, move.Y)],
        ScrollAction scroll => [(scroll.X, scroll.Y)],
        DragAction drag => [(drag.X, drag.Y), (drag.ToX, drag.ToY)],
        _ => []
    };

    private static void EnsureDesktopAvailable()
    {
        if (!WindowsSessionGate.IsInputOnDefaultDesktop())
            throw new DesktopHandoffRequiredException("Windows is showing the lock screen or a secure prompt such as UAC. Autobots can't act there; finish it yourself, then start the task again.");
    }

    private static async ValueTask<(nint Window, int ProcessId)> WaitForForegroundWindowAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var window = GetForegroundWindow();
            if (window != 0 && GetWindowThreadProcessId(window, out var processId) != 0)
                return (window, (int)processId);
            // Focus is briefly unassigned while Windows switches between apps.
            await Task.Delay(120, cancellationToken).ConfigureAwait(false);
        }
        throw new DesktopHandoffRequiredException("No active window could be identified. Click the app you want Autobots to use and start again.");
    }

    private static string WindowTitle(nint window)
    {
        var length = Math.Clamp(GetWindowTextLength(window), 0, 2048);
        var title = new StringBuilder(length + 1);
        _ = GetWindowText(window, title, title.Capacity);
        return title.ToString();
    }

    private static byte[] EncodeJpeg(Bitmap source, int width, int height)
    {
        var codec = ImageCodecInfo.GetImageEncoders().First(encoder => encoder.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, JpegQuality);
        using var stream = new MemoryStream();
        if (width == source.Width && height == source.Height)
        {
            source.Save(stream, codec, parameters);
            return stream.ToArray();
        }
        using var scaled = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(scaled))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.DrawImage(source, new Rectangle(0, 0, width, height));
        }
        scaled.Save(stream, codec, parameters);
        return stream.ToArray();
    }

    private static byte[] CaptureThumbnail()
    {
        var bounds = Screen.PrimaryScreen?.Bounds ?? throw new PlatformCapabilityUnavailableException("No primary display is available.");
        using var thumbnail = new Bitmap(ThumbnailWidth, ThumbnailHeight, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(thumbnail))
        {
            var destination = graphics.GetHdc();
            var screen = GetDC(0);
            try
            {
                _ = SetStretchBltMode(destination, 4);
                _ = StretchBlt(destination, 0, 0, ThumbnailWidth, ThumbnailHeight, screen, bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x00CC0020);
            }
            finally
            {
                _ = ReleaseDC(0, screen);
                graphics.ReleaseHdc(destination);
            }
        }
        var data = thumbnail.LockBits(new Rectangle(0, 0, ThumbnailWidth, ThumbnailHeight), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            var raw = new byte[data.Stride * ThumbnailHeight];
            Marshal.Copy(data.Scan0, raw, 0, raw.Length);
            var luminance = new byte[ThumbnailWidth * ThumbnailHeight];
            for (var y = 0; y < ThumbnailHeight; y++)
            {
                for (var x = 0; x < ThumbnailWidth; x++)
                {
                    var offset = (y * data.Stride) + (x * 3);
                    luminance[(y * ThumbnailWidth) + x] = (byte)(((raw[offset] * 29) + (raw[offset + 1] * 150) + (raw[offset + 2] * 77)) >> 8);
                }
            }
            return luminance;
        }
        finally
        {
            thumbnail.UnlockBits(data);
        }
    }

    private static double MeanDifference(byte[] first, byte[] second)
    {
        long total = 0;
        for (var index = 0; index < first.Length; index++)
            total += Math.Abs(first[index] - second[index]);
        return total / (double)first.Length;
    }

    private long GetLayoutGeneration(string fingerprint)
    {
        lock (_layoutGate)
        {
            if (!string.Equals(_layoutFingerprint, fingerprint, StringComparison.Ordinal))
            {
                _layoutFingerprint = fingerprint;
                _layoutGeneration++;
            }
            return _layoutGeneration;
        }
    }
}
