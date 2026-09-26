using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Autobots.Platform;

namespace Autobots.Platform.Windows;

/// <summary>
/// A click-through, always-on-top indicator drawn around the real Windows pointer while a task runs.
/// It shows where Autobots is working (halo, click ripples, activity) and a short caption. It is a
/// layered window that never takes focus or input and is hidden while Autobots captures a screenshot.
/// </summary>
public sealed class WindowsPointerOverlay : IAgentPointerOverlay
{
    private const uint WsPopup = 0x80000000;
    private const uint WsExLayered = 0x00080000;
    private const uint WsExTransparent = 0x00000020;
    private const uint WsExTopmost = 0x00000008;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExNoActivate = 0x08000000;
    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;
    private const uint UlwAlpha = 0x00000002;
    private const byte AcSrcOver = 0x00;
    private const byte AcSrcAlpha = 0x01;
    private const uint WmQuit = 0x0012;
    private const uint WmApp = 0x8000;
    private const uint PmRemove = 0x0001;
    private const uint QsAllInput = 0x04FF;
    private const uint MonitorDefaultToNearest = 2;
    private const int SurfaceWidth = 1400;
    private const int SurfaceHeight = 420;
    private const double MaxScale = 3;
    private static readonly Color Accent = Color.FromArgb(255, 46, 230, 200);
    private static readonly Color AccentSoft = Color.FromArgb(255, 124, 240, 222);
    private static readonly Color Ripple = Color.FromArgb(255, 255, 200, 87);

    private readonly object _stateGate = new();
    private readonly Thread _thread;
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private Font? _captionFont;
    private double _captionFontScale;
    private TaskCompletionSource? _hiddenAcknowledged;
    private uint _threadId;
    private string? _caption;
    private PointerActivity _activity;
    private double _clickAt = double.NegativeInfinity;
    private bool _visible;
    private bool _hiddenForCapture;
    private int _disposed;

    public WindowsPointerOverlay()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Autobots pointer overlay" };
        _thread.Start();
        try
        {
            if (!_started.Task.Wait(TimeSpan.FromSeconds(3)))
                throw new PlatformCapabilityUnavailableException("The Autobots pointer indicator could not start.");
        }
        catch (AggregateException error) when (error.InnerException is PlatformCapabilityUnavailableException inner)
        {
            throw inner;
        }
    }

    public void Show()
    {
        lock (_stateGate)
            _visible = true;
        Wake();
    }

    public void Hide()
    {
        lock (_stateGate)
            _visible = false;
        Wake();
    }

    public void SetCaption(string? caption)
    {
        var trimmed = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim();
        lock (_stateGate)
            _caption = trimmed is { Length: > 90 } ? trimmed[..89] + "…" : trimmed;
    }

    public void SetActivity(PointerActivity activity)
    {
        lock (_stateGate)
            _activity = activity;
    }

    public void PulseClick()
    {
        lock (_stateGate)
            _clickAt = _clock.Elapsed.TotalMilliseconds;
    }

    public async ValueTask SetHiddenForCaptureAsync(bool hidden, CancellationToken cancellationToken)
    {
        TaskCompletionSource? acknowledged = null;
        lock (_stateGate)
        {
            _hiddenForCapture = hidden;
            if (hidden)
                acknowledged = _hiddenAcknowledged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        Wake();
        if (acknowledged is not null)
        {
            await acknowledged.Task.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            // Wait for the desktop compositor to present a frame without the indicator.
            _ = DwmFlush();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        if (_threadId != 0)
            _ = PostThreadMessage(_threadId, WmQuit, 0, 0);
        _ = _thread.Join(TimeSpan.FromSeconds(2));
    }

    private void Wake()
    {
        if (_threadId != 0)
            _ = PostThreadMessage(_threadId, WmApp + 1, 0, 0);
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        var className = "AutobotsPointerOverlay" + Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var windowClass = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            // The indicator never handles input, so the native default window procedure is sufficient.
            WindowProcedure = NativeLibrary.GetExport(NativeLibrary.Load("user32.dll"), "DefWindowProcW"),
            Instance = GetModuleHandle(null),
            ClassName = className
        };
        if (RegisterClassEx(ref windowClass) == 0)
        {
            _started.TrySetException(new PlatformCapabilityUnavailableException("The pointer indicator window class could not be registered."));
            return;
        }

        var window = CreateWindowEx(WsExLayered | WsExTransparent | WsExTopmost | WsExToolWindow | WsExNoActivate,
            className, "Autobots pointer", WsPopup, 0, 0, 1, 1, 0, 0, windowClass.Instance, 0);
        var screenDc = GetDC(0);
        var memoryDc = CreateCompatibleDC(screenDc);
        var header = new BitmapInfoHeader
        {
            Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
            Width = SurfaceWidth,
            Height = -SurfaceHeight,
            Planes = 1,
            BitCount = 32
        };
        var dib = CreateDIBSection(screenDc, ref header, 0, out var bits, 0, 0);
        if (window == 0 || memoryDc == 0 || dib == 0)
        {
            _started.TrySetException(new PlatformCapabilityUnavailableException("The pointer indicator surface could not be created."));
            return;
        }
        var previousBitmap = SelectObject(memoryDc, dib);
        var surface = new Bitmap(SurfaceWidth, SurfaceHeight, SurfaceWidth * 4, PixelFormat.Format32bppPArgb, bits);
        var graphics = Graphics.FromImage(surface);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        _started.TrySetResult();

        var shown = false;
        try
        {
            while (true)
            {
                while (PeekMessage(out var message, 0, 0, 0, PmRemove))
                {
                    if (message.Message == WmQuit)
                        return;
                    _ = TranslateMessage(ref message);
                    _ = DispatchMessage(ref message);
                }

                bool visible;
                TaskCompletionSource? acknowledge;
                lock (_stateGate)
                {
                    visible = _visible && !_hiddenForCapture;
                    acknowledge = _hiddenForCapture ? _hiddenAcknowledged : null;
                    if (acknowledge is not null)
                        _hiddenAcknowledged = null;
                }

                if (visible)
                {
                    try
                    {
                        RenderFrame(window, screenDc, memoryDc, graphics);
                    }
                    catch (Exception error) when (error is ExternalException or ArgumentException or InvalidOperationException)
                    {
                        // A dropped indicator frame must never affect the task or the process.
                    }
                    if (!shown)
                    {
                        _ = ShowWindow(window, SwShowNoActivate);
                        shown = true;
                    }
                }
                else if (shown)
                {
                    _ = ShowWindow(window, SwHide);
                    shown = false;
                }
                acknowledge?.TrySetResult();
                _ = MsgWaitForMultipleObjects(0, 0, false, visible ? 15u : 250u, QsAllInput);
            }
        }
        finally
        {
            graphics.Dispose();
            surface.Dispose();
            _captionFont?.Dispose();
            _ = SelectObject(memoryDc, previousBitmap);
            _ = DeleteObject(dib);
            _ = DeleteDC(memoryDc);
            _ = ReleaseDC(0, screenDc);
            _ = DestroyWindow(window);
            _ = UnregisterClass(className, windowClass.Instance);
        }
    }

    private void RenderFrame(nint window, nint screenDc, nint memoryDc, Graphics graphics)
    {
        if (!GetCursorPos(out var cursor))
            return;
        var monitor = MonitorFromPoint(cursor, MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        _ = GetMonitorInfo(monitor, ref monitorInfo);
        var scale = GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 ? Math.Clamp(dpiX / 96d, 1, MaxScale) : 1;

        string? caption;
        PointerActivity activity;
        double clickAt;
        lock (_stateGate)
        {
            caption = _caption;
            activity = _activity;
            clickAt = _clickAt;
        }
        var now = _clock.Elapsed.TotalMilliseconds;
        var pulse = (Math.Sin(now / 260d) + 1) / 2;

        if (_captionFont is null || Math.Abs(_captionFontScale - scale) > 0.01)
        {
            _captionFont?.Dispose();
            _captionFont = new Font("Segoe UI Semibold", (float)(12.5 * scale), FontStyle.Regular, GraphicsUnit.Pixel);
            _captionFontScale = scale;
        }
        var captionFont = _captionFont;
        SizeF textSize = default;
        var pillWidth = 0d;
        var pillHeight = 28 * scale;
        if (caption is not null)
        {
            textSize = graphics.MeasureString(caption, captionFont);
            pillWidth = Math.Min(textSize.Width, 320 * scale) + (34 * scale);
        }

        // Content is laid out around the pointer hotspot at (0, 0) and then offset into the surface.
        var haloExtent = 50 * scale;
        var pillOnLeft = cursor.X + (26 * scale) + pillWidth + (8 * scale) > monitorInfo.Monitor.Right;
        var pillAbove = cursor.Y + (22 * scale) + pillHeight + (8 * scale) > monitorInfo.Monitor.Bottom;
        var pillX = pillOnLeft ? -(22 * scale) - pillWidth : 24 * scale;
        var pillY = pillAbove ? -(22 * scale) - pillHeight : 20 * scale;
        var minX = Math.Min(-haloExtent, caption is null ? 0 : pillX - (4 * scale));
        var minY = Math.Min(-haloExtent, caption is null ? 0 : pillY - (4 * scale));
        var maxX = Math.Max(haloExtent, caption is null ? 0 : pillX + pillWidth + (4 * scale));
        var maxY = Math.Max(haloExtent, caption is null ? 0 : pillY + pillHeight + (4 * scale));
        var width = Math.Min(SurfaceWidth, (int)Math.Ceiling(maxX - minX));
        var height = Math.Min(SurfaceHeight, (int)Math.Ceiling(maxY - minY));
        var originX = (float)-minX;
        var originY = (float)-minY;

        graphics.ResetTransform();
        graphics.SetClip(new Rectangle(0, 0, width, height));
        graphics.Clear(Color.Transparent);
        graphics.TranslateTransform(originX, originY);

        DrawHalo(graphics, scale, pulse, activity, now);
        var sinceClick = now - clickAt;
        if (sinceClick is >= 0 and < 520)
            DrawRipple(graphics, scale, sinceClick / 520d);
        if (caption is not null)
            DrawCaption(graphics, captionFont, caption, textSize, scale, (float)pillX, (float)pillY, (float)pillWidth, (float)pillHeight, activity, pulse);
        graphics.ResetClip();
        graphics.Flush(FlushIntention.Sync);

        var destination = new NativePoint { X = cursor.X - (int)Math.Round(originX), Y = cursor.Y - (int)Math.Round(originY) };
        var size = new NativeSize { Width = width, Height = height };
        var source = new NativePoint();
        var blend = new BlendFunction { BlendOp = AcSrcOver, SourceConstantAlpha = 255, AlphaFormat = AcSrcAlpha };
        _ = UpdateLayeredWindow(window, screenDc, ref destination, ref size, memoryDc, ref source, 0, ref blend, UlwAlpha);
    }

    /// <summary>
    /// Developer preview: renders sample indicator states onto a neutral background and saves a PNG,
    /// without creating a window or touching the real pointer.
    /// </summary>
    public static void SavePreview(string path, double scale = 1.5)
    {
        (string? Caption, PointerActivity Activity, double Click)[] samples =
        [
            ("Looking at the screen…", PointerActivity.Thinking, -1),
            ("Open the Start menu", PointerActivity.Clicking, 0.25),
            ("Typing “hello world”", PointerActivity.Typing, -1),
            (null, PointerActivity.Scrolling, -1)
        ];
        var cell = (int)(240 * scale);
        var rowHeight = (int)(130 * scale);
        using var bitmap = new Bitmap(cell * 2, rowHeight * 2, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(Color.FromArgb(255, 32, 38, 46));
        using var font = new Font("Segoe UI Semibold", (float)(12.5 * scale), FontStyle.Regular, GraphicsUnit.Pixel);
        for (var index = 0; index < samples.Length; index++)
        {
            var (caption, activity, click) = samples[index];
            graphics.ResetTransform();
            graphics.TranslateTransform((index % 2 * cell) + (float)(60 * scale), (index / 2 * rowHeight) + (float)(50 * scale));
            DrawHalo(graphics, scale, 0.6, activity, 900);
            if (click >= 0)
                DrawRipple(graphics, scale, click);
            if (caption is not null)
            {
                var textSize = graphics.MeasureString(caption, font);
                var width = (float)(Math.Min(textSize.Width, 320 * scale) + (34 * scale));
                DrawCaption(graphics, font, caption, textSize, scale, (float)(24 * scale), (float)(20 * scale), width, (float)(28 * scale), activity, 0.6);
            }
            // A plain arrow stands in for the real Windows pointer in the preview.
            using var arrow = new GraphicsPath();
            arrow.AddPolygon(new[] { new PointF(0, 0), new PointF(0, (float)(17 * scale)), new PointF((float)(4.5 * scale), (float)(13 * scale)), new PointF((float)(12 * scale), (float)(13 * scale)) });
            graphics.FillPath(Brushes.White, arrow);
            using var outline = new Pen(Color.Black, (float)scale);
            graphics.DrawPath(outline, arrow);
        }
        bitmap.Save(path, ImageFormat.Png);
    }

    private static void DrawHalo(Graphics graphics, double scale, double pulse, PointerActivity activity, double now)
    {
        var glowRadius = (float)((30 + (pulse * 6)) * scale);
        using (var glowPath = new GraphicsPath())
        {
            glowPath.AddEllipse(-glowRadius, -glowRadius, glowRadius * 2, glowRadius * 2);
            using var glow = new PathGradientBrush(glowPath)
            {
                CenterColor = Color.FromArgb((int)(70 + (pulse * 40)), Accent),
                SurroundColors = [Color.FromArgb(0, Accent)]
            };
            graphics.FillEllipse(glow, -glowRadius, -glowRadius, glowRadius * 2, glowRadius * 2);
        }

        var ringRadius = (float)((16 + (pulse * 1.5)) * scale);
        using (var ring = new Pen(Color.FromArgb(235, Accent), (float)(2.4 * scale)))
            graphics.DrawEllipse(ring, -ringRadius, -ringRadius, ringRadius * 2, ringRadius * 2);
        var outerRadius = ringRadius + (float)(3.5 * scale);
        using (var outer = new Pen(Color.FromArgb(70, Color.White), (float)(1.2 * scale)))
            graphics.DrawEllipse(outer, -outerRadius, -outerRadius, outerRadius * 2, outerRadius * 2);

        if (activity is PointerActivity.Thinking or PointerActivity.Waiting)
        {
            // Rotating arc while Autobots is looking at the screen or waiting for an app.
            var arcRadius = ringRadius + (float)(7 * scale);
            using var arc = new Pen(Color.FromArgb(220, AccentSoft), (float)(2.6 * scale)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            var start = (float)(now / 3.2 % 360);
            graphics.DrawArc(arc, -arcRadius, -arcRadius, arcRadius * 2, arcRadius * 2, start, 80);
            graphics.DrawArc(arc, -arcRadius, -arcRadius, arcRadius * 2, arcRadius * 2, start + 180, 80);
        }
        else if (activity is PointerActivity.Scrolling)
        {
            using var chevron = new Pen(Color.FromArgb(230, AccentSoft), (float)(2.2 * scale)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            var offset = (float)((ringRadius + (8 * scale)) + (Math.Sin(now / 90d) * 2 * scale));
            var wing = (float)(5 * scale);
            graphics.DrawLines(chevron, [new PointF(-wing, -offset + wing), new PointF(0, -offset), new PointF(wing, -offset + wing)]);
            graphics.DrawLines(chevron, [new PointF(-wing, offset - wing), new PointF(0, offset), new PointF(wing, offset - wing)]);
        }
    }

    private static void DrawRipple(Graphics graphics, double scale, double progress)
    {
        var eased = 1 - Math.Pow(1 - progress, 3);
        var radius = (float)((12 + (eased * 34)) * scale);
        var alpha = (int)(255 * (1 - progress));
        using var pen = new Pen(Color.FromArgb(alpha, Ripple), (float)((3 - (progress * 2)) * scale));
        graphics.DrawEllipse(pen, -radius, -radius, radius * 2, radius * 2);
        var core = (float)((6 - (progress * 4)) * scale);
        using var fill = new SolidBrush(Color.FromArgb(alpha / 2, Ripple));
        graphics.FillEllipse(fill, -core, -core, core * 2, core * 2);
    }

    private static void DrawCaption(Graphics graphics, Font font, string caption, SizeF textSize, double scale, float x, float y, float width, float height, PointerActivity activity, double pulse)
    {
        var radius = (float)(height / 2);
        using var path = new GraphicsPath();
        path.AddArc(x, y, radius * 2, height, 90, 180);
        path.AddArc(x + width - (radius * 2), y, radius * 2, height, 270, 180);
        path.CloseFigure();

        using (var shadow = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
        {
            graphics.TranslateTransform(0, (float)(2 * scale));
            graphics.FillPath(shadow, path);
            graphics.TranslateTransform(0, (float)(-2 * scale));
        }
        using (var background = new SolidBrush(Color.FromArgb(238, 12, 20, 32)))
            graphics.FillPath(background, path);
        using (var border = new Pen(Color.FromArgb(150, Accent), (float)(1 * scale)))
            graphics.DrawPath(border, path);

        var dotX = x + (float)(14 * scale);
        var dotY = y + (height / 2);
        using (var dot = new SolidBrush(activity == PointerActivity.Clicking ? Ripple : Accent))
        {
            // A small pair of eyes keeps the cursor caption in the same visual family as the pilot.
            var eyeWidth = (float)(3.4 * scale);
            var eyeHeight = (float)((activity is PointerActivity.Thinking or PointerActivity.Waiting ? 6 + pulse : 7) * scale);
            graphics.FillEllipse(dot, dotX - (float)(5 * scale), dotY - eyeHeight / 2, eyeWidth, eyeHeight);
            graphics.FillEllipse(dot, dotX + (float)(1 * scale), dotY - eyeHeight / 2 + (float)scale, eyeWidth, eyeHeight - (float)scale);
        }

        var textX = x + (float)(24 * scale);
        var textWidth = width - (float)(32 * scale);
        using var textBrush = new SolidBrush(Color.FromArgb(255, 242, 247, 251));
        using var format = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Center };
        graphics.DrawString(caption, font, textBrush, new RectangleF(textX, y, textWidth + (float)(4 * scale), height), format);
        _ = textSize;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClass(string className, nint instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(nint window, nint destinationDc, ref NativePoint destination, ref NativeSize size, nint sourceDc, ref NativePoint source, uint colorKey, ref BlendFunction blend, uint flags);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint window, nint dc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint dc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint dc);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint dc, nint gdiObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint gdiObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateDIBSection(nint dc, ref BitmapInfoHeader header, uint usage, out nint bits, nint section, uint offset);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out NativeMessage message, nint window, uint minFilter, uint maxFilter, uint removeMessage);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref NativeMessage message);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref NativeMessage message);

    [DllImport("user32.dll")]
    private static extern uint MsgWaitForMultipleObjects(uint count, nint handles, bool waitAll, uint milliseconds, uint wakeMask);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public nint WindowProcedure;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public string? MenuName;
        public string ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint Window;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }
}
