using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Autobots.Platform.Windows;

/// <summary>
/// Reads Windows mandatory integrity levels and display names for processes that own target windows.
/// Autobots runs at normal integrity; Windows silently drops input to higher-integrity windows, so
/// Autobots reports those targets instead of pretending an action succeeded.
/// </summary>
internal static class WindowsProcessIntegrity
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenIntegrityLevel = 25;
    private const int ErrorAccessDenied = 5;
    private static readonly Lazy<int> CurrentRid = new(() => ReadIntegrityRid(Process.GetCurrentProcess().Handle) ?? 0x2000);
    private static readonly ConcurrentDictionary<int, (DateTime CheckedAt, bool Elevated)> Cache = new();
    private static readonly ConcurrentDictionary<string, string> NameCache = new(StringComparer.OrdinalIgnoreCase);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(nint process, int flags, StringBuilder name, ref int size);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(nint process, uint access, out nint token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(nint token, int informationClass, nint information, int length, out int returnLength);

    [DllImport("advapi32.dll")]
    private static extern nint GetSidSubAuthorityCount(nint sid);

    [DllImport("advapi32.dll")]
    private static extern nint GetSidSubAuthority(nint sid, uint index);

    public static bool IsAboveCurrentProcess(int processId)
    {
        if (processId <= 0 || processId == Environment.ProcessId)
            return false;
        if (Cache.TryGetValue(processId, out var cached) && DateTime.UtcNow - cached.CheckedAt < TimeSpan.FromSeconds(30))
            return cached.Elevated;

        var elevated = false;
        var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process != 0)
        {
            try
            {
                if (!OpenProcessToken(process, TokenQuery, out var token))
                {
                    // Medium-integrity callers cannot query an elevated process's token.
                    elevated = Marshal.GetLastWin32Error() == ErrorAccessDenied;
                }
                else
                {
                    try
                    {
                        elevated = ReadIntegrityRid(token, isToken: true) is { } rid && rid > CurrentRid.Value;
                    }
                    finally
                    {
                        _ = CloseHandle(token);
                    }
                }
            }
            finally
            {
                _ = CloseHandle(process);
            }
        }
        Cache[processId] = (DateTime.UtcNow, elevated);
        return elevated;
    }

    /// <summary>Returns a friendly application name such as "Google Chrome", or the process name.</summary>
    public static string FriendlyName(int processId)
    {
        try
        {
            var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
            if (process == 0)
                return string.Empty;
            string path;
            try
            {
                var buffer = new StringBuilder(1024);
                var size = buffer.Capacity;
                if (!QueryFullProcessImageName(process, 0, buffer, ref size))
                    return string.Empty;
                path = buffer.ToString(0, size);
            }
            finally
            {
                _ = CloseHandle(process);
            }
            return NameCache.GetOrAdd(path, static imagePath =>
            {
                try
                {
                    var description = FileVersionInfo.GetVersionInfo(imagePath).FileDescription?.Trim();
                    if (!string.IsNullOrWhiteSpace(description))
                        return description;
                }
                catch (FileNotFoundException)
                {
                    // Fall back to the executable name.
                }
                return Path.GetFileNameWithoutExtension(imagePath);
            });
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException)
        {
            return string.Empty;
        }
    }

    private static int? ReadIntegrityRid(nint processOrToken, bool isToken = false)
    {
        var token = processOrToken;
        if (!isToken && !OpenProcessToken(processOrToken, TokenQuery, out token))
            return null;
        try
        {
            _ = GetTokenInformation(token, TokenIntegrityLevel, 0, 0, out var length);
            if (length <= 0)
                return null;
            var buffer = Marshal.AllocHGlobal(length);
            try
            {
                if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, length, out _))
                    return null;
                var sid = Marshal.ReadIntPtr(buffer);
                var count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                return count == 0 ? null : Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            if (!isToken)
                _ = CloseHandle(token);
        }
    }
}
