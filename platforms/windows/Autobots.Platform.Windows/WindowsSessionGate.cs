using System.Runtime.InteropServices;
using System.Text;
using Autobots.Platform;

namespace Autobots.Platform.Windows;

/// <summary>
/// Reports whether the signed-in user's normal desktop is receiving input. The lock screen and the UAC
/// secure desktop switch input to another desktop, where Autobots must hand control back to the owner.
/// </summary>
public sealed class WindowsSessionGate : IInteractiveSessionGate
{
    private const uint DesktopReadObjects = 0x0001;
    private const int UoiName = 2;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint OpenInputDesktop(uint flags, bool inherit, uint desiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(nint desktop);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetUserObjectInformation(nint handle, int index, StringBuilder? info, int length, out int needed);

    public ValueTask<bool> IsInteractiveAndUnlockedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(IsInputOnDefaultDesktop());
    }

    public static bool IsInputOnDefaultDesktop()
    {
        var desktop = OpenInputDesktop(0, false, DesktopReadObjects);
        if (desktop == 0)
            return false;
        try
        {
            var name = new StringBuilder(64);
            if (!GetUserObjectInformation(desktop, UoiName, name, name.Capacity * 2, out _))
                return false;
            return string.Equals(name.ToString(), "Default", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            _ = CloseDesktop(desktop);
        }
    }
}
