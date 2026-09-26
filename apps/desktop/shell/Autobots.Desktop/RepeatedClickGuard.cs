using System.Security.Cryptography;
using Autobots.Contracts;

namespace Autobots.Desktop;

/// <summary>Best-effort stall detection. Stores hashes, never screenshot bytes.</summary>
public sealed class RepeatedClickGuard
{
    private readonly Queue<string> _executed = new();

    public bool CanExecute(ProposedAction action, ReadOnlySpan<byte> image, int? foregroundProcessId)
    {
        if (action is not ClickAction click) return true;
        var key = Key(click, image, foregroundProcessId);
        return _executed.Count(item => item == key) < 2;
    }

    public void RecordExecuted(ProposedAction action, ReadOnlySpan<byte> image, int? foregroundProcessId)
    {
        if (action is not ClickAction click) return;
        _executed.Enqueue(Key(click, image, foregroundProcessId));
        while (_executed.Count > 12) _executed.Dequeue();
    }

    private static string Key(ClickAction click, ReadOnlySpan<byte> image, int? processId) =>
        $"{processId}:{click.X}:{click.Y}:{click.Button}:{click.Clicks}:{Convert.ToHexString(SHA256.HashData(image))}";
}
