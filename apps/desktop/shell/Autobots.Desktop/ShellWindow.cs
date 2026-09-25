using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Autobots.Contracts;
using Autobots.Core;
using System.Net.Sockets;
#if WINDOWS
using Autobots.Platform;
using Autobots.Platform.Windows;
#endif

namespace Autobots.Desktop;

public sealed class ShellWindow : Window
{
    private const int MaxActionsPerTask = 20;
    private static readonly TimeSpan MaxTaskDuration = TimeSpan.FromMinutes(5);
    private readonly LocalTaskSupervisor _supervisor = new();
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(120) };
    private readonly TextBlock _status;
    private readonly TextBlock _proposal;
    private readonly TextBlock _taskDisclosure;
    private readonly TextBox _instruction;
    private readonly Button _signIn;
    private readonly Button _start;
    private readonly Button _stop;
    private readonly CognitoOAuthLogin? _login;
    private readonly AutobotsApiClient? _api;
#if WINDOWS
    private CancellationTokenSource? _activeOperation;
    private Guid? _activeTaskId;
    private bool _userStopped;
    private readonly WindowsDesktopAdapter _platform;
    private readonly WindowsStopShortcut _stopShortcut;
#endif

    public ShellWindow()
    {
        Title = "Autobots by Origin Studios";
        Width = 700;
        Height = 680;
        MinWidth = 480;
        MinHeight = 500;

        var heading = new TextBlock { Text = "Autobots", FontSize = 28, FontWeight = FontWeight.SemiBold };
        var brand = new TextBlock
        {
            Text = "by Origin Studios",
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 2, 0, 16)
        };
        _instruction = new TextBox
        {
            PlaceholderText = "Describe a task for Autobots to perform in a visible app",
            AcceptsReturn = true,
            MinHeight = 90,
            TextWrapping = TextWrapping.Wrap,
            IsEnabled = false
        };
        AutomationProperties.SetName(_instruction, "Task instruction");
        _taskDisclosure = new TextBlock
        {
            Text = "Starting a task authorizes Autobots to capture and send full primary-screen images to Gemini, then carry out the requested steps in the visible app without pausing for each action. It may make changes before you see them; STOP does not undo changes already made. A task can run for up to five minutes or 20 actions. Keep private content off screen.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = Brushes.DarkSlateGray
        };

        try
        {
            var configuration = DesktopConfiguration.Load();
            _login = new CognitoOAuthLogin(_httpClient, configuration);
            _api = new AutobotsApiClient(_httpClient, configuration);
            _status = new TextBlock { Text = "Ready. Sign in to connect to the owner-only API.", TextWrapping = TextWrapping.Wrap };
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or System.Text.Json.JsonException)
        {
            _status = new TextBlock { Text = error.Message, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkRed };
        }

        _signIn = new Button { Content = "Sign in with Cognito", MinHeight = 40, Margin = new Thickness(0, 12, 0, 0), IsEnabled = _login is not null };
        _signIn.Click += SignInClicked;
        _start = new Button
        {
            Content = OperatingSystem.IsWindows() ? "Start autonomous Windows task" : "Desktop automation is currently Windows-only",
            IsEnabled = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 48,
            Margin = new Thickness(0, 10, 0, 0)
        };
        _start.Click += StartAnalysisClicked;
        _stop = new Button
        {
            Content = "STOP  ·  Ctrl+Alt+Shift+S",
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            MinHeight = 56,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 14, 0, 0)
        };
        _stop.Click += StopClicked;
        _proposal = new TextBlock
        {
            Text = "Task progress and the most recent action will appear here.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(12),
            Background = Brushes.Gainsboro
        };
        _status.Margin = new Thickness(0, 12, 0, 0);

#if WINDOWS
        _platform = new WindowsDesktopAdapter(IsTaskCaptureAuthorizedAsync, MinimizeShellAsync, RestoreShellAsync);
        _stopShortcut = new WindowsStopShortcut();
        Opened += (_, _) => _ = RegisterGlobalStopShortcutAsync();
#endif

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Spacing = 4,
                Margin = new Thickness(28),
                Children = { heading, brand, _instruction, _taskDisclosure, _signIn, _start, _stop, _proposal, _status }
            }
        };
        Closed += (_, _) =>
        {
#if WINDOWS
            _activeOperation?.Cancel();
            _ = _platform.StopInputAsync(CancellationToken.None);
            _ = _stopShortcut.DisposeAsync();
#endif
            _supervisor.Dispose();
            _httpClient.Dispose();
        };
    }

    private async void SignInClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs)
    {
        if (_login is null)
            return;
        _signIn.IsEnabled = false;
        _status.Text = "Opening Cognito sign-in in your browser...";
        try
        {
            await _login.SignInAsync(CancellationToken.None);
            _instruction.IsEnabled = true;
            _start.IsEnabled = OperatingSystem.IsWindows();
            _signIn.Content = "Signed in · Sign in again";
            _status.Text = "Signed in. Submitting a task starts bounded autonomous desktop control.";
        }
        catch (Exception error) when (error is InvalidOperationException or TimeoutException or HttpRequestException or SocketException or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            _status.Text = error.Message;
        }
        finally
        {
            _signIn.IsEnabled = _login is not null;
        }
    }

    private async void StartAnalysisClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs)
    {
#if WINDOWS
        if (_login is null || _api is null || !_login.IsSignedIn)
        {
            _status.Text = "Sign in before submitting a task.";
            return;
        }
        var instruction = _instruction.Text?.Trim();
        if (string.IsNullOrWhiteSpace(instruction) || instruction.Length > 4000)
        {
            _status.Text = "Enter a task between 1 and 4000 characters.";
            return;
        }
        if (_activeOperation is not null)
        {
            _status.Text = "A task is already active. Press STOP before starting another.";
            return;
        }

        var operation = new CancellationTokenSource();
        _activeOperation = operation;
        _activeTaskId = null;
        _userStopped = false;
        _start.IsEnabled = false;
        _stop.IsEnabled = true;
        var completed = false;
        var lastStep = 0;
        decimal taskCost = 0;
        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            var accessToken = await _login.GetAccessTokenAsync(operation.Token);
            var deviceId = LocalDeviceIdentity.GetOrCreate();
            await _api.EnrollDeviceAsync(accessToken, deviceId, operation.Token);
            var task = await _api.CreateTaskAsync(accessToken, deviceId, instruction, Guid.NewGuid().ToString("N"), operation.Token);
            _activeTaskId = task.TaskId;
            operation.Token.ThrowIfCancellationRequested();

            var lease = _supervisor.StartTask(task.TaskId, deviceId, ownerSubmittedTask: true);
            for (var step = 1; step <= MaxActionsPerTask; step++)
            {
                lastStep = step;
                operation.Token.ThrowIfCancellationRequested();
                if (DateTimeOffset.UtcNow - startedAt > MaxTaskDuration)
                    throw new TimeoutException("The five-minute task limit was reached. Check the visible result and start a new task if needed.");

                _status.Text = step == 1
                    ? "Capturing the target app for the owner-submitted task..."
                    : $"Re-observing the target app for step {step} of {MaxActionsPerTask}...";
                var frame = await _platform.CapturePrimaryDisplayAsync(operation.Token);
                if (frame.PngBytes.Length > 3_000_000)
                    throw new InvalidOperationException("This display capture exceeds the API's 3 MB image limit. Reduce display resolution and try again.");
                operation.Token.ThrowIfCancellationRequested();

                if (!Guid.TryParse(frame.ObservationId, out var observationId) || !_supervisor.TrySetObservation(lease, observationId))
                    throw new InvalidOperationException("The local supervisor could not accept this screen observation.");

                _status.Text = $"Sending step {step} and a {frame.PixelWidth}×{frame.PixelHeight} screen image to Gemini...";
                var result = await _api.ProposeActionAsync(
                    accessToken,
                    task.TaskId,
                    deviceId,
                    observationId,
                    lease.LeaseId,
                    lease.Epoch,
                    step,
                    frame.PngBytes,
                    operation.Token);
                operation.Token.ThrowIfCancellationRequested();
                if (result.Execution != "not_executed")
                    throw new InvalidOperationException("The API returned an unexpected execution state; Autobots stopped this task.");
                if (decimal.TryParse(result.ActualCostUsd, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var stepCost))
                    taskCost += stepCost;

                if ((result.Status is "completed" or "needs_input") && result.Proposal is null)
                {
                    completed = result.Status == "completed";
                    _proposal.Text = $"{(completed ? "TASK COMPLETE" : "TASK NEEDS INPUT")}{Environment.NewLine}{Environment.NewLine}{result.CompletionMessage ?? (completed ? "The model reports that the visible task is complete." : "Please add the missing task details and start again.")}{Environment.NewLine}{Environment.NewLine}Estimated model cost: USD {taskCost.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)}";
                    _status.Text = completed
                        ? "Autobots reported completion from the latest screen. Check the result in the target app."
                        : "Autobots stopped because it needs more information. Update the task and start again.";
                    break;
                }
                if (result.Status != "proposal" || result.Proposal is null)
                    throw new InvalidOperationException("The API returned an unsupported task step.");
                if (!_supervisor.TryValidateProposal(lease, result.Proposal, frame, out var rejection))
                    throw new InvalidOperationException($"The local supervisor rejected the proposal: {rejection}");

                if (!_supervisor.TryAuthorizeTaskAction(lease, result.Proposal, frame, DateTimeOffset.UtcNow, out var authorization, out rejection) || authorization is null)
                    throw new UnauthorizedAccessException($"The local supervisor rejected the task action: {rejection}");

                _proposal.Text = $"AUTONOMOUS STEP {step}{Environment.NewLine}{Describe(result.Proposal.Action)}{Environment.NewLine}{Environment.NewLine}Target: {DisplayTarget(frame)}{Environment.NewLine}Model: {result.ModelId}{Environment.NewLine}Estimated task charge: USD {taskCost.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)}{Environment.NewLine}{Environment.NewLine}The submitted task is the authorization for this step. STOP remains available.";
                _status.Text = $"Executing step {step} in {DisplayTarget(frame)}...";
                await _platform.ExecuteAuthorizedActionAsync(authorization, frame, operation.Token);
                operation.Token.ThrowIfCancellationRequested();
                _proposal.Text = $"EXECUTED STEP {step}{Environment.NewLine}{Describe(result.Proposal.Action)}{Environment.NewLine}{Environment.NewLine}Target: {DisplayTarget(frame)}{Environment.NewLine}Step: {step} of {MaxActionsPerTask}{Environment.NewLine}Estimated task charge: USD {taskCost.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)}";
                _status.Text = $"Step {step} executed. Re-observing before another action...";
            }
            if (!completed && !_userStopped && lastStep >= MaxActionsPerTask)
                _status.Text = $"Stopped at the {MaxActionsPerTask}-action limit. Check the target app before starting another task. Estimated model cost: USD {taskCost.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)}.";
        }
        catch (OperationCanceledException)
        {
            _status.Text = _userStopped
                ? "Stopped locally. Any late model result is rejected; the API stop is being sent."
                : "The task was cancelled. The local lease is inactive.";
        }
        catch (Exception error)
        {
            _status.Text = error.Message;
        }
        finally
        {
            _supervisor.Stop();
            _ = _platform.StopInputAsync(CancellationToken.None);
            if (_activeTaskId is { } taskId)
                await StopCloudTaskAsync(taskId);
            _activeTaskId = null;
            if (ReferenceEquals(_activeOperation, operation))
                _activeOperation = null;
            operation.Dispose();
            _start.IsEnabled = _login?.IsSignedIn == true && OperatingSystem.IsWindows();
            _stop.IsEnabled = true;
        }
#else
        await Task.CompletedTask;
#endif
    }

    private void StopClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs) => StopCurrentTask();

    private void StopCurrentTask()
    {
#if WINDOWS
        _userStopped = true;
        _supervisor.Stop();
        _activeOperation?.Cancel();
        _ = _platform.StopInputAsync(CancellationToken.None);
#else
        _supervisor.Stop();
#endif
        _proposal.Text = "STOP requested. Autobots cancels queued follow-up actions and releases any input it still holds. It cannot undo changes already made in another app.";
        _status.Text = "Stopped locally. Any late model result will be rejected.";
    }

#if WINDOWS
    private ValueTask<bool> IsTaskCaptureAuthorizedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var authorized = _activeOperation is { IsCancellationRequested: false } && _activeTaskId is not null && !_userStopped;
        return ValueTask.FromResult(authorized);
    }
    private async ValueTask<bool> MinimizeShellAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var minimized = false;
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!cancellationToken.IsCancellationRequested && WindowState != WindowState.Minimized)
            {
                WindowState = WindowState.Minimized;
                minimized = true;
            }
        });
        return minimized || WindowState == WindowState.Minimized;
    }

    private async ValueTask RestoreShellAsync(CancellationToken cancellationToken)
    {
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
        });
    }

    private async Task RegisterGlobalStopShortcutAsync()
    {
        try
        {
            await _stopShortcut.RegisterAsync(
                _ =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(StopCurrentTask);
                    return ValueTask.CompletedTask;
                },
                CancellationToken.None);
            _status.Text = "Global STOP is armed: Ctrl+Alt+Shift+S.";
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _status.Text = $"Global STOP shortcut could not be registered: {error.Message} The STOP button remains available.";
        }
    }

    private static string DisplayTarget(CapturedFrame frame) =>
        string.IsNullOrWhiteSpace(frame.ForegroundWindowTitle)
            ? $"Windows process {frame.ForegroundProcessId}"
            : $"{frame.ForegroundWindowTitle} (process {frame.ForegroundProcessId})";
#endif

    private async Task StopCloudTaskAsync(Guid taskId)
    {
        if (_login is null || _api is null)
            return;
        try
        {
            var token = await _login.GetAccessTokenAsync(CancellationToken.None);
            await _api.StopTaskAsync(token, taskId, CancellationToken.None);
        }
        catch (Exception error) when (error is InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
#if WINDOWS
            if (!_userStopped)
                _status.Text += " Cloud stop could not be confirmed; the local STOP remains active.";
#endif
        }
    }

    private static string Describe(ProposedAction action) => action switch
    {
        ClickAction click => $"Click {click.Button} at normalized display point ({click.X}, {click.Y}).",
        TypeTextAction text => $"Type exactly: {text.Text}",
        KeyPressAction key => $"Press key: {key.Key}",
        ScrollAction scroll => $"Scroll at ({scroll.X}, {scroll.Y}) by ({scroll.DeltaX}, {scroll.DeltaY}).",
        WaitAction wait => $"Wait for {wait.DurationMs} milliseconds.",
        _ => "An unsupported action was returned."
    };
}
