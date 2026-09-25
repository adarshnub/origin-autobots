using Autobots.Contracts;
using Autobots.Core;
using Autobots.Platform;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Autobots.Core.Tests;

public sealed class SupervisorTests
{
    private static readonly Guid DeviceId = Guid.NewGuid();

    [Fact]
    public void TaskCannotStartWithoutAnExistingGrant()
    {
        using var supervisor = new LocalTaskSupervisor();
        Assert.Throws<UnauthorizedAccessException>(() => supervisor.StartTask(Guid.NewGuid(), DeviceId, ownerSubmittedTask: false));
    }

    [Fact]
    public void StopInvalidatesTheLeaseAndCancelsItsToken()
    {
        using var supervisor = new LocalTaskSupervisor();
        var lease = supervisor.StartTask(Guid.NewGuid(), DeviceId, ownerSubmittedTask: true);

        supervisor.Stop();

        Assert.True(lease.CancellationToken.IsCancellationRequested);
        Assert.Equal(lease.Epoch + 1, supervisor.Epoch);
    }

    [Fact]
    public void DuplicateAndStaleActionsAreRejected()
    {
        using var supervisor = new LocalTaskSupervisor();
        var lease = supervisor.StartTask(Guid.NewGuid(), DeviceId, ownerSubmittedTask: true);
        var observationId = Guid.NewGuid();
        Assert.True(supervisor.TrySetObservation(lease, observationId));
        var envelope = NewClick(lease, observationId, sequence: 1);
        var frame = NewFrame(observationId);

        Assert.True(supervisor.TryValidateProposal(lease, envelope, frame, out var reason), reason);
        Assert.True(supervisor.TryAuthorizeTaskAction(lease, envelope, frame, DateTimeOffset.UtcNow, out var authorized, out reason), reason);
        Assert.NotNull(authorized);
        Assert.False(supervisor.TryValidateProposal(lease, envelope, frame, out _));
        supervisor.Stop();
        Assert.False(supervisor.TryValidateProposal(lease, NewClick(lease, observationId, sequence: 2), frame, out _));
    }

    [Fact]
    public void StaleObservationCannotBeAuthorizedByTheTaskGrant()
    {
        using var supervisor = new LocalTaskSupervisor();
        var lease = supervisor.StartTask(Guid.NewGuid(), DeviceId, ownerSubmittedTask: true);
        var observationId = Guid.NewGuid();
        Assert.True(supervisor.TrySetObservation(lease, observationId));
        var envelope = NewClick(lease, observationId, sequence: 1);
        var staleFrame = NewFrame(observationId) with { CapturedAt = DateTimeOffset.UtcNow.AddMinutes(-3) };

        Assert.False(supervisor.TryAuthorizeTaskAction(lease, envelope, staleFrame, DateTimeOffset.UtcNow, out var authorized, out var reason));
        Assert.Null(authorized);
        Assert.Contains("too old", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OnlyOneTaskCanOwnTheLease()
    {
        using var supervisor = new LocalTaskSupervisor();
        supervisor.StartTask(Guid.NewGuid(), DeviceId, ownerSubmittedTask: true);
        Assert.Throws<InvalidOperationException>(() => supervisor.StartTask(Guid.NewGuid(), DeviceId, ownerSubmittedTask: true));
    }

    [Fact]
    public void CoordinateTransformKeepsDisplayOriginAndCorners()
    {
        Assert.Equal((-1920, 0), DisplayCoordinateTransform.ToDesktopPixels(0, 0, 1920, 1080, -1920, 0));
        Assert.Equal((-1, 1079), DisplayCoordinateTransform.ToDesktopPixels(999, 999, 1920, 1080, -1920, 0));
    }

    [Fact]
    public void CSharpContractReadsTheSharedJsonFixture()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "action-click.v1.json"));
        var envelope = JsonSerializer.Deserialize<ActionEnvelope>(json);

        Assert.NotNull(envelope);
        Assert.Equal(1, envelope.SchemaVersion);
        Assert.IsType<ClickAction>(envelope.Action);
        Assert.Equal(PointerButton.Left, ((ClickAction)envelope.Action).Button);
    }

    private static ActionEnvelope NewClick(TaskLease lease, Guid observationId, long sequence) => new(
        SchemaVersion: 1,
        TaskId: lease.TaskId,
        DeviceId: lease.DeviceId,
        ActionId: Guid.NewGuid(),
        ObservationId: observationId,
        LeaseId: lease.LeaseId,
        Epoch: lease.Epoch,
        Sequence: sequence,
        Action: new ClickAction(250, 250, PointerButton.Left));

    private static CapturedFrame NewFrame(Guid observationId) => new(
        ObservationId: observationId.ToString("D"),
        CapturedAt: DateTimeOffset.UtcNow,
        MonitorId: "primary",
        PixelWidth: 1920,
        PixelHeight: 1080,
        DesktopOriginX: 0,
        DesktopOriginY: 0,
        VirtualDesktopOriginX: 0,
        VirtualDesktopOriginY: 0,
        VirtualDesktopWidth: 1920,
        VirtualDesktopHeight: 1080,
        DpiX: 96,
        DpiY: 96,
        ForegroundWindowHandle: (nint)123,
        ForegroundProcessId: 456,
        ForegroundWindowTitle: "Test application",
        LayoutGeneration: 1,
        PngBytes: [1, 2, 3]);
}
