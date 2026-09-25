using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Autobots.Contracts;
using Autobots.Platform;

namespace Autobots.Platform.Windows;

public sealed class WindowsDesktopAdapter(
    Func<CancellationToken, ValueTask<bool>> isTaskCaptureAuthorized,
    Func<CancellationToken, ValueTask<bool>> minimizeShell,
    Func<CancellationToken, ValueTask> restoreShell) : IPlatformAutomation, IInputController
{
    private readonly object _layoutGate = new();
    private readonly WindowsInputController _inputController = new();
    private string? _layoutFingerprint;
    private long _layoutGeneration;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowTextLength(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    public bool IsArmed => _inputController.IsArmed;

    public DesktopCapabilities GetCapabilities() => new(
        Platform: "windows",
        ScreenCapture: true,
        NativeInput: true,
        GlobalHotkeys: false,
        SecureCredentialStore: false,
        Limitations: [
            "Input targets only the same foreground, normal-integrity window that was captured; UAC and secure-desktop prompts are unsupported.",
            "Text entry is blocked when the focused control is a password field or its password status cannot be verified.",
            "Capture uses the active task grant through a GDI prototype; the planned Windows.Graphics.Capture picker and capture exclusion are not implemented."]);

    public async ValueTask<CapturedFrame> CapturePrimaryDisplayAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await isTaskCaptureAuthorized(cancellationToken).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Screen capture requires an active owner-submitted task grant.");

        if (!await minimizeShell(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("Autobots could not reveal the target application for capture.");
        try
        {
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            var bounds = Screen.PrimaryScreen?.Bounds ?? throw new PlatformCapabilityUnavailableException("No primary display is available.");
            var virtualLeft = GetSystemMetrics(76);
            var virtualTop = GetSystemMetrics(77);
            var virtualWidth = GetSystemMetrics(78);
            var virtualHeight = GetSystemMetrics(79);
            var foregroundWindow = GetForegroundWindow();
            var threadId = GetWindowThreadProcessId(foregroundWindow, out var processId);
            if (foregroundWindow == IntPtr.Zero || threadId == 0 || processId == Environment.ProcessId)
                throw new InvalidOperationException("Open the application you want Autobots to use, then try again.");

            using var bitmap = new Bitmap(bounds.Width, bounds.Height);
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);

            using var desktopGraphics = Graphics.FromHwnd(foregroundWindow);
            var dpiX = desktopGraphics.DpiX;
            var dpiY = desktopGraphics.DpiY;
            var fingerprint = $"{Screen.PrimaryScreen?.DeviceName}|{bounds.Left}|{bounds.Top}|{bounds.Width}|{bounds.Height}|{virtualLeft}|{virtualTop}|{virtualWidth}|{virtualHeight}|{dpiX}|{dpiY}";
            var layoutGeneration = GetLayoutGeneration(fingerprint);
            var foregroundProcessId = (int?)processId;
            var titleLength = Math.Clamp(GetWindowTextLength(foregroundWindow), 0, 2048);
            var title = new StringBuilder(titleLength + 1);
            _ = GetWindowText(foregroundWindow, title, title.Capacity);

            using var image = new MemoryStream();
            bitmap.Save(image, ImageFormat.Png);
            return new CapturedFrame(
                ObservationId: Guid.NewGuid().ToString("D"),
                CapturedAt: DateTimeOffset.UtcNow,
                MonitorId: Screen.PrimaryScreen?.DeviceName ?? "primary",
                PixelWidth: bitmap.Width,
                PixelHeight: bitmap.Height,
                DesktopOriginX: bounds.Left,
                DesktopOriginY: bounds.Top,
                VirtualDesktopOriginX: virtualLeft,
                VirtualDesktopOriginY: virtualTop,
                VirtualDesktopWidth: virtualWidth,
                VirtualDesktopHeight: virtualHeight,
                DpiX: dpiX,
                DpiY: dpiY,
                ForegroundWindowHandle: foregroundWindow,
                ForegroundProcessId: foregroundProcessId,
                ForegroundWindowTitle: title.ToString(),
                LayoutGeneration: layoutGeneration,
                PngBytes: image.ToArray());
        }
        finally
        {
            await restoreShell(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public ValueTask StopInputAsync(CancellationToken cancellationToken)
    {
        return _inputController.ReleaseAllAsync(cancellationToken);
    }

    public async ValueTask ExecuteAuthorizedActionAsync(AuthorizedAction authorization, CapturedFrame observation, CancellationToken cancellationToken)
    {
        if (!await minimizeShell(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("Autobots could not yield focus to the target application.");
        try
        {
            await Task.Delay(150, cancellationToken).ConfigureAwait(false);
            await _inputController.ExecuteAuthorizedActionAsync(authorization, observation, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await restoreShell(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public ValueTask ReleaseAllAsync(CancellationToken cancellationToken) =>
        _inputController.ReleaseAllAsync(cancellationToken);

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
