using System.Text.Json;

namespace Autobots.Desktop;

/// <summary>
/// Optional local diagnostics. Enabled only with AUTOBOTS_TASK_LOG_DIRECTORY. Never writes screen
/// images, audio, access tokens, instructions or typed action payloads. A logging error cannot disable STOP.
/// </summary>
public sealed class AgentRunLog : IDisposable
{
    private readonly StreamWriter? _writer;
    private readonly object _gate = new();
    private bool _disposed;

    public AgentRunLog(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return;
        try
        {
            Directory.CreateDirectory(directory);
            var name = $"run-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfff}-{Guid.NewGuid():N}.jsonl";
            _writer = new StreamWriter(new FileStream(Path.Combine(directory, name), FileMode.CreateNew,
                FileAccess.Write, FileShare.Read), new System.Text.UTF8Encoding(false)) { AutoFlush = true };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Diagnostics are optional; the task and local STOP remain available.
        }
    }

    public void Write(string kind, object details)
    {
        lock (_gate)
        {
            if (_writer is null || _disposed)
                return;
            try
            {
                _writer.WriteLine(JsonSerializer.Serialize(new { at = DateTimeOffset.UtcNow, kind, details }));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ObjectDisposedException)
            {
                // A full disk or closed diagnostic file must not interfere with task cancellation.
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            try { _writer?.Dispose(); }
            catch (IOException) { }
        }
    }
}
