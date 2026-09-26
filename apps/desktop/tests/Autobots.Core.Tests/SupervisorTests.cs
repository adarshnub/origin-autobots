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
    public void ExtendedDesktopActionsAreValidatedBeforeAuthorization()
    {
        using var supervisor = new LocalTaskSupervisor();
        var lease = supervisor.StartTask(Guid.NewGuid(), DeviceId, ownerSubmittedTask: true);
        var observationId = Guid.NewGuid();
        Assert.True(supervisor.TrySetObservation(lease, observationId));
        var frame = NewFrame(observationId);

        ProposedAction[] valid =
        [
            new ClickAction(10, 20, PointerButton.Left, Clicks: 2),
            new MoveAction(999, 0),
            new DragAction(1, 2, 998, 997),
            new TypeTextAction("hello", PressEnter: true),
            new KeyPressAction("Control+Shift+ArrowLeft")
        ];
        var sequence = 1;
        foreach (var action in valid)
        {
            var envelope = NewEnvelope(lease, observationId, sequence++, action);
            Assert.True(supervisor.TryAuthorizeTaskAction(lease, envelope, frame, DateTimeOffset.UtcNow, out var authorized, out var reason), reason);
            Assert.Equal(authorized!.AuthorizedAt + LocalTaskSupervisor.ActionCapabilityLifetime, authorized.ExpiresAt);
        }

        ProposedAction[] invalid =
        [
            new ClickAction(10, 20, PointerButton.Left, Clicks: 4),
            new MoveAction(1000, 0),
            new DragAction(1, 2, 3, -1),
            new TypeTextAction(new string('x', 4001)),
            new KeyPressAction(new string('k', 65))
        ];
        foreach (var action in invalid)
            Assert.False(supervisor.TryValidateProposal(lease, NewEnvelope(lease, observationId, sequence++, action), frame, out _));
    }

    [Fact]
    public void ObservationWithoutUploadedImageDimensionsIsRejected()
    {
        using var supervisor = new LocalTaskSupervisor();
        var lease = supervisor.StartTask(Guid.NewGuid(), DeviceId, ownerSubmittedTask: true);
        var observationId = Guid.NewGuid();
        Assert.True(supervisor.TrySetObservation(lease, observationId));
        var frame = NewFrame(observationId) with { ImageWidth = 0 };

        Assert.False(supervisor.TryValidateProposal(lease, NewClick(lease, observationId, sequence: 1), frame, out var reason));
        Assert.Contains("display metadata", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("action-drag.v1.json")]
    [InlineData("action-type-enter.v1.json")]
    public void CSharpContractReadsExtendedActionFixtures(string fixture)
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, fixture));
        var envelope = JsonSerializer.Deserialize<ActionEnvelope>(json);

        Assert.NotNull(envelope);
        Assert.True(envelope.Action is DragAction { ToX: 800 } or TypeTextAction { PressEnter: true });
    }

    [Fact]
    public void ClickWithoutCountDefaultsToSingleClick()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "action-click.v1.json"));
        var envelope = JsonSerializer.Deserialize<ActionEnvelope>(json);
        Assert.Equal(1, Assert.IsType<ClickAction>(envelope!.Action).Clicks);
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

    private static ActionEnvelope NewClick(TaskLease lease, Guid observationId, long sequence) =>
        NewEnvelope(lease, observationId, sequence, new ClickAction(250, 250, PointerButton.Left));

    private static ActionEnvelope NewEnvelope(TaskLease lease, Guid observationId, long sequence, ProposedAction action) => new(
        SchemaVersion: 1,
        TaskId: lease.TaskId,
        DeviceId: lease.DeviceId,
        ActionId: Guid.NewGuid(),
        ObservationId: observationId,
        LeaseId: lease.LeaseId,
        Epoch: lease.Epoch,
        Sequence: sequence,
        Action: action);

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
        ImageWidth: 1440,
        ImageHeight: 810,
        ImageMimeType: "image/jpeg",
        ImageBytes: [1, 2, 3]);
}
