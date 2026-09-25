using System.Runtime.InteropServices;
using System.Windows.Automation;
using Autobots.Contracts;
using Autobots.Platform;

namespace Autobots.Platform.Windows;

/// <summary>
/// Windows input is emitted only for a one-use action capability issued by the local supervisor.
/// The process runs at the signed-in user's normal integrity level.
/// </summary>
public sealed class WindowsInputController : IInputController
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint MouseMove = 0x0001;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseRightDown = 0x0008;
    private const uint MouseRightUp = 0x0010;
    private const uint MouseMiddleDown = 0x0020;
    private const uint MouseMiddleUp = 0x0040;
    private const uint MouseWheel = 0x0800;
    private const uint MouseHorizontalWheel = 0x1000;
    private const uint MouseAbsolute = 0x8000;
    private const uint MouseVirtualDesk = 0x4000;
    private const uint KeyUp = 0x0002;
    private const uint KeyUnicode = 0x0004;
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;
    private const int MaxEventsPerBatch = 64;

    private readonly object _inputGate = new();
    private readonly HashSet<KeyIdentity> _pressedKeys = [];
    private readonly HashSet<uint> _pressedMouseButtons = [];
    private int _armed;

    public bool IsArmed => Volatile.Read(ref _armed) != 0;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, [In] Input[] inputs, int inputSize);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    public async ValueTask ExecuteAuthorizedActionAsync(
        AuthorizedAction authorization,
        CapturedFrame observation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(observation);
        if (Interlocked.CompareExchange(ref _armed, 1, 0) != 0)
            throw new InvalidOperationException("Another local input action is already active.");

        try
        {
            ValidateAuthorization(authorization, observation);
            await Task.Run(() => ExecuteSynchronously(authorization, observation, cancellationToken), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch
        {
            await ReleaseAllAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally
        {
            Volatile.Write(ref _armed, 0);
        }
    }

    public async ValueTask ReleaseAllAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Run(ReleaseTrackedInputs, CancellationToken.None).ConfigureAwait(false);
    }

    private static void ValidateAuthorization(AuthorizedAction authorization, CapturedFrame observation)
    {
        var now = DateTimeOffset.UtcNow;
        if (authorization.ExpiresAt <= now || authorization.AuthorizedAt > now.AddSeconds(5))
            throw new UnauthorizedAccessException("The local task authorization expired. Capture a fresh screen before acting.");
        if (!Guid.TryParse(observation.ObservationId, out var observationId) || authorization.ObservationId != observationId ||
            authorization.Envelope.ObservationId != observationId || authorization.LayoutGeneration != observation.LayoutGeneration ||
            authorization.ForegroundWindowHandle != observation.ForegroundWindowHandle ||
            authorization.ForegroundProcessId != observation.ForegroundProcessId ||
            observation.ForegroundWindowHandle == 0 || observation.ForegroundProcessId is null)
            throw new UnauthorizedAccessException("The authorized action no longer matches its screen observation.");
        if (now - observation.CapturedAt > TimeSpan.FromMinutes(2))
            throw new UnauthorizedAccessException("The screen observation is too old. Capture again before acting.");
    }

    private void ExecuteSynchronously(AuthorizedAction authorization, CapturedFrame observation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureForegroundMatches(authorization);
        EnsureDisplayMatches(observation);

        switch (authorization.Envelope.Action)
        {
            case ClickAction click:
            {
                var point = DisplayCoordinateTransform.ToDesktopPixels(
                    click.X, click.Y, observation.PixelWidth, observation.PixelHeight,
                    observation.DesktopOriginX, observation.DesktopOriginY);
                var inputs = new List<Input>(3) { AbsoluteMove(point.X, point.Y, observation) };
                var (down, up) = click.Button switch
                {
                    PointerButton.Left => (MouseLeftDown, MouseLeftUp),
                    PointerButton.Right => (MouseRightDown, MouseRightUp),
                    PointerButton.Middle => (MouseMiddleDown, MouseMiddleUp),
                    _ => throw new UnauthorizedAccessException("This pointer button is not allowed.")
                };
                inputs.Add(Mouse(down));
                inputs.Add(Mouse(up));
                SendInCancellableBatches(inputs, authorization, observation, cancellationToken);
                break;
            }
            case TypeTextAction typeText:
                EnsureFocusedControlIsNotPassword();
                SendUnicodeText(typeText.Text, authorization, observation, cancellationToken);
                break;
            case KeyPressAction keyPress:
                SendKeyPress(keyPress.Key, authorization, observation, cancellationToken);
                break;
            case ScrollAction scroll:
            {
                var point = DisplayCoordinateTransform.ToDesktopPixels(
                    scroll.X, scroll.Y, observation.PixelWidth, observation.PixelHeight,
                    observation.DesktopOriginX, observation.DesktopOriginY);
                var inputs = new List<Input>(3) { AbsoluteMove(point.X, point.Y, observation) };
                if (scroll.DeltaY != 0)
                    inputs.Add(Mouse(MouseWheel, unchecked((uint)Math.Clamp(-scroll.DeltaY * 120 / 300, -480, 480))));
                if (scroll.DeltaX != 0)
                    inputs.Add(Mouse(MouseHorizontalWheel, unchecked((uint)Math.Clamp(scroll.DeltaX * 120 / 300, -480, 480))));
                SendInCancellableBatches(inputs, authorization, observation, cancellationToken);
                break;
            }
            case WaitAction wait:
                Task.Delay(wait.DurationMs, cancellationToken).GetAwaiter().GetResult();
                break;
            default:
                throw new UnauthorizedAccessException("The proposed action type is not supported on Windows.");
        }
    }

    private static void EnsureForegroundMatches(AuthorizedAction authorization)
    {
        var foreground = GetForegroundWindow();
        if (foreground != authorization.ForegroundWindowHandle)
            throw new UnauthorizedAccessException("The target application lost focus. No action was sent.");
        var threadId = GetWindowThreadProcessId(foreground, out var processId);
        if (threadId == 0 || authorization.ForegroundProcessId != (int)processId)
            throw new UnauthorizedAccessException("The target application changed. No action was sent.");
    }

    private static void EnsureDisplayMatches(CapturedFrame observation)
    {
        var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds
            ?? throw new PlatformCapabilityUnavailableException("No primary display is available.");
        if (bounds.Left != observation.DesktopOriginX || bounds.Top != observation.DesktopOriginY ||
            bounds.Width != observation.PixelWidth || bounds.Height != observation.PixelHeight ||
            GetSystemMetrics(SmXVirtualScreen) != observation.VirtualDesktopOriginX ||
            GetSystemMetrics(SmYVirtualScreen) != observation.VirtualDesktopOriginY ||
            GetSystemMetrics(SmCxVirtualScreen) != observation.VirtualDesktopWidth ||
            GetSystemMetrics(SmCyVirtualScreen) != observation.VirtualDesktopHeight)
            throw new UnauthorizedAccessException("The display layout changed after capture. Capture again before acting.");

        using var graphics = System.Drawing.Graphics.FromHwnd(GetForegroundWindow());
        if (Math.Abs(graphics.DpiX - observation.DpiX) > 1 || Math.Abs(graphics.DpiY - observation.DpiY) > 1)
            throw new UnauthorizedAccessException("The target display scaling changed after capture. Capture again before acting.");
    }

    private static void EnsureFocusedControlIsNotPassword()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is null)
                throw new UnauthorizedAccessException("The focused control could not be identified, so no text was typed.");
            var properties = focused.Current;
            if (properties.IsPassword)
                throw new UnauthorizedAccessException("Autobots will not type into a password or unverified field.");
            if (properties.ControlType != ControlType.Edit && properties.ControlType != ControlType.ComboBox && properties.ControlType != ControlType.Document)
                throw new UnauthorizedAccessException("The focused control is not an accessible text-entry field, so no text was typed.");
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (Exception error) when (error is COMException or ElementNotAvailableException or InvalidOperationException)
        {
            throw new UnauthorizedAccessException("The focused control could not be verified as a non-password field, so no text was typed.", error);
        }
    }

    private void SendUnicodeText(string text, AuthorizedAction authorization, CapturedFrame observation, CancellationToken cancellationToken)
    {
        var inputs = new List<Input>(text.Length * 2);
        foreach (var codeUnit in text)
        {
            inputs.Add(UnicodeKey(codeUnit, keyUp: false));
            inputs.Add(UnicodeKey(codeUnit, keyUp: true));
        }
        SendInCancellableBatches(inputs, authorization, observation, cancellationToken);
    }

    private void SendKeyPress(string key, AuthorizedAction authorization, CapturedFrame observation, CancellationToken cancellationToken)
    {
        var pieces = key.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (pieces.Length is < 1 or > 3)
            throw new UnauthorizedAccessException("The key combination is not allowed.");

        var modifiers = new List<ushort>();
        ushort? mainKey = null;
        foreach (var piece in pieces)
        {
            var normalized = piece.ToUpperInvariant();
            if (normalized is "CTRL" or "CONTROL") modifiers.Add(0x11);
            else if (normalized is "SHIFT") modifiers.Add(0x10);
            else if (normalized is "ALT") modifiers.Add(0x12);
            else if (mainKey is null) mainKey = ParseVirtualKey(normalized);
            else throw new UnauthorizedAccessException("The key combination is not allowed.");
        }
        if (mainKey is null || modifiers.Distinct().Count() != modifiers.Count || modifiers.Contains(mainKey.Value))
            throw new UnauthorizedAccessException("The key combination is not allowed.");
        if (mainKey.Value is 0x5B or 0x5C or 0x2E && modifiers.Contains((ushort)0x11) && modifiers.Contains((ushort)0x12))
            throw new UnauthorizedAccessException("Windows security and system shortcuts are not allowed.");

        var inputs = new List<Input>(modifiers.Count * 2 + 2);
        foreach (var modifier in modifiers)
            inputs.Add(VirtualKey(modifier, keyUp: false));
        inputs.Add(VirtualKey(mainKey.Value, keyUp: false));
        inputs.Add(VirtualKey(mainKey.Value, keyUp: true));
        foreach (var modifier in modifiers.AsEnumerable().Reverse())
            inputs.Add(VirtualKey(modifier, keyUp: true));
        SendInCancellableBatches(inputs, authorization, observation, cancellationToken);
    }

    private static ushort ParseVirtualKey(string key)
    {
        if (key.Length == 1 && ((key[0] is >= 'A' and <= 'Z') || (key[0] is >= '0' and <= '9')))
            return key[0];
        if (key.Length is 2 or 3 && key[0] == 'F' && ushort.TryParse(key.AsSpan(1), out var functionKey) && functionKey is >= 1 and <= 12)
            return (ushort)(0x70 + functionKey - 1);
        return key switch
        {
            "ENTER" or "RETURN" => 0x0D,
            "TAB" => 0x09,
            "ESC" or "ESCAPE" => 0x1B,
            "SPACE" => 0x20,
            "BACKSPACE" => 0x08,
            "DELETE" or "DEL" => 0x2E,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" => 0x21,
            "PAGEDOWN" => 0x22,
            "ARROWUP" or "UP" => 0x26,
            "ARROWDOWN" or "DOWN" => 0x28,
            "ARROWLEFT" or "LEFT" => 0x25,
            "ARROWRIGHT" or "RIGHT" => 0x27,
            _ => throw new UnauthorizedAccessException($"The key '{key}' is not in the Windows input allowlist.")
        };
    }

    private void SendInCancellableBatches(IReadOnlyList<Input> inputs, AuthorizedAction authorization, CapturedFrame observation, CancellationToken cancellationToken)
    {
        for (var offset = 0; offset < inputs.Count; offset += MaxEventsPerBatch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateAuthorization(authorization, observation);
            EnsureForegroundMatches(authorization);
            EnsureDisplayMatches(observation);
            var count = Math.Min(MaxEventsPerBatch, inputs.Count - offset);
            var batch = inputs.Skip(offset).Take(count).ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            SendBatchAndTrack(batch);
        }
    }

    private void SendBatchAndTrack(Input[] inputs)
    {
        lock (_inputGate)
        {
            var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
            RecordPressedState(inputs, (int)Math.Min(sent, (uint)inputs.Length));
            if (sent != (uint)inputs.Length)
            {
                ReleaseTrackedInputsLocked();
                throw new InvalidOperationException("Windows rejected some or all input events. The action result is uncertain and Autobots stopped the task.");
            }
        }
    }

    private void RecordPressedState(IReadOnlyList<Input> inputs, int count)
    {
        for (var index = 0; index < count; index++)
        {
            var input = inputs[index];
            if (input.Type == InputKeyboard)
            {
                var key = (input.Data.Keyboard.Flags & KeyUnicode) != 0
                    ? new KeyIdentity(true, input.Data.Keyboard.ScanCode)
                    : new KeyIdentity(false, input.Data.Keyboard.VirtualKey);
                if ((input.Data.Keyboard.Flags & KeyUp) != 0) _pressedKeys.Remove(key);
                else _pressedKeys.Add(key);
            }
            else if (input.Type == InputMouse)
            {
                UpdateMouseButton(input.Data.Mouse.Flags, MouseLeftDown, MouseLeftUp);
                UpdateMouseButton(input.Data.Mouse.Flags, MouseRightDown, MouseRightUp);
                UpdateMouseButton(input.Data.Mouse.Flags, MouseMiddleDown, MouseMiddleUp);
            }
        }
    }

    private void UpdateMouseButton(uint flags, uint down, uint up)
    {
        if ((flags & down) != 0) _pressedMouseButtons.Add(down);
        if ((flags & up) != 0) _pressedMouseButtons.Remove(down);
    }

    private void ReleaseTrackedInputs()
    {
        lock (_inputGate)
            ReleaseTrackedInputsLocked();
    }

    private void ReleaseTrackedInputsLocked()
    {
        var releases = new List<Input>(_pressedKeys.Count + _pressedMouseButtons.Count);
        releases.AddRange(_pressedKeys.Select(key => key.IsUnicode
            ? UnicodeKey((char)key.Code, keyUp: true)
            : VirtualKey(key.Code, keyUp: true)));
        foreach (var button in _pressedMouseButtons)
            releases.Add(Mouse(button switch
            {
                MouseLeftDown => MouseLeftUp,
                MouseRightDown => MouseRightUp,
                MouseMiddleDown => MouseMiddleUp,
                _ => 0
            }));
        if (releases.Count > 0)
            _ = SendInput((uint)releases.Count, releases.ToArray(), Marshal.SizeOf<Input>());
        _pressedKeys.Clear();
        _pressedMouseButtons.Clear();
    }

    private static Input AbsoluteMove(int x, int y, CapturedFrame observation)
    {
        var virtualLeft = observation.VirtualDesktopOriginX;
        var virtualTop = observation.VirtualDesktopOriginY;
        var virtualWidth = observation.VirtualDesktopWidth;
        var virtualHeight = observation.VirtualDesktopHeight;
        if (virtualWidth < 2 || virtualHeight < 2)
            throw new PlatformCapabilityUnavailableException("Windows did not report a usable virtual desktop size.");
        var absoluteX = (int)Math.Round((x - virtualLeft) * 65535d / (virtualWidth - 1), MidpointRounding.AwayFromZero);
        var absoluteY = (int)Math.Round((y - virtualTop) * 65535d / (virtualHeight - 1), MidpointRounding.AwayFromZero);
        return Mouse(MouseMove | MouseAbsolute | MouseVirtualDesk, 0, absoluteX, absoluteY);
    }

    private static Input Mouse(uint flags, uint data = 0, int x = 0, int y = 0) => new()
    {
        Type = InputMouse,
        Data = new InputUnion { Mouse = new MouseInput { X = x, Y = y, Data = data, Flags = flags } }
    };

    private static Input VirtualKey(ushort virtualKey, bool keyUp) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = virtualKey, Flags = keyUp ? KeyUp : 0 } }
    };

    private static Input UnicodeKey(char character, bool keyUp) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion { Keyboard = new KeyboardInput { ScanCode = character, Flags = KeyUnicode | (keyUp ? KeyUp : 0) } }
    };

    private readonly record struct KeyIdentity(bool IsUnicode, ushort Code);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint Data;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }
}
