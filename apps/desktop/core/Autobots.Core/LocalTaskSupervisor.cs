using Autobots.Contracts;
using Autobots.Platform;

namespace Autobots.Core;

public sealed record TaskLease(Guid TaskId, Guid DeviceId, Guid LeaseId, long Epoch, CancellationToken CancellationToken);

public sealed class LocalTaskSupervisor : IDisposable
{
    /// <summary>
    /// How long a one-use action capability stays valid. It covers visible, paced pointer motion and
    /// typing; the executor still re-checks focus, layout and cancellation before every input batch.
    /// </summary>
    public static readonly TimeSpan ActionCapabilityLifetime = TimeSpan.FromSeconds(15);

    private readonly object _gate = new();
    private readonly HashSet<Guid> _actionIds = [];
    private CancellationTokenSource? _activeCancellation;
    private TaskLease? _activeLease;
    private Guid? _currentObservationId;
    private long _epoch;
    private long _lastSequence;
    private bool _disposed;

    public long Epoch
    {
        get { lock (_gate) return _epoch; }
    }

    public TaskLease StartTask(Guid taskId, Guid deviceId, bool ownerSubmittedTask)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!ownerSubmittedTask)
            throw new UnauthorizedAccessException("A task cannot start without an owner-submitted instruction.");
        if (taskId == Guid.Empty)
            throw new ArgumentException("Task ID must be set.", nameof(taskId));
        if (deviceId == Guid.Empty)
            throw new ArgumentException("Device ID must be set.", nameof(deviceId));

        lock (_gate)
        {
            if (_activeLease is not null)
                throw new InvalidOperationException("Another task owns the local input lease.");

            _activeCancellation = new CancellationTokenSource();
            var lease = new TaskLease(taskId, deviceId, Guid.NewGuid(), _epoch, _activeCancellation.Token);
            _activeLease = lease;
            _currentObservationId = null;
            _lastSequence = 0;
            _actionIds.Clear();
            return lease;
        }
    }

    public bool TrySetObservation(TaskLease lease, Guid observationId)
    {
        if (observationId == Guid.Empty)
            return false;

        lock (_gate)
        {
            if (!LeaseIsCurrent(lease))
                return false;
            _currentObservationId = observationId;
            return true;
        }
    }

    public bool TryValidateProposal(TaskLease lease, ActionEnvelope envelope, CapturedFrame observation, out string rejectionReason)
    {
        lock (_gate)
        {
            return ValidateEnvelope(lease, envelope, observation, out rejectionReason);
        }
    }

    /// <summary>
    /// Issues a one-use native-input capability within the task grant created when the
    /// owner submits an instruction. The action ID and sequence are consumed before
    /// control reaches any platform input API.
    /// </summary>
    public bool TryAuthorizeTaskAction(
        TaskLease lease,
        ActionEnvelope envelope,
        CapturedFrame observation,
        DateTimeOffset authorizedAt,
        out AuthorizedAction? authorizedAction,
        out string rejectionReason)
    {
        lock (_gate)
        {
            authorizedAction = null;
            if (!ValidateEnvelope(lease, envelope, observation, out rejectionReason))
                return false;
            if (observation.CapturedAt > authorizedAt.AddSeconds(5) || authorizedAt - observation.CapturedAt > TimeSpan.FromMinutes(2))
                return Reject("The screen observation is too old to act on. Capture a fresh screen and request a new proposal.", out rejectionReason);
            if (observation.ForegroundWindowHandle == 0 || observation.ForegroundProcessId is null)
                return Reject("The proposal is not tied to a foreground application window.", out rejectionReason);

            _lastSequence = envelope.Sequence;
            _actionIds.Add(envelope.ActionId);
            authorizedAction = new AuthorizedAction(envelope, observation, authorizedAt, authorizedAt.Add(ActionCapabilityLifetime));
            rejectionReason = string.Empty;
            return true;
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            _epoch++;
            cancellation = _activeCancellation;
            _activeCancellation = null;
            _activeLease = null;
            _currentObservationId = null;
            _actionIds.Clear();
        }

        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Stop();
    }

    private bool LeaseIsCurrent(TaskLease lease) =>
        _activeLease is { } current &&
        current.TaskId == lease.TaskId &&
        current.LeaseId == lease.LeaseId &&
        current.Epoch == lease.Epoch &&
        lease.Epoch == _epoch &&
        !lease.CancellationToken.IsCancellationRequested;

    private bool ValidateEnvelope(TaskLease lease, ActionEnvelope envelope, CapturedFrame observation, out string rejectionReason)
    {
        if (!LeaseIsCurrent(lease))
            return Reject("The task lease is no longer active.", out rejectionReason);
        if (envelope.SchemaVersion != 1 || envelope.TaskId != lease.TaskId || envelope.DeviceId != lease.DeviceId || envelope.ActionId == Guid.Empty || envelope.ObservationId == Guid.Empty || envelope.LeaseId != lease.LeaseId || envelope.Epoch != lease.Epoch || envelope.Sequence < 1)
            return Reject("Task, lease, epoch, or schema does not match.", out rejectionReason);
        if (!Guid.TryParse(observation.ObservationId, out var observationId) || envelope.ObservationId != observationId || envelope.ObservationId != _currentObservationId)
            return Reject("The action was proposed from a stale or mismatched observation.", out rejectionReason);
        if (envelope.Sequence <= _lastSequence || _actionIds.Contains(envelope.ActionId))
            return Reject("The action sequence or ID has already been used.", out rejectionReason);
        if (!ActionIsValid(envelope.Action))
            return Reject("The action payload is invalid.", out rejectionReason);
        if (observation.PixelWidth <= 0 || observation.PixelHeight <= 0 || observation.LayoutGeneration <= 0 ||
            observation.ImageWidth <= 0 || observation.ImageHeight <= 0 ||
            observation.VirtualDesktopWidth <= 0 || observation.VirtualDesktopHeight <= 0 ||
            !double.IsFinite(observation.DpiX) || !double.IsFinite(observation.DpiY) || observation.DpiX <= 0 || observation.DpiY <= 0)
            return Reject("The screen observation has invalid display metadata.", out rejectionReason);
        if (observation.DesktopOriginX < observation.VirtualDesktopOriginX || observation.DesktopOriginY < observation.VirtualDesktopOriginY ||
            (long)observation.DesktopOriginX + observation.PixelWidth > (long)observation.VirtualDesktopOriginX + observation.VirtualDesktopWidth ||
            (long)observation.DesktopOriginY + observation.PixelHeight > (long)observation.VirtualDesktopOriginY + observation.VirtualDesktopHeight)
            return Reject("The primary display is outside the reported virtual desktop bounds.", out rejectionReason);

        rejectionReason = string.Empty;
        return true;
    }

    private static bool Reject(string reason, out string rejectionReason)
    {
        rejectionReason = reason;
        return false;
    }

    private static bool ActionIsValid(ProposedAction action) => action switch
    {
        ClickAction click => IsPoint(click.X, click.Y) && Enum.IsDefined(click.Button) && click.Clicks is >= 1 and <= 3,
        TypeTextAction text => text.Text.Length is > 0 and <= 4000,
        KeyPressAction key => key.Key.Length is > 0 and <= 64,
        ScrollAction scroll => IsPoint(scroll.X, scroll.Y) && scroll.DeltaX is >= -1200 and <= 1200 && scroll.DeltaY is >= -1200 and <= 1200,
        WaitAction wait => wait.DurationMs is >= 0 and <= 5000,
        MoveAction move => IsPoint(move.X, move.Y),
        DragAction drag => IsPoint(drag.X, drag.Y) && IsPoint(drag.ToX, drag.ToY),
        _ => false
    };

    private static bool IsPoint(int x, int y) => x is >= 0 and <= 999 && y is >= 0 and <= 999;
}
