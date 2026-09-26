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
    Settling,
    WaitingForOwner
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
    private readonly OwnerResumeGate _ownerResume = new();
    private volatile bool _captureAuthorized;

    public event Action<AgentProgress>? Progress;
    public event Action<AgentTimelineEntry>? Timeline;

    public bool IsRunning
    {
        get { lock (_gate) return _run is not null; }
    }

    public bool IsWaitingForOwner => _ownerResume.IsPending && IsRunning;

    /// <summary>Continue the same grant and lease after the owner handles a visible prompt.</summary>
    public bool ContinueAfterOwnerAction() => IsRunning && _ownerResume.Continue();

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
        var history = new List<StepHistoryEntry>();
        var ownerHandoffs = 0;
        var settle = (Minimum: TimeSpan.FromMilliseconds(450), Maximum: TimeSpan.FromMilliseconds(1500));
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

            var repeatedClicks = new RepeatedClickGuard();
            var calendarFieldLoop = new CalendarFieldLoopGuard();
            var consecutiveRejections = 0;
            var consecutiveServiceFailures = 0;
            var requiresExistingCalendarEditor = instruction.Contains("already-open, unsaved Google Calendar Event details form", StringComparison.OrdinalIgnoreCase);
            var existingCalendarEditorSeen = false;
            var requiresFreshCalendarEvent = instruction.StartsWith("Fresh Google Calendar scheduling test.", StringComparison.OrdinalIgnoreCase);
            var freshCalendarHomeSeen = false;
            var initialCalendarRepairAttempted = false;
            var exactNotepadText = ExactNotepadText.FromOwnerInstruction(instruction);

            async Task<(int Actions, string? Error)> RepairCalendarFieldsAsync(long firstSequence, nint expectedWindow)
            {
                var timing = CalendarTaskTiming.FromOwnerInstruction(instruction);
                if (timing is null)
                    return (0, "The Calendar task has no unambiguous start and end values for the field adapter.");
                var adapterActions = 0;
                foreach (var fieldName in new[] { "Start date", "Start time", "End date", "End time" })
                {
                    token.ThrowIfCancellationRequested();
                    var observed = await desktop.CapturePrimaryDisplayAsync(token).ConfigureAwait(false);
                    var field = SelectCalendarField(observed.CalendarFields, fieldName);
                    if (observed.ForegroundWindowHandle != expectedWindow || field is null)
                        return (adapterActions, "The Calendar editor or its accessible date/time fields changed; no further adapter input was sent.");
                    if (timing.Matches(fieldName, field.Value))
                        continue;

                    var targetText = timing.TextFor(fieldName);
                    foreach (var part in new[] { "focus", "select", "type", "commit" })
                    {
                        if (executed >= options.MaxSteps || clock.Elapsed >= options.MaxDuration)
                            return (adapterActions, "The task limit ended before the Calendar fields were verified. Do not save this draft yet.");
                        token.ThrowIfCancellationRequested();
                        stage = "calendar_adapter_observing";
                        await desktop.WaitForVisualSettleAsync(TimeSpan.FromMilliseconds(120), TimeSpan.FromMilliseconds(550), token).ConfigureAwait(false);
                        observed = await desktop.CapturePrimaryDisplayAsync(token).ConfigureAwait(false);
                        if (observed.ForegroundWindowHandle != expectedWindow ||
                            !Guid.TryParse(observed.ObservationId, out var adapterObservationId) ||
                            !supervisor.TrySetObservation(lease, adapterObservationId))
                            return (adapterActions, "The active Calendar window changed during field entry; no further input was sent.");
                        field = SelectCalendarField(observed.CalendarFields, fieldName);
                        if (part == "focus" && field is null)
                            return (adapterActions, "The Calendar field is no longer accessible; no further input was sent.");
                        ProposedAction action = part switch
                        {
                            "focus" => new ClickAction(field!.X, field.Y, PointerButton.Left),
                            "select" => new KeyPressAction("Ctrl+A"),
                            "type" => new TypeTextAction(targetText),
                            _ => new KeyPressAction("Tab")
                        };
                        var actionSequence = firstSequence + adapterActions;
                        var envelope = new ActionEnvelope(1, task.TaskId, deviceId, Guid.NewGuid(), adapterObservationId,
                            lease.LeaseId, lease.Epoch, actionSequence, action);
                        stage = "calendar_adapter_authorizing";
                        if (!supervisor.TryAuthorizeTaskAction(lease, envelope, observed, DateTimeOffset.UtcNow, out var authorization, out var rejection) || authorization is null)
                            return (adapterActions, $"The local supervisor blocked Calendar field entry: {rejection}");
                        var label = $"Calendar adapter: {part} {fieldName.ToLowerInvariant()}";
                        Report(AgentPhase.Acting, executed + 1, options.MaxSteps, label, "Using the owner's requested date and time", "Google Calendar");
                        pointerOverlay?.SetActivity(ActionNarration.Activity(action));
                        pointerOverlay?.SetCaption(label);
                        stage = "calendar_adapter_executing";
                        try
                        {
                            await desktop.ExecuteAuthorizedActionAsync(authorization, observed, token).ConfigureAwait(false);
                        }
                        catch (ActionNotDispatchedException error)
                        {
                            return (adapterActions, $"Calendar field entry stopped: {error.Message}");
                        }
                        executed++;
                        adapterActions++;
                        runLog.Write("adapter_executed", new { sequence = actionSequence, step = executed, field = fieldName, part, action = action.GetType().Name });
                        history.Add(new StepHistoryEntry(actionSequence, label, "executed", null));
                        Emit(executed, ActionNarration.Glyph(action), label, "Entered through the local Calendar field adapter", TimelineTone.Neutral);
                    }

                    await desktop.WaitForVisualSettleAsync(TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(750), token).ConfigureAwait(false);
                    observed = await desktop.CapturePrimaryDisplayAsync(token).ConfigureAwait(false);
                    field = SelectCalendarField(observed.CalendarFields, fieldName);
                    if (observed.ForegroundWindowHandle != expectedWindow || field is null || !timing.Matches(fieldName, field.Value))
                        return (adapterActions, $"Calendar {fieldName.ToLowerInvariant()} did not match the owner's request after entry. No Save was sent.");
                    runLog.Write("adapter_field_verified", new { field = fieldName });
                }

                var finalFrame = await desktop.CapturePrimaryDisplayAsync(token).ConfigureAwait(false);
                if (finalFrame.ForegroundWindowHandle != expectedWindow || finalFrame.CalendarFields is not { } finalFields ||
                    !timing.Matches("Start date", finalFields.StartDate.Value) ||
                    !timing.Matches("Start time", finalFields.StartTime.Value) ||
                    !timing.Matches("End date", finalFields.EndDate.Value) ||
                    !timing.Matches("End time", finalFields.EndTime.Value))
                    return (adapterActions, "The four Calendar date/time values did not match after entry. No Save was sent.");
                runLog.Write("adapter_all_fields_verified", new { actions = adapterActions });
                return (adapterActions, null);
            }

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
                if (requiresFreshCalendarEvent &&
                    frame.ForegroundWindowTitle.Contains("Google Calendar", StringComparison.OrdinalIgnoreCase) &&
                    !frame.ForegroundWindowTitle.Contains("Create event", StringComparison.OrdinalIgnoreCase) &&
                    !frame.ForegroundWindowTitle.Contains("Edit event", StringComparison.OrdinalIgnoreCase) &&
                    !frame.ForegroundWindowTitle.Contains("Event details", StringComparison.OrdinalIgnoreCase))
                    freshCalendarHomeSeen = true;
                if (requiresExistingCalendarEditor && !existingCalendarEditorSeen)
                {
                    if (frame.CalendarFields is null)
                    {
                        runLog.Write("calendar_editor_handoff", new { sequence });
                        var paused = await PauseForOwnerAsync(
                            "Bring the existing unsaved Google Calendar Event details form to the front, then Resume. Autobots will not create another event.", sequence);
                        if (paused is { } outcome) return outcome;
                        continue;
                    }
                    existingCalendarEditorSeen = true;
                    runLog.Write("calendar_editor_found", new { sequence });
                }
                var appName = AppLabel(frame);

                if (!initialCalendarRepairAttempted &&
                    (requiresExistingCalendarEditor || (requiresFreshCalendarEvent && freshCalendarHomeSeen)) &&
                    frame.CalendarFields is not null && CalendarTaskTiming.FromOwnerInstruction(instruction) is not null)
                {
                    initialCalendarRepairAttempted = true;
                    runLog.Write("calendar_adapter_started", new { sequence, trigger = "first_editor_observation" });
                    var repair = await RepairCalendarFieldsAsync(sequence, frame.ForegroundWindowHandle);
                    if (repair.Error is not null)
                        return Finish(AgentOutcome.NeedsInput, repair.Error);
                    calendarFieldLoop.Reset();
                    consecutiveRejections = 0;
                    if (repair.Actions > 0)
                    {
                        sequence += repair.Actions - 1;
                        history.Add(new StepHistoryEntry(sequence, "Calendar adapter verified all four date/time fields", "verified",
                            "Proceed with the remaining task. Do not change the verified dates or times; check before Save."));
                        settle = (TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(700));
                        continue;
                    }
                }

                Report(AgentPhase.Thinking, executed + 1, options.MaxSteps, "Deciding the next step", appName is null ? "Reading the screen" : $"In {appName}", appName);
                pointerOverlay?.SetCaption("Deciding the next step…");
                accessToken = await login.GetAccessTokenAsync(token).ConfigureAwait(false);
                AutobotsApiClient.ActionProposalResponse result;
                stage = "proposing";
                runLog.Write("observation", new { sequence, frame.ImageWidth, frame.ImageHeight, app = frame.ForegroundProcessName,
                    calendarFieldsAvailable = frame.CalendarFields is not null });
                try
                {
                    result = await api.ProposeActionAsync(
                        accessToken, task.TaskId, deviceId, observationId, lease.LeaseId, lease.Epoch, sequence,
                        frame.ImageMimeType, frame.ImageBytes, history, frame.ForegroundWindowTitle, frame.CalendarFields, token).ConfigureAwait(false);
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
                catch (AutobotsApiException error) when (error.Code == "owner_confirmation_required")
                {
                    var paused = await PauseForOwnerAsync(error.Reason ?? "The AI service requires your action in the visible app. Perform it yourself, then choose Resume. Resume does not execute the paused action.", sequence);
                    if (paused is { } outcome) return outcome;
                    continue;
                }
                catch (AutobotsApiException error) when (error.StatusCode >= 500)
                {
                    consecutiveServiceFailures++;
                    runLog.Write("service_retry", new { sequence, error.StatusCode, consecutiveServiceFailures });
                    if (consecutiveServiceFailures > 2)
                        return Finish(AgentOutcome.Failed, "The AI service is still unavailable after three attempts. No action was sent for those failed requests.");
                    history.Add(new StepHistoryEntry(sequence, "AI service temporarily unavailable", "rejected",
                        "No desktop action was sent. Observe the screen again before continuing."));
                    Report(AgentPhase.Settling, executed + 1, options.MaxSteps, "Service retry", "Taking a fresh look before asking again…", appName);
                    await Task.Delay(TimeSpan.FromSeconds(consecutiveServiceFailures), token).ConfigureAwait(false);
                    continue;
                }
                token.ThrowIfCancellationRequested();
                consecutiveServiceFailures = 0;
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
                    if (result.Status == "needs_input")
                    {
                        var paused = await PauseForOwnerAsync(result.CompletionMessage ?? "The app needs your input. Resolve it on screen, then choose Resume.", sequence);
                        if (paused is { } outcome) return outcome;
                        continue;
                    }
                    var completed = result.Status == "completed";
                    if (completed && WhatsAppCompletionGuard.RequiresReview(instruction))
                        return Finish(AgentOutcome.Uncertain, WhatsAppCompletionGuard.ReviewMessage);
                    return Finish(
                        completed ? AgentOutcome.Completed : AgentOutcome.NeedsInput,
                        result.CompletionMessage ?? (completed ? "The task looks complete. Check the result on screen." : "Autobots needs more detail to continue."));
                }
                if (result.Status != "proposal" || result.Proposal is null)
                    throw new InvalidOperationException("The service returned an unsupported task step.");

                var action = result.Proposal.Action;
                if (exactNotepadText is not null && ExactNotepadText.IsNotepad(frame.ForegroundProcessName) &&
                    action is TypeTextAction proposedText && !string.Equals(proposedText.Text, exactNotepadText, StringComparison.Ordinal))
                {
                    runLog.Write("rejected", new { sequence, stage = "exact-notepad-text", proposedLength = proposedText.Text.Length,
                        requiredLength = exactNotepadText.Length });
                    return Finish(AgentOutcome.NeedsInput,
                        "The proposed Notepad text differed from the exact story in your task. Autobots stopped before typing or sharing it.");
                }
                if (!calendarFieldLoop.CanExecute(action, frame.ForegroundWindowTitle, result.Intent))
                {
                    if (frame.CalendarFields is not null && CalendarTaskTiming.FromOwnerInstruction(instruction) is not null)
                    {
                        runLog.Write("calendar_adapter_started", new { sequence });
                        var repair = await RepairCalendarFieldsAsync(sequence, frame.ForegroundWindowHandle);
                        sequence += repair.Actions;
                        if (repair.Error is not null)
                            return Finish(AgentOutcome.NeedsInput, repair.Error);
                        calendarFieldLoop.Reset();
                        consecutiveRejections = 0;
                        history.Add(new StepHistoryEntry(sequence, "Calendar adapter verified all four date/time fields", "verified",
                            "Proceed with the remaining task. Do not change the verified dates or times; check before Save."));
                        settle = (TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(700));
                        continue;
                    }
                    var reason = CalendarFieldLoopGuard.RecoveryHint;
                    runLog.Write("rejected", new { sequence, stage = "calendar-field-loop", reason });
                    if (++consecutiveRejections > MaxConsecutiveRejections)
                        return Finish(AgentOutcome.NeedsInput, "Calendar date and time controls did not respond. Check the four visible values before starting again.");
                    history.Add(new StepHistoryEntry(sequence, ActionNarration.ForHistory(action, result.Intent), "rejected", reason));
                    Emit(executed + 1, Icons.Shield, "Calendar field needs a new approach", reason, TimelineTone.Warning);
                    settle = (TimeSpan.Zero, TimeSpan.FromMilliseconds(300));
                    continue;
                }
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
                calendarFieldLoop.RecordExecuted(action, frame.ForegroundWindowTitle, result.Intent);
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

        async Task<AgentRunResult?> PauseForOwnerAsync(string reason, long sequence)
        {
            if (++ownerHandoffs > 3)
                return Finish(AgentOutcome.NeedsInput, "This task still needs your help after several attempts. Check the app and start a new task with any missing details.");
            var remaining = options.MaxDuration - clock.Elapsed;
            if (remaining <= TimeSpan.Zero)
                return Finish(AgentOutcome.TimeLimit, "The task's time limit ended while waiting for the app.");
            _captureAuthorized = false;
            pointerOverlay?.Hide();
            await desktop.StopInputAsync(CancellationToken.None).ConfigureAwait(false);
            stage = "waiting_for_owner";
            runLog.Write("paused_for_owner", new { sequence, secondsRemaining = remaining.TotalSeconds });
            var resume = _ownerResume.WaitAsync(remaining, token);
            Report(AgentPhase.WaitingForOwner, executed + 1, options.MaxSteps,
                "Waiting for you", reason, null);
            Emit(executed + 1, Icons.Warning, "Your action needed", reason, TimelineTone.Warning);
            if (!await resume.ConfigureAwait(false))
                return Finish(AgentOutcome.TimeLimit, "The task timed out while waiting for your response.");
            token.ThrowIfCancellationRequested();
            _captureAuthorized = true;
            runLog.Write("resumed_by_owner", new { sequence });
            history.Add(new StepHistoryEntry(sequence, "Owner chose Resume after a visible handoff", "owner_resumed", "Reobserve the current screen. Do not replay an uncertain send, submit, or meeting creation."));
            settle = (TimeSpan.FromMilliseconds(350), TimeSpan.FromMilliseconds(1200));
            return null;
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
        AgentOutcome.Uncertain => "Result needs review",
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

    private static CalendarEditorField? SelectCalendarField(CalendarEventFields? fields, string name) => name switch
    {
        "Start date" => fields?.StartDate,
        "Start time" => fields?.StartTime,
        "End date" => fields?.EndDate,
        "End time" => fields?.EndTime,
        _ => null
    };

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
