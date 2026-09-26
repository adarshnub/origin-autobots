namespace Autobots.Desktop;

/// <summary>One explicit owner continuation for an active, paused task.</summary>
public sealed class OwnerResumeGate
{
    private readonly object _gate = new();
    private TaskCompletionSource<bool>? _pending;

    public bool IsPending
    {
        get { lock (_gate) return _pending is not null; }
    }

    public bool Continue()
    {
        lock (_gate)
            return _pending?.TrySetResult(true) == true;
    }

    public async Task<bool> WaitAsync(TimeSpan remaining, CancellationToken cancellationToken)
    {
        if (remaining <= TimeSpan.Zero) return false;
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_pending is not null)
                throw new InvalidOperationException("The task is already waiting for the owner.");
            _pending = pending;
        }
        try
        {
            return await pending.Task.WaitAsync(remaining, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return false;
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_pending, pending)) _pending = null;
            }
        }
    }
}
