using System.ComponentModel;
using System.Runtime.InteropServices;
using Autobots.Platform;

namespace Autobots.Platform.Windows;

/// <summary>
/// Registers Ctrl+Alt+Shift+S on a dedicated message-pump thread so STOP remains
/// reachable while Autobots is minimized or waiting on the model/network.
/// </summary>
public sealed class WindowsStopShortcut : IStopShortcut
{
    private const int HotkeyId = 0x4155;
    private const uint ModControl = 0x0002;
    private const uint ModAlt = 0x0001;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const uint WmHotkey = 0x0312;
    private const uint WmQuit = 0x0012;
    private const uint PmNoRemove = 0x0000;

    private readonly TaskCompletionSource _registered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Thread? _thread;
    private uint _threadId;
    private Func<CancellationToken, ValueTask>? _stop;
    private int _disposed;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint window, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(out NativeMessage message, nint window, uint minFilter, uint maxFilter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PeekMessage(out NativeMessage message, nint window, uint minFilter, uint maxFilter, uint removeMessage);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    public async ValueTask RegisterAsync(Func<CancellationToken, ValueTask> stop, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stop);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_thread is not null)
            throw new InvalidOperationException("The global STOP shortcut is already registered.");

        _stop = stop;
        _thread = new Thread(MessageLoop)
        {
            IsBackground = true,
            Name = "Autobots global STOP shortcut"
        };
        _thread.Start();
        await _registered.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        var thread = _thread;
        if (thread is null)
            return;
        if (_threadId != 0)
            _ = PostThreadMessage(_threadId, WmQuit, 0, 0);
        await Task.Run(() => thread.Join(TimeSpan.FromSeconds(3))).ConfigureAwait(false);
    }

    private void MessageLoop()
    {
        _threadId = GetCurrentThreadId();
        _ = PeekMessage(out _, nint.Zero, 0, 0, PmNoRemove); // Create this thread's message queue.
        if (!RegisterHotKey(nint.Zero, HotkeyId, ModControl | ModAlt | ModShift | ModNoRepeat, (uint)'S'))
        {
            _registered.TrySetException(new Win32Exception(Marshal.GetLastWin32Error(), "Ctrl+Alt+Shift+S could not be registered."));
            return;
        }
        _registered.TrySetResult();

        try
        {
            while (true)
            {
                var result = GetMessage(out var message, nint.Zero, 0, 0);
                if (result <= 0)
                    break;
                if (message.Message == WmHotkey && message.WParam == (nuint)HotkeyId)
                {
                    var callback = _stop;
                    if (callback is not null)
                        _ = Task.Run(async () => await callback(CancellationToken.None).ConfigureAwait(false));
                }
            }
        }
        finally
        {
            _ = UnregisterHotKey(nint.Zero, HotkeyId);
        }
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
