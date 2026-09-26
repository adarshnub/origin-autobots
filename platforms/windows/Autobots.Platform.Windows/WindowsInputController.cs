using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using Autobots.Contracts;
using Autobots.Platform;
using DragAction = Autobots.Contracts.DragAction;

namespace Autobots.Platform.Windows;

/// <summary>
/// Windows input is emitted only for a one-use action capability issued by the local supervisor.
/// The process runs at the signed-in user's normal integrity level. The real pointer travels visibly
/// to each target and typing is paced so the owner can follow what Autobots is doing.
/// </summary>
public sealed class WindowsInputController(IAgentPointerOverlay? pointerOverlay = null) : IInputController
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
    private const uint KeyExtended = 0x0001;
    private const uint KeyUp = 0x0002;
    private const uint KeyUnicode = 0x0004;
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;
    private const int WheelNotch = 120;
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020;
    private const uint GaRoot = 2;
    private const uint MapVkToVscEx = 4;
    private static readonly TimeSpan ValidationInterval = TimeSpan.FromMilliseconds(120);

    private readonly object _inputGate = new();
    private readonly HashSet<KeyIdentity> _pressedKeys = [];
    private readonly HashSet<uint> _pressedMouseButtons = [];
    private int _armed;

    public bool IsArmed => Volatile.Read(ref _armed) != 0;
    public event Action<PointerTargetDiagnostic>? PointerTargetObserved;

    /// <summary>Scales pointer travel time: 0.5 is brisk, 1 is the default, 1.6 is slow and easy to follow.</summary>
    public double PointerSpeed { get; set; } = 1.0;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, [In] Input[] inputs, int inputSize);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(nint window, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetKeyboardLayout(uint threadId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern short VkKeyScanEx(char character, nint keyboardLayout);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint MapVirtualKeyEx(uint code, uint mapType, nint keyboardLayout);

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
            var dispatch = new ActionDispatch(authorization, observation, cancellationToken);
            dispatch.Check(force: true);
            await Task.Run(() => ExecuteSynchronously(dispatch), CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            await ReleaseAllAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally
        {
            pointerOverlay?.SetActivity(PointerActivity.Idle);
            Volatile.Write(ref _armed, 0);
        }
    }

    public async ValueTask ReleaseAllAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Run(ReleaseTrackedInputs, CancellationToken.None).ConfigureAwait(false);
    }

    private void ExecuteSynchronously(ActionDispatch dispatch)
    {
        var observation = dispatch.Observation;
        switch (dispatch.Authorization.Envelope.Action)
        {
            case ClickAction click:
            {
                var target = ToDesktop(click.X, click.Y, observation);
                EnsurePointerTargetIsControllable(target, dispatch);
                MovePointer(target, dispatch, dragging: false);
                Thread.Sleep(40);
                dispatch.Check(force: true);
                EnsurePointerTargetIsControllable(target, dispatch);
                var (down, up) = click.Button switch
                {
                    PointerButton.Left => (MouseLeftDown, MouseLeftUp),
                    PointerButton.Right => (MouseRightDown, MouseRightUp),
                    PointerButton.Middle => (MouseMiddleDown, MouseMiddleUp),
                    _ => throw new ActionNotDispatchedException("This pointer button is not allowed.")
                };
                pointerOverlay?.SetActivity(PointerActivity.Clicking);
                // Multi-clicks go out as one batch: the first click may legitimately activate the target
                // window, and Windows recognizes the batch as a double or triple click.
                var clicks = new Input[click.Clicks * 2];
                for (var index = 0; index < click.Clicks; index++)
                {
                    clicks[index * 2] = Mouse(down);
                    clicks[(index * 2) + 1] = Mouse(up);
                }
                SendBatchAndTrack(clicks, dispatch);
                pointerOverlay?.PulseClick();
                break;
            }
            case MoveAction move:
            {
                var target = ToDesktop(move.X, move.Y, observation);
                MovePointer(target, dispatch, dragging: false);
                break;
            }
            case DragAction drag:
            {
                var start = ToDesktop(drag.X, drag.Y, observation);
                var end = ToDesktop(drag.ToX, drag.ToY, observation);
                EnsurePointerTargetIsControllable(start, dispatch);
                EnsurePointerTargetIsControllable(end, dispatch);
                MovePointer(start, dispatch, dragging: false);
                Thread.Sleep(60);
                dispatch.Check(force: true);
                pointerOverlay?.SetActivity(PointerActivity.Dragging);
                SendBatchAndTrack([Mouse(MouseLeftDown)], dispatch);
                Thread.Sleep(90);
                MovePointer(end, dispatch, dragging: true);
                Thread.Sleep(70);
                SendBatchAndTrack([Mouse(MouseLeftUp)], dispatch);
                break;
            }
            case ScrollAction scroll:
            {
                var target = ToDesktop(scroll.X, scroll.Y, observation);
                EnsurePointerTargetIsControllable(target, dispatch);
                MovePointer(target, dispatch, dragging: false);
                pointerOverlay?.SetActivity(PointerActivity.Scrolling);
                // Roughly 100 model pixels per wheel notch, sent one notch at a time for smooth scrolling.
                var vertical = Math.Clamp((int)Math.Round(-scroll.DeltaY / 100d, MidpointRounding.AwayFromZero), -12, 12);
                var horizontal = Math.Clamp((int)Math.Round(scroll.DeltaX / 100d, MidpointRounding.AwayFromZero), -12, 12);
                if (vertical == 0 && scroll.DeltaY != 0) vertical = scroll.DeltaY > 0 ? -1 : 1;
                if (horizontal == 0 && scroll.DeltaX != 0) horizontal = scroll.DeltaX > 0 ? 1 : -1;
                for (var notch = 0; notch < Math.Abs(vertical); notch++)
                {
                    dispatch.Check();
                    SendBatchAndTrack([Mouse(MouseWheel, unchecked((uint)(Math.Sign(vertical) * WheelNotch)))], dispatch);
                    Thread.Sleep(35);
                }
                for (var notch = 0; notch < Math.Abs(horizontal); notch++)
                {
                    dispatch.Check();
                    SendBatchAndTrack([Mouse(MouseHorizontalWheel, unchecked((uint)(Math.Sign(horizontal) * WheelNotch)))], dispatch);
                    Thread.Sleep(35);
                }
                break;
            }
            case TypeTextAction typeText:
                EnsureKeyboardTargetIsControllable(dispatch);
                EnsureFocusedControlAcceptsText();
                // Modern Notepad can acknowledge a large SendInput batch while dropping most of
                // its Unicode characters. A blank document gives us an exact, read-only check.
                var verifyBlankNotepad = TryReadFocusedNotepadText() is { Length: 0 };
                pointerOverlay?.SetActivity(PointerActivity.Typing);
                TypeText(typeText.Text, dispatch);
                if (typeText.PressEnter)
                {
                    Thread.Sleep(60);
                    dispatch.Check(force: true);
                    SendBatchAndTrack(KeyStroke(0x0D, dispatch.KeyboardLayout), dispatch);
                }
                if (verifyBlankNotepad && !typeText.PressEnter)
                    VerifyNotepadText(typeText.Text, dispatch);
                break;
            case KeyPressAction keyPress:
            {
                if (!KeyChord.TryParse(keyPress.Key, out var chord, out var error))
                    throw new ActionNotDispatchedException(error);
                if (chord.BlockedReason() is { } blocked)
                    throw new ActionNotDispatchedException(blocked);
                if (chord.Modifiers == KeyModifiers.Alt && chord.Key == "F4" && IsDesktopShell(GetForegroundWindow()))
                    throw new ActionNotDispatchedException("Alt+F4 on the desktop opens the Windows shut-down dialog, so it is not allowed.");
                EnsureKeyboardTargetIsControllable(dispatch);
                pointerOverlay?.SetActivity(PointerActivity.Typing);
                SendBatchAndTrack(ChordInputs(chord, dispatch.KeyboardLayout), dispatch);
                break;
            }
            case WaitAction wait:
                pointerOverlay?.SetActivity(PointerActivity.Waiting);
                if (dispatch.CancellationToken.WaitHandle.WaitOne(wait.DurationMs))
                    dispatch.CancellationToken.ThrowIfCancellationRequested();
                break;
            default:
                throw new ActionNotDispatchedException("The proposed action type is not supported on Windows.");
        }
    }

    private void MovePointer((int X, int Y) target, ActionDispatch dispatch, bool dragging)
    {
        pointerOverlay?.SetActivity(dragging ? PointerActivity.Dragging : PointerActivity.Moving);
        if (!GetCursorPos(out var start))
            start = new NativePoint { X = target.X, Y = target.Y };
        double dx = target.X - start.X;
        double dy = target.Y - start.Y;
        var distance = Math.Sqrt((dx * dx) + (dy * dy));
        if (distance >= 2)
        {
            var speed = Math.Clamp(PointerSpeed, 0.4, 2.0);
            var durationMs = Math.Clamp(150 + (distance * 0.3), 170, 650) * speed * (dragging ? 1.4 : 1);
            // A gentle arc reads as deliberate pointer travel rather than a teleport.
            var bend = Math.Clamp(distance * 0.1, 0, 80) * (dx >= 0 ? 1 : -1);
            var controlX = start.X + (dx / 2) + (-dy / distance * bend);
            var controlY = start.Y + (dy / 2) + (dx / distance * bend);
            var clock = Stopwatch.StartNew();
            while (true)
            {
                dispatch.Check();
                var t = Math.Min(1, clock.Elapsed.TotalMilliseconds / durationMs);
                var eased = t < 0.5 ? 4 * t * t * t : 1 - (Math.Pow((-2 * t) + 2, 3) / 2);
                var inverse = 1 - eased;
                var x = (inverse * inverse * start.X) + (2 * inverse * eased * controlX) + (eased * eased * target.X);
                var y = (inverse * inverse * start.Y) + (2 * inverse * eased * controlY) + (eased * eased * target.Y);
                SendPointerMove((int)Math.Round(x), (int)Math.Round(y), dispatch);
                if (t >= 1)
                    break;
                Thread.Sleep(8);
            }
        }
        SendPointerMove(target.X, target.Y, dispatch);
        // SendInput acknowledges queuing. Measure the actual physical cursor before pressing a
        // button so clipped movement, scaling errors or pointer interference cannot become a click.
        var arrived = false;
        NativePoint actual = default;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            dispatch.Check(force: true);
            if (GetCursorPos(out actual) && Math.Abs(actual.X - target.X) <= 2 && Math.Abs(actual.Y - target.Y) <= 2)
            {
                arrived = true;
                break;
            }
            Thread.Sleep(10);
        }
        PointerTargetObserved?.Invoke(new PointerTargetDiagnostic(dispatch.Authorization.Envelope.Sequence,
            target.X, target.Y, actual.X, actual.Y));
        if (!arrived)
            throw dispatch.Fail("The pointer did not reach the observed target. No click was sent; observe again.");
    }

    private void TypeText(string text, ActionDispatch dispatch)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        // Send one character per batch. Bursting 24 characters at once was observed to leave a
        // 122-character Notepad document containing mostly spaces and periods after a 168-char
        // proposal. A small gap lets the editor consume each WM_CHAR before the next batch.
        for (var index = 0; index < normalized.Length; index++)
        {
            dispatch.Check();
            var character = normalized[index];
            if (character == '\n')
                SendBatchAndTrack(KeyStroke(0x0D, dispatch.KeyboardLayout), dispatch);
            else if (character == '\t')
                SendBatchAndTrack(KeyStroke(0x09, dispatch.KeyboardLayout), dispatch);
            else if (!char.IsControl(character))
            {
                SendBatchAndTrack([UnicodeKey(character, keyUp: false), UnicodeKey(character, keyUp: true)], dispatch);
            }
            Thread.Sleep(25);
        }
    }

    private static string? TryReadFocusedNotepadText()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is null || focused.Current.ProcessId <= 0 ||
                !Process.GetProcessById(focused.Current.ProcessId).ProcessName.Equals("Notepad", StringComparison.OrdinalIgnoreCase))
                return null;
            var window = AutomationElement.RootElement.FindFirst(TreeScope.Children,
                new PropertyCondition(AutomationElement.ProcessIdProperty, focused.Current.ProcessId));
            var document = window?.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));
            if (document is null || !document.TryGetCurrentPattern(TextPattern.Pattern, out var pattern) || pattern is not TextPattern textPattern)
                return null;
            return textPattern.DocumentRange.GetText(-1).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
        }
        catch (ElementNotAvailableException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (ArgumentException) { return null; }
    }

    private static void VerifyNotepadText(string expected, ActionDispatch dispatch)
    {
        var normalized = expected.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd('\n');
        string? observed = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            dispatch.Check(force: true);
            observed = TryReadFocusedNotepadText();
            if (observed == normalized)
                return;
            Thread.Sleep(80);
        }
        throw new ActionInterruptedException($"Notepad retained {observed?.Length ?? 0} of {normalized.Length} expected characters after typing. Autobots stopped before copying or sharing uncertain text.");
    }

    private Input[] ChordInputs(KeyChord chord, nint layout)
    {
        var modifiers = new List<ushort>(4);
        var main = chord.Key switch
        {
            "Control" => (ushort)0x11,
            "Shift" => (ushort)0x10,
            "Alt" => (ushort)0x12,
            "Meta" => (ushort)0x5B,
            _ => (ushort)0
        };
        if (main == 0)
        {
            main = NamedVirtualKey(chord.Key) ?? 0;
            if (main == 0)
            {
                if (chord.Key.Length != 1)
                    throw new ActionNotDispatchedException($"The key '{chord}' is not available on Windows.");
                var scan = VkKeyScanEx(chord.Key[0], layout);
                if (scan == -1)
                    throw new ActionNotDispatchedException($"The key '{chord.Key}' is not on the current keyboard layout.");
                main = (ushort)(scan & 0xFF);
                var shiftState = (scan >> 8) & 0xFF;
                if ((shiftState & 1) != 0 && !chord.Modifiers.HasFlag(KeyModifiers.Shift)) modifiers.Add(0x10);
                if ((shiftState & 2) != 0 && !chord.Modifiers.HasFlag(KeyModifiers.Control)) modifiers.Add(0x11);
                if ((shiftState & 4) != 0 && !chord.Modifiers.HasFlag(KeyModifiers.Alt)) modifiers.Add(0x12);
            }
        }
        if (chord.Modifiers.HasFlag(KeyModifiers.Control)) modifiers.Insert(0, 0x11);
        if (chord.Modifiers.HasFlag(KeyModifiers.Alt)) modifiers.Add(0x12);
        if (chord.Modifiers.HasFlag(KeyModifiers.Shift)) modifiers.Add(0x10);
        if (chord.Modifiers.HasFlag(KeyModifiers.Meta)) modifiers.Add(0x5B);

        var inputs = new List<Input>((modifiers.Count * 2) + 2);
        foreach (var modifier in modifiers.Distinct())
            inputs.Add(VirtualKey(modifier, keyUp: false, layout));
        inputs.Add(VirtualKey(main, keyUp: false, layout));
        inputs.Add(VirtualKey(main, keyUp: true, layout));
        foreach (var modifier in modifiers.Distinct().Reverse())
            inputs.Add(VirtualKey(modifier, keyUp: true, layout));
        return inputs.ToArray();
    }

    private static ushort? NamedVirtualKey(string key)
    {
        if (key.Length is 2 or 3 && key[0] == 'F' && int.TryParse(key.AsSpan(1), out var function) && function is >= 1 and <= 24)
            return (ushort)(0x70 + function - 1);
        return key switch
        {
            "Enter" => 0x0D,
            "Tab" => 0x09,
            "Escape" => 0x1B,
            "Space" => 0x20,
            "Backspace" => 0x08,
            "Delete" => 0x2E,
            "Insert" => 0x2D,
            "Home" => 0x24,
            "End" => 0x23,
            "PageUp" => 0x21,
            "PageDown" => 0x22,
            "ArrowUp" => 0x26,
            "ArrowDown" => 0x28,
            "ArrowLeft" => 0x25,
            "ArrowRight" => 0x27,
            "CapsLock" => 0x14,
            "PrintScreen" => 0x2C,
            "ContextMenu" => 0x5D,
            "VolumeMute" => 0xAD,
            "VolumeDown" => 0xAE,
            "VolumeUp" => 0xAF,
            "MediaNext" => 0xB0,
            "MediaPrevious" => 0xB1,
            "MediaStop" => 0xB2,
            "MediaPlayPause" => 0xB3,
            _ => null
        };
    }

    private static void EnsureFocusedControlAcceptsText()
    {
        try
        {
            var focused = AutomationElement.FocusedElement
                ?? throw new ActionNotDispatchedException("The focused control could not be identified, so no text was typed.");
            var properties = focused.Current;
            if (properties.IsPassword)
                throw new ActionNotDispatchedException("Autobots will not type into a password field.");
            if (properties.ControlType == ControlType.Edit || properties.ControlType == ControlType.Document || properties.ControlType == ControlType.ComboBox)
                return;
            if (focused.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePattern) && valuePattern is ValuePattern value && !value.Current.IsReadOnly)
                return;
            // Terminals and custom editors expose text through the text pattern instead of the edit control type.
            if (properties.IsKeyboardFocusable && focused.TryGetCurrentPattern(TextPattern.Pattern, out _))
                return;
            throw new ActionNotDispatchedException("The focused control is not an accessible text-entry field, so no text was typed. Click the field first.");
        }
        catch (ActionNotDispatchedException)
        {
            throw;
        }
        catch (Exception error) when (error is COMException or ElementNotAvailableException or InvalidOperationException or UnauthorizedAccessException)
        {
            throw new ActionNotDispatchedException("The focused control could not be verified as a non-password text field, so no text was typed.", error);
        }
    }

    private static void EnsurePointerTargetIsControllable((int X, int Y) target, ActionDispatch dispatch)
    {
        var window = WindowFromPoint(new NativePoint { X = target.X, Y = target.Y });
        if (window == 0)
            throw dispatch.Fail("No window is under the proposed point.");
        var root = GetAncestor(window, GaRoot);
        if (root == 0)
            root = window;
        _ = GetWindowThreadProcessId(root, out var processId);
        if (processId == Environment.ProcessId && (GetWindowLongPtr(root, GwlExStyle) & WsExTransparent) == 0)
            throw dispatch.Fail("Autobots' own window covers that point, so the action was not sent.");
        if (WindowsProcessIntegrity.IsAboveCurrentProcess((int)processId))
            throw dispatch.Fail("That window belongs to an app running with administrator rights. Autobots runs without elevation and cannot control it.");
    }

    private static void EnsureKeyboardTargetIsControllable(ActionDispatch dispatch)
    {
        if (dispatch.Authorization.ForegroundProcessId is { } processId && WindowsProcessIntegrity.IsAboveCurrentProcess(processId))
            throw dispatch.Fail("The active window belongs to an app running with administrator rights. Autobots runs without elevation and cannot type into it.");
    }

    private static bool IsDesktopShell(nint window)
    {
        var className = new StringBuilder(64);
        _ = GetClassName(window, className, className.Capacity);
        return className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    private static (int X, int Y) ToDesktop(int normalizedX, int normalizedY, CapturedFrame observation) =>
        DisplayCoordinateTransform.ToDesktopPixels(
            normalizedX, normalizedY, observation.PixelWidth, observation.PixelHeight,
            observation.DesktopOriginX, observation.DesktopOriginY);

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

    private static void EnsureForegroundMatches(AuthorizedAction authorization)
    {
        var foreground = GetForegroundWindow();
        if (foreground != authorization.ForegroundWindowHandle)
            throw new UnauthorizedAccessException("The active window changed after the screen was captured.");
        var threadId = GetWindowThreadProcessId(foreground, out var processId);
        if (threadId == 0 || authorization.ForegroundProcessId != (int)processId)
            throw new UnauthorizedAccessException("The active application changed after the screen was captured.");
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
            throw new UnauthorizedAccessException("The display layout changed after capture.");

        var dpi = GetDpiForWindow(GetForegroundWindow());
        if (dpi != 0 && (Math.Abs(dpi - observation.DpiX) > 1 || Math.Abs(dpi - observation.DpiY) > 1))
            throw new UnauthorizedAccessException("The display scaling changed after capture.");
    }

    private void SendPointerMove(int x, int y, ActionDispatch dispatch)
    {
        var observation = dispatch.Observation;
        x = Math.Clamp(x, observation.VirtualDesktopOriginX, observation.VirtualDesktopOriginX + observation.VirtualDesktopWidth - 1);
        y = Math.Clamp(y, observation.VirtualDesktopOriginY, observation.VirtualDesktopOriginY + observation.VirtualDesktopHeight - 1);
        var inputs = new[] { AbsoluteMove(x, y, observation) };
        lock (_inputGate)
        {
            dispatch.CancellationToken.ThrowIfCancellationRequested();
            if (SendInput(1, inputs, Marshal.SizeOf<Input>()) != 1)
                throw dispatch.Fail("Windows rejected the pointer movement.");
        }
    }

    private void SendBatchAndTrack(Input[] inputs, ActionDispatch dispatch)
    {
        dispatch.CancellationToken.ThrowIfCancellationRequested();
        lock (_inputGate)
        {
            var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
            RecordPressedState(inputs, (int)Math.Min(sent, (uint)inputs.Length));
            if (sent > 0)
                dispatch.MarkInputSent();
            if (sent != (uint)inputs.Length)
            {
                ReleaseTrackedInputsLocked();
                throw new ActionInterruptedException("Windows rejected some or all input events. The action result is uncertain, so Autobots stopped the task.");
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
                    ? new KeyIdentity(true, input.Data.Keyboard.ScanCode, 0)
                    : new KeyIdentity(false, input.Data.Keyboard.VirtualKey, input.Data.Keyboard.ScanCode);
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
            : new Input
            {
                Type = InputKeyboard,
                Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = key.Code, ScanCode = key.ScanCode, Flags = KeyUp | (IsExtendedKey(key.Code) ? KeyExtended : 0) } }
            }));
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

    private static Input[] KeyStroke(ushort virtualKey, nint layout) =>
        [VirtualKey(virtualKey, keyUp: false, layout), VirtualKey(virtualKey, keyUp: true, layout)];

    private static Input VirtualKey(ushort virtualKey, bool keyUp, nint layout)
    {
        var scan = MapVirtualKeyEx(virtualKey, MapVkToVscEx, layout);
        var extended = IsExtendedKey(virtualKey) || (scan & 0xFF00) is 0xE000 or 0xE100;
        return new Input
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = virtualKey,
                    ScanCode = (ushort)(scan & 0xFF),
                    Flags = (keyUp ? KeyUp : 0) | (extended ? KeyExtended : 0)
                }
            }
        };
    }

    private static bool IsExtendedKey(ushort virtualKey) => virtualKey is
        0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2C or 0x2D or 0x2E or
        0x5B or 0x5C or 0x5D or 0xAD or 0xAE or 0xAF or 0xB0 or 0xB1 or 0xB2 or 0xB3;

    private static Input UnicodeKey(char character, bool keyUp) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion { Keyboard = new KeyboardInput { ScanCode = character, Flags = KeyUnicode | (keyUp ? KeyUp : 0) } }
    };

    /// <summary>
    /// Per-action dispatch state. Validation failures before any input reaches Windows mean the action was
    /// not sent; failures after that point leave an uncertain outcome.
    /// </summary>
    private sealed class ActionDispatch
    {
        private readonly Stopwatch _sinceValidation = new();
        private bool _inputSent;

        public ActionDispatch(AuthorizedAction authorization, CapturedFrame observation, CancellationToken cancellationToken)
        {
            Authorization = authorization;
            Observation = observation;
            CancellationToken = cancellationToken;
            var threadId = GetWindowThreadProcessId(authorization.ForegroundWindowHandle, out _);
            KeyboardLayout = GetKeyboardLayout(threadId);
        }

        public AuthorizedAction Authorization { get; }
        public CapturedFrame Observation { get; }
        public CancellationToken CancellationToken { get; }
        public nint KeyboardLayout { get; }

        public void MarkInputSent() => _inputSent = true;

        public void Check(bool force = false)
        {
            CancellationToken.ThrowIfCancellationRequested();
            if (!force && _sinceValidation.IsRunning && _sinceValidation.Elapsed < ValidationInterval)
                return;
            try
            {
                ValidateAuthorization(Authorization, Observation);
                EnsureForegroundMatches(Authorization);
                EnsureDisplayMatches(Observation);
            }
            catch (UnauthorizedAccessException error) when (error is not ActionNotDispatchedException)
            {
                throw Fail(error.Message);
            }
            _sinceValidation.Restart();
        }

        public Exception Fail(string reason) => _inputSent
            ? new ActionInterruptedException($"{reason} Autobots stopped part-way through this action, so its result is uncertain.")
            : new ActionNotDispatchedException($"{reason} No input was sent.");
    }

    private readonly record struct KeyIdentity(bool IsUnicode, ushort Code, ushort ScanCode);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

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
