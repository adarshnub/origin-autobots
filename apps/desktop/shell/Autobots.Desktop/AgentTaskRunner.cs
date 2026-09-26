using System.Diagnostics;
using System.Globalization;
using Autobots.Contracts;
using Autobots.Core;
using Autobots.Platform;

namespace Autobots.Desktop;

public enum AgentPhase
{
    Starting,
    Observing,
    Thinking,
    Acting,
    Settling
}

public enum AgentOutcome
{
    Completed,
    NeedsInput,
    Stopped,
    StepLimit,
    TimeLimit,
    Handoff,
    Uncertain,
    Failed
}

public enum TimelineTone
{
    Neutral,
    Success,
    Warning,
    Error
}

public sealed record AgentRunOptions(int MaxSteps, TimeSpan MaxDuration, bool ShowPointer);

public sealed record AgentProgress(AgentPhase Phase, int Step, int MaxSteps, string Title, string Detail, string? AppName);

public sealed record AgentTimelineEntry(int Step, string Glyph, string Title, string Detail, TimelineTone Tone, DateTimeOffset At);

public sealed record AgentRunResult(AgentOutcome Outcome, string Message, int Steps, decimal CostUsd, TimeSpan Duration);

/// <summary>
/// Runs one owner-submitted task: observe the screen, ask the service for one proposal, validate it with
/// the local supervisor, perform it visibly, then observe again. The submitted task is the grant; the
/// model proposal never authorizes itself.
/// </summary>
public sealed class AgentTaskRunner(
    IDesktopAutomation desktop,
    IAgentPointerOverlay? pointerOverlay,
    LocalTaskSupervisor supervisor,
    AutobotsApiClient api,
    CognitoOAuthLogin login)
{
    private const int MaxConsecutiveRejections = 3;
    private readonly object _gate = new();
    private CancellationTokenSource? _run;
    private volatile bool _captureAuthorized;

    public event Action<AgentProgress>? Progress;
    public event Action<AgentTimelineEntry>? Timeline;

    public bool IsRunning
    {
        get { lock (_gate) return _run is not null; }
    }

    /// <summary>Capture is allowed only while an owner-submitted task holds the local lease.</summary>
    public bool IsCaptureAuthorized => _captureAuthorized && IsRunning;

    /// <summary>Local STOP: revokes the lease, cancels pending work and releases held input. No network needed.</summary>
    public void RequestStop()
    {
        CancellationTokenSource? run;
        lock (_gate)
            run = _run;
        _captureAuthorized = false;
        supervisor.Stop();
        try
        {
            run?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The run already finished.
        }
        _ = desktop.StopInputAsync(CancellationToken.None);
        pointerOverlay?.Hide();
    }

    public async Task<AgentRunResult> RunAsync(string instruction, AgentRunOptions options, CancellationToken cancellationToken)
    {
        using var run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_gate)
        {
            if (_run is not null)
                throw new InvalidOperationException("A task is already running. Stop it before starting another.");
            _run = run;
        }

        var token = run.Token;
        var clock = Stopwatch.StartNew();
        var executed = 0;
        var cost = 0m;
        Guid? taskId = null;
        using var runLog = new AgentRunLog(Environment.GetEnvironmentVariable("AUTOBOTS_TASK_LOG_DIRECTORY"));
        var stage = "starting";
        runLog.Write("started", new { options.MaxSteps, maxSeconds = options.MaxDuration.TotalSeconds });
        void RecordPointerTarget(PointerTargetDiagnostic diagnostic) => runLog.Write("pointer_target", diagnostic);
        desktop.PointerTargetObserved += RecordPointerTarget;
        try
        {
            Report(AgentPhase.Starting, 0, options.MaxSteps, "Getting ready", "Connecting to your Autobots service…", null);
            var accessToken = await login.GetAccessTokenAsync(token).ConfigureAwait(false);
            var deviceId = LocalDeviceIdentity.GetOrCreate();
            await api.EnrollDeviceAsync(accessToken, deviceId, token).ConfigureAwait(false);
            var task = await api.CreateTaskAsync(accessToken, deviceId, instruction, Guid.NewGuid().ToString("N"), token).ConfigureAwait(false);
            taskId = task.TaskId;
            var lease = supervisor.StartTask(task.TaskId, deviceId, ownerSubmittedTask: true);
            _captureAuthorized = true;
            if (options.ShowPointer)
            {
                pointerOverlay?.SetCaption("Autobots is taking over the pointer");
                pointerOverlay?.SetActivity(PointerActivity.Thinking);
                pointerOverlay?.Show();
            }

            var history = new List<StepHistoryEntry>();
            var repeatedClicks = new RepeatedClickGuard();
            var consecutiveRejections = 0;
            var settle = (Minimum: TimeSpan.FromMilliseconds(450), Maximum: TimeSpan.FromMilliseconds(1500));
            for (long sequence = 1; ; sequence++)
            {
                token.ThrowIfCancellationRequested();
                if (executed >= options.MaxSteps)
                    return Finish(AgentOutcome.StepLimit, $"Paused after {executed} steps, your per-task limit. Check the screen and start again to continue.");
                if (clock.Elapsed >= options.MaxDuration)
                    return Finish(AgentOutcome.TimeLimit, $"Paused at your {options.MaxDuration.TotalMinutes:0}-minute task limit. Check the screen and start again to continue.");
                if (sequence > options.MaxSteps + 12)
                    return Finish(AgentOutcome.Failed, "Autobots made too many attempts without progress, so it stopped. Try describing the task differently.");

                Report(AgentPhase.Settling, executed + 1, options.MaxSteps, "Waiting for the screen", "Letting the app finish updating…", null);
                stage = "settling";
                await desktop.WaitForVisualSettleAsync(settle.Minimum, settle.Maximum, token).ConfigureAwait(false);

                Report(AgentPhase.Observing, executed + 1, options.MaxSteps, "Looking at your screen", "Taking a fresh screenshot…", null);
                pointerOverlay?.SetActivity(PointerActivity.Thinking);
                pointerOverlay?.SetCaption("Looking at the screen…");
                stage = "observing";
                var frame = await desktop.CapturePrimaryDisplayAsync(token).ConfigureAwait(false);
                if (!Guid.TryParse(frame.ObservationId, out var observationId) || !supervisor.TrySetObservation(lease, observationId))
                    throw new InvalidOperationException("The local supervisor could not accept this screen observation.");
                var appName = AppLabel(frame);

                Report(AgentPhase.Thinking, executed + 1, options.MaxSteps, "Deciding the next step", appName is null ? "Reading the screen" : $"In {appName}", appName);
                pointerOverlay?.SetCaption("Deciding the next step…");
                accessToken = await login.GetAccessTokenAsync(token).ConfigureAwait(false);
                AutobotsApiClient.ActionProposalResponse result;
                stage = "proposing";
                runLog.Write("observation", new { sequence, frame.ImageWidth, frame.ImageHeight, app = frame.ForegroundProcessName });
                try
                {
                    result = await api.ProposeActionAsync(
                        accessToken, task.TaskId, deviceId, observationId, lease.LeaseId, lease.Epoch, sequence,
                        frame.ImageMimeType, frame.ImageBytes, history, frame.ForegroundWindowTitle, token).ConfigureAwait(false);
                }
                catch (AutobotsApiException error) when (error.Code == "proposal_unavailable")
                {
                    runLog.Write("rejected", new { sequence, stage, reason = error.Reason });
                    if (++consecutiveRejections > MaxConsecutiveRejections)
                        return Finish(AgentOutcome.Failed, "The AI kept suggesting actions Autobots can't perform, so it stopped. Try rephrasing the task.");
                    history.Add(new StepHistoryEntry(sequence, "unusable suggestion", "rejected", error.Reason ?? "not a supported desktop action"));
                    Emit(executed + 1, Icons.Warning, "Skipped an unusable suggestion", error.Reason ?? "Asking again with a fresh screenshot.", TimelineTone.Warning);
                    settle = (TimeSpan.Zero, TimeSpan.FromMilliseconds(300));
                    continue;
                }
                token.ThrowIfCancellationRequested();
                if (result.Execution != "not_executed")
                    throw new InvalidOperationException("The service returned an unexpected execution state, so Autobots stopped.");
                if (decimal.TryParse(result.ActualCostUsd, NumberStyles.Number, CultureInfo.InvariantCulture, out var stepCost))
                    cost += stepCost;
                runLog.Write("proposal", new { sequence, result.Status, action = result.Proposal?.Action.GetType().Name,
                    key = (result.Proposal?.Action as KeyPressAction)?.Key,
                    x = (result.Proposal?.Action as ClickAction)?.X, y = (result.Proposal?.Action as ClickAction)?.Y,
                    textLength = (result.Proposal?.Action as TypeTextAction)?.Text.Length, costUsd = stepCost });

                if (result.Status is "completed" or "needs_input" && result.Proposal is null)
                {
                    var completed = result.Status == "completed";
                    return Finish(
                        completed ? AgentOutcome.Completed : AgentOutcome.NeedsInput,
                        result.CompletionMessage ?? (completed ? "The task looks complete. Check the result on screen." : "Autobots needs more detail to continue."));
                }
                if (result.Status != "proposal" || result.Proposal is null)
                    throw new InvalidOperationException("The service returned an unsupported task step.");

                var action = result.Proposal.Action;
                if (!repeatedClicks.CanExecute(action, frame.ImageBytes, frame.ForegroundProcessId))
                {
                    const string reason = "This same click was already executed twice on an identical screen without visible progress. No click sent. Switch to the real target app or use a different approach; do not click controls inside a screenshot.";
                    runLog.Write("rejected", new { sequence, stage = "progress-check", reason });
                    if (++consecutiveRejections > MaxConsecutiveRejections)
                        return Finish(AgentOutcome.NeedsInput, "Repeated clicks are not making visible progress. Check the active app before starting again.");
                    history.Add(new StepHistoryEntry(sequence, ActionNarration.ForHistory(action, result.Intent), "rejected", reason));
                    Emit(executed + 1, Icons.Shield, "No visible progress", reason, TimelineTone.Warning);
                    settle = (TimeSpan.Zero, TimeSpan.FromMilliseconds(300));
                    continue;
                }
                stage = "authorizing";
                if (!supervisor.TryAuthorizeTaskAction(lease, result.Proposal, frame, DateTimeOffset.UtcNow, out var authorization, out var rejection) || authorization is null)
                {
                    runLog.Write("rejected", new { sequence, stage, reason = rejection });
                    if (++consecutiveRejections > MaxConsecutiveRejections)
                        return Finish(AgentOutcome.Failed, $"The local supervisor rejected repeated actions: {rejection}");
                    history.Add(new StepHistoryEntry(sequence, ActionNarration.ForHistory(action, result.Intent), "rejected", rejection));
                    Emit(executed + 1, Icons.Shield, "Blocked a step", rejection, TimelineTone.Warning);
                    settle = (TimeSpan.Zero, TimeSpan.FromMilliseconds(300));
                    continue;
                }

                var headline = ActionNarration.Headline(action);
                var detail = result.Intent ?? (appName is null ? string.Empty : $"In {appName}");
                Report(AgentPhase.Acting, executed + 1, options.MaxSteps, headline, detail, appName);
                pointerOverlay?.SetActivity(ActionNarration.Activity(action));
                pointerOverlay?.SetCaption(result.Intent ?? headline);
                try
                {
                    stage = "executing";
                    await desktop.ExecuteAuthorizedActionAsync(authorization, frame, token).ConfigureAwait(false);
                }
                catch (ActionNotDispatchedException error)
                {
                    runLog.Write("rejected", new { sequence, stage, reason = error.Message });
                    if (++consecutiveRejections > MaxConsecutiveRejections)
                        return Finish(AgentOutcome.Failed, error.Message);
                    history.Add(new StepHistoryEntry(sequence, ActionNarration.ForHistory(action, result.Intent), "rejected", ActionNarration.Clip(error.Message, 280)));
                    Emit(executed + 1, Icons.Shield, "Skipped a step", error.Message, TimelineTone.Warning);
                    settle = (TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(600));
                    continue;
                }

                executed++;
                repeatedClicks.RecordExecuted(action, frame.ImageBytes, frame.ForegroundProcessId);
                runLog.Write("executed", new { sequence, step = executed, action = action.GetType().Name });
                consecutiveRejections = 0;
                history.Add(new StepHistoryEntry(sequence, ActionNarration.ForHistory(action, result.Intent), "executed", null));
                Emit(executed, ActionNarration.Glyph(action), ActionNarration.PastTense(action), detail, TimelineTone.Neutral);
                settle = SettleWindow(action);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return Finish(AgentOutcome.Stopped, "Stopped. Nothing further will be sent. Steps already completed can't be undone.");
        }
        catch (DesktopHandoffRequiredException error)
        {
            return Finish(AgentOutcome.Handoff, error.Message);
        }
        catch (ActionInterruptedException error)
        {
            return Finish(AgentOutcome.Uncertain, error.Message);
        }
        catch (Exception error) when (error is InvalidOperationException or UnauthorizedAccessException or HttpRequestException or TimeoutException or NotSupportedException or OperationCanceledException or IOException)
        {
            return Finish(AgentOutcome.Failed, error is OperationCanceledException ? "The Autobots service took too long to respond, so the task stopped." : error.Message);
        }
        finally
        {
            desktop.PointerTargetObserved -= RecordPointerTarget;
            _captureAuthorized = false;
            supervisor.Stop();
            try
            {
                await desktop.StopInputAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception error) when (error is InvalidOperationException or NotSupportedException)
            {
                // Held input was already released by the failing action.
            }
            pointerOverlay?.SetActivity(PointerActivity.Idle);
            pointerOverlay?.Hide();
            if (taskId is { } id)
                await StopCloudTaskAsync(id).ConfigureAwait(false);
            lock (_gate)
                _run = null;
        }

        AgentRunResult Finish(AgentOutcome outcome, string message)
        {
            runLog.Write("finished", new { outcome = outcome.ToString(), stage, steps = executed, costUsd = cost, seconds = clock.Elapsed.TotalSeconds, message });
            var tone = outcome switch
            {
                AgentOutcome.Completed => TimelineTone.Success,
                AgentOutcome.NeedsInput or AgentOutcome.StepLimit or AgentOutcome.TimeLimit or AgentOutcome.Handoff or AgentOutcome.Stopped => TimelineTone.Warning,
                _ => TimelineTone.Error
            };
            var glyph = outcome == AgentOutcome.Completed ? Icons.Check : outcome == AgentOutcome.Stopped ? Icons.Stop : Icons.Warning;
            Emit(executed, glyph, OutcomeTitle(outcome), message, tone);
            return new AgentRunResult(outcome, message, executed, cost, clock.Elapsed);
        }
    }

    public static string OutcomeTitle(AgentOutcome outcome) => outcome switch
    {
        AgentOutcome.Completed => "Task complete",
        AgentOutcome.NeedsInput => "Needs your input",
        AgentOutcome.Stopped => "Stopped",
        AgentOutcome.StepLimit => "Step limit reached",
        AgentOutcome.TimeLimit => "Time limit reached",
        AgentOutcome.Handoff => "Your turn",
        AgentOutcome.Uncertain => "Stopped mid-action",
        _ => "Task stopped"
    };

    private static (TimeSpan Minimum, TimeSpan Maximum) SettleWindow(ProposedAction action) => action switch
    {
        ClickAction => (TimeSpan.FromMilliseconds(350), TimeSpan.FromMilliseconds(1800)),
        KeyPressAction => (TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(1800)),
        TypeTextAction { PressEnter: true } => (TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(2500)),
        TypeTextAction => (TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(600)),
        ScrollAction => (TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(900)),
        DragAction => (TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(1200)),
        MoveAction => (TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(700)),
        _ => (TimeSpan.Zero, TimeSpan.FromMilliseconds(300))
    };

    private static string? AppLabel(CapturedFrame frame)
    {
        if (!string.IsNullOrWhiteSpace(frame.ForegroundProcessName) && frame.ForegroundProcessName is not ("Application Frame Host" or "ApplicationFrameHost"))
            return ActionNarration.Clip(frame.ForegroundProcessName, 40);
        return string.IsNullOrWhiteSpace(frame.ForegroundWindowTitle) ? null : ActionNarration.Clip(frame.ForegroundWindowTitle, 40);
    }

    private async Task StopCloudTaskAsync(Guid taskId)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var accessToken = await login.GetAccessTokenAsync(timeout.Token).ConfigureAwait(false);
            await api.StopTaskAsync(accessToken, taskId, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is InvalidOperationException or HttpRequestException or OperationCanceledException)
        {
            // Local STOP is authoritative; the service also rejects late steps for a stopped epoch.
        }
    }

    private void Report(AgentPhase phase, int step, int maxSteps, string title, string detail, string? appName) =>
        Progress?.Invoke(new AgentProgress(phase, step, maxSteps, title, detail, appName));

    private void Emit(int step, string glyph, string title, string detail, TimelineTone tone) =>
        Timeline?.Invoke(new AgentTimelineEntry(step, glyph, title, detail, tone, DateTimeOffset.Now));
}
