using System.Globalization;
using System.Net.Sockets;
using Autobots.Core;
using Autobots.Platform;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Autobots.Desktop;

public sealed class ShellWindow : Window
{
    private const int MaxTimelineRows = 80;
    private static readonly TimeSpan VoiceCountdown = TimeSpan.FromSeconds(3);
    private static readonly (string Label, string Instruction)[] Suggestions =
    [
        ("Draft a to-do list in Notepad", "Open Notepad and write a short to-do list for today"),
        ("Check the weather in Edge", "Open Microsoft Edge and search for today's weather"),
        ("Create hello.py in VS Code", "Open VS Code, create hello.py and make it print hello"),
        ("Show my Downloads", "Open File Explorer and show my Downloads folder")
    ];
    private static readonly string[] VoiceLanguages = ["Auto-detect", "English", "Hindi", "Malayalam", "Tamil", "Telugu", "Kannada", "Spanish", "French", "German"];

    private readonly Action _shutdown;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(90) };
    private readonly LocalTaskSupervisor _supervisor = new();
    private readonly CognitoOAuthLogin? _login;
    private readonly AutobotsApiClient? _api;
    private readonly string? _configurationError;
    private readonly PilotHudWindow _hud = new();
    private readonly DesktopServices _services;
    private readonly AgentTaskRunner? _runner;
    private readonly Bitmap? _brandBitmap;
    private readonly Stream? _iconStream;

    private readonly TextBox _composer;
    private readonly Button _micButton;
    private readonly Button _startButton;
    private readonly Button _accountButton;
    private readonly TextBlock _limitsText;
    private readonly TextBlock _statusText;
    private readonly Border _statusDot;
    private readonly StackPanel _timeline;
    private readonly ScrollViewer _timelineScroll;
    private readonly Control _timelineEmpty;
    private readonly Border _settingsSheet;
    private readonly Panel _settingsScrim;
    private readonly TextBlock _greeting;
    private readonly TextBlock _voiceHint;

    private AppSettings _settings;
    private CancellationTokenSource? _voiceSession;
    private CancellationTokenSource? _voiceStop;
    private DispatcherTimer? _countdown;
    private string? _pendingVoiceTask;
    private bool _signingIn;
    private bool _quitting;
    private bool _trayNoticeShown;
    private string _stopHotkey = "Ctrl+Alt+Shift+S";
    private string _talkHotkey = "Ctrl+Alt+Space";

    public ShellWindow(AppSettings settings, Action shutdown)
    {
        _settings = settings;
        _shutdown = shutdown;
        Title = "Autobots";
        Width = 1120;
        Height = 800;
        MinWidth = 900;
        MinHeight = 660;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        RequestedThemeVariant = ThemeVariant.Dark;
        FontFamily = Ui.TextFont;
        Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.35, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.Parse("#0F1720"), 0), new GradientStop(Color.Parse("#0A0E13"), 1) }
        };

        _iconStream = typeof(ShellWindow).Assembly.GetManifestResourceStream("Autobots.Desktop.Assets.AutobotsIcon.ico");
        if (_iconStream is not null)
            Icon = new WindowIcon(_iconStream);
        _brandBitmap = LoadBrandBitmap();

        try
        {
            var configuration = DesktopConfiguration.Load();
            _login = new CognitoOAuthLogin(_httpClient, configuration);
            _api = new AutobotsApiClient(_httpClient, configuration);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or System.Text.Json.JsonException)
        {
            _configurationError = error.Message;
        }

        var surfaces = new AssistantSurfaces(_hud, () => _services?.PointerOverlay);
        _services = DesktopServices.Create(IsTaskCaptureAuthorizedAsync, surfaces);
        if (_services.Desktop is not null && _login is not null && _api is not null)
        {
            _runner = new AgentTaskRunner(_services.Desktop, _services.PointerOverlay, _supervisor, _api, _login);
            _runner.Progress += progress => Dispatcher.UIThread.Post(() => OnProgress(progress));
            _runner.Timeline += entry => Dispatcher.UIThread.Post(() => AddTimelineRow(entry));
        }
        _stopHotkey = _services.StopShortcutText;
        _talkHotkey = _services.TalkShortcutText;

        // ---- Title bar -------------------------------------------------------------------------
        var logo = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(7),
            ClipToBounds = true,
            Child = _brandBitmap is null
                ? Ui.Icon(Icons.Autobots, 18, Ui.AccentBrush)
                : new Image { Source = _brandBitmap, Stretch = Stretch.UniformToFill }
        };
        var titleText = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                Ui.Text("Autobots", 13.5, Ui.TextPrimary, FontWeight.SemiBold),
                new Border
                {
                    Background = Ui.Solid("#1A2EE6C8"),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(6, 1),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = Ui.Text("PILOT", 9.5, Ui.AccentBrush, FontWeight.Bold)
                }
            }
        };
        _accountButton = Ui.Skin(new Button
        {
            Padding = new Thickness(12, 6),
            CornerRadius = new CornerRadius(16),
            VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = _login is not null
        }, Ui.ButtonKind.Subtle);
        _accountButton.Click += async (_, _) => await SignInAsync();
        AutomationProperties.SetName(_accountButton, "Autobots account");
        var settingsButton = Ui.Skin(new Button
        {
            Content = Ui.Icon(Icons.Settings, 17, Ui.TextPrimary),
            Padding = new Thickness(9),
            CornerRadius = new CornerRadius(16),
            VerticalAlignment = VerticalAlignment.Center
        }, Ui.ButtonKind.Ghost);
        ToolTip.SetTip(settingsButton, "Settings");
        AutomationProperties.SetName(settingsButton, "Settings");
        settingsButton.Click += (_, _) => ToggleSettings(true);

        var titleBar = new Grid
        {
            Height = 56,
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto"),
            ColumnSpacing = 10,
            Margin = new Thickness(28, 0, 20, 0)
        };
        Grid.SetColumn(logo, 0);
        Grid.SetColumn(titleText, 1);
        Grid.SetColumn(_accountButton, 3);
        Grid.SetColumn(settingsButton, 4);
        titleBar.Children.Add(logo);
        titleBar.Children.Add(titleText);
        titleBar.Children.Add(_accountButton);
        titleBar.Children.Add(settingsButton);

        // ---- Hero ------------------------------------------------------------------------------
        _greeting = Ui.Text(Greeting(), 13, Ui.AccentBrush, FontWeight.SemiBold);
        var hero = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                _greeting,
                Ui.Text("What can I do on your PC?", 30, Ui.TextPrimary, FontWeight.SemiBold, display: true),
                Ui.Text("Describe a task or just say it. Autobots takes the mouse and keyboard, works across your apps, browsers and VS Code, and shows every move as it goes.", 14, Ui.TextSecondary)
            }
        };

        // ---- Composer --------------------------------------------------------------------------
        _composer = Ui.SkinComposer(new TextBox
        {
            PlaceholderText = "Describe a task, e.g. “Open VS Code, create hello.py and make it print hello”",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 76,
            MaxHeight = 200,
            FontSize = 16,
            Padding = new Thickness(4, 2),
            MaxLength = 4000
        });
        AutomationProperties.SetName(_composer, "Task instruction");
        _composer.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter && args.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control))
            {
                args.Handled = true;
                _ = StartTaskFromComposerAsync();
            }
        };

        _micButton = new Button
        {
            Width = 44,
            Height = 44,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(22),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = Ui.Icon(Icons.Mic, 20, Ui.TextPrimary)
        };
        Ui.Skin(_micButton, Ui.ButtonKind.Subtle);
        AutomationProperties.SetName(_micButton, "Speak a task");
        _micButton.Click += async (_, _) => await ToggleVoiceAsync();

        _voiceHint = Ui.Text($"Press {_talkHotkey} anywhere to talk", 12, Ui.TextTertiary);
        _voiceHint.VerticalAlignment = VerticalAlignment.Center;
        _voiceHint.TextWrapping = TextWrapping.NoWrap;
        _limitsText = Ui.Text(string.Empty, 12, Ui.TextSecondary, FontWeight.SemiBold);
        _limitsText.TextWrapping = TextWrapping.NoWrap;
        var limitsChip = Ui.Skin(new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { Ui.Icon(Icons.Clock, 13, Ui.TextSecondary), _limitsText }
            },
            Padding = new Thickness(10, 6),
            VerticalAlignment = VerticalAlignment.Center
        }, Ui.ButtonKind.Chip);
        ToolTip.SetTip(limitsChip, "Per-task step and time limits");
        limitsChip.Click += (_, _) => ToggleSettings(true);

        _startButton = Ui.Skin(new Button
        {
            MinWidth = 132,
            Height = 44,
            Padding = new Thickness(18, 0),
            CornerRadius = new CornerRadius(22),
            FontSize = 14.5,
            FontWeight = FontWeight.SemiBold,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            IsEnabled = _runner is not null
        }, Ui.ButtonKind.Accent);
        AutomationProperties.SetName(_startButton, "Start task");
        _startButton.Click += async (_, _) =>
        {
            if (_runner?.IsRunning == true)
                StopEverything("Stop button");
            else
                await StartTaskFromComposerAsync();
        };

        var composerFooter = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 12 };
        var micGroup = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { _micButton, _voiceHint } };
        Grid.SetColumn(micGroup, 0);
        Grid.SetColumn(limitsChip, 2);
        Grid.SetColumn(_startButton, 3);
        composerFooter.Children.Add(micGroup);
        composerFooter.Children.Add(limitsChip);
        composerFooter.Children.Add(_startButton);

        var composerCard = new Border
        {
            Background = Ui.Solid("#14FFFFFF"),
            BorderBrush = Ui.Solid("#24FFFFFF"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(18, 16, 14, 14),
            BoxShadow = BoxShadows.Parse("0 18 40 0 #40000000"),
            Child = new StackPanel { Spacing = 12, Children = { _composer, composerFooter } }
        };

        var chips = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8 };
        foreach (var (suggestionLabel, suggestion) in Suggestions)
        {
            var chip = Ui.Skin(new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 7,
                    Children = { Ui.Icon(Icons.Sparkle, 12, Ui.AccentBrush), Ui.Text(suggestionLabel, 12.5, Ui.Solid("#DCE5EE")) }
                },
                Padding = new Thickness(12, 7)
            }, Ui.ButtonKind.Chip);
            ToolTip.SetTip(chip, suggestion);
            chip.Click += (_, _) =>
            {
                _composer.Text = suggestion;
                _composer.Focus();
                _composer.CaretIndex = suggestion.Length;
            };
            chips.Children.Add(chip);
        }

        // ---- Activity + guidance ---------------------------------------------------------------
        _timeline = new StackPanel { Spacing = 2 };
        _timelineEmpty = new StackPanel
        {
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 14),
            Children =
            {
                Ui.Companion(94),
                CenteredText("Nothing yet. Start a task and each step shows up here.", 13, Ui.TextTertiary)
            }
        };
        _timelineScroll = new ScrollViewer
        {
            Height = 236,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new Panel { Children = { _timelineEmpty, _timeline } }
        };
        var clearButton = Ui.Skin(new Button { Content = Ui.Text("Clear", 12, Ui.TextSecondary), Padding = new Thickness(10, 4) }, Ui.ButtonKind.Ghost);
        clearButton.Click += (_, _) =>
        {
            _timeline.Children.Clear();
            _timelineEmpty.IsVisible = true;
        };
        var activityHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var activityTitle = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { Ui.Icon(Icons.Bolt, 15, Ui.AccentBrush), Ui.Text("Live activity", 14, Ui.TextPrimary, FontWeight.SemiBold) }
        };
        Grid.SetColumn(clearButton, 1);
        activityHeader.Children.Add(activityTitle);
        activityHeader.Children.Add(clearButton);
        var activityCard = Ui.Card(new StackPanel { Spacing = 10, Children = { activityHeader, _timelineScroll } }, 18);

        var guideCard = Ui.Card(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Ui.Text("How it works", 14, Ui.TextPrimary, FontWeight.SemiBold),
                GuideRow(Icons.Eye, "Sees your screen during a task", "A fresh screenshot goes to the AI before each step. Nothing is stored."),
                GuideRow(Icons.Cursor, "Uses your real pointer", "Watch the glowing cursor move, click and type in any app."),
                GuideRow(Icons.Mic, "Talk instead of typing", $"Press {_talkHotkey} from any app and say the task."),
                GuideRow(Icons.Stop, "Stop instantly", $"{_stopHotkey} or the Stop button. Finished steps can't be undone."),
                GuideRow(Icons.Shield, "Stays in bounds", "No passwords, admin windows, UAC or lock screen. It asks when unsure.")
            }
        }, 18);

        var lowerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("5*,3*"), ColumnSpacing = 16 };
        Grid.SetColumn(guideCard, 1);
        lowerGrid.Children.Add(activityCard);
        lowerGrid.Children.Add(guideCard);

        var body = new StackPanel
        {
            Spacing = 16,
            MaxWidth = 1060,
            Margin = new Thickness(36, 8, 36, 24),
            Children = { hero, composerCard, chips, lowerGrid }
        };
        if (_configurationError is not null)
            body.Children.Insert(1, Banner(Icons.Warning, "Setup needs attention", _configurationError, Ui.WarningBrush));
        else if (_services.Desktop is null)
            body.Children.Insert(1, Banner(Icons.Warning, "Desktop control isn't available on this system yet", "Autobots currently controls Windows 11. macOS and Linux support is planned.", Ui.WarningBrush));

        // ---- Status bar ------------------------------------------------------------------------
        _statusDot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = Ui.TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        _statusText = Ui.Text("Starting…", 12, Ui.TextSecondary);
        _statusText.TextWrapping = TextWrapping.NoWrap;
        _statusText.TextTrimming = TextTrimming.CharacterEllipsis;
        var shortcuts = Ui.Text($"Stop  {_stopHotkey}    ·    Talk  {_talkHotkey}", 12, Ui.TextTertiary);
        shortcuts.TextWrapping = TextWrapping.NoWrap;
        var statusBar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 10,
            Height = 36,
            Margin = new Thickness(20, 0),
        };
        Grid.SetColumn(_statusText, 1);
        Grid.SetColumn(shortcuts, 2);
        statusBar.Children.Add(_statusDot);
        statusBar.Children.Add(_statusText);
        statusBar.Children.Add(shortcuts);
        var statusBorder = new Border { BorderBrush = Ui.Divider, BorderThickness = new Thickness(0, 1, 0, 0), Background = Ui.Solid("#10000000"), Child = statusBar };

        // ---- Settings sheet --------------------------------------------------------------------
        _settingsScrim = new Panel { Background = Ui.Solid("#66000000"), IsVisible = false };
        _settingsScrim.PointerPressed += (_, _) => ToggleSettings(false);
        _settingsSheet = BuildSettingsSheet();

        var root = new Grid { RowDefinitions = new RowDefinitions("56,*,Auto") };
        var scroll = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(titleBar, 0);
        Grid.SetRow(scroll, 1);
        Grid.SetRow(statusBorder, 2);
        root.Children.Add(titleBar);
        root.Children.Add(scroll);
        root.Children.Add(statusBorder);
        Grid.SetRowSpan(_settingsScrim, 3);
        Grid.SetRowSpan(_settingsSheet, 3);
        root.Children.Add(_settingsScrim);
        root.Children.Add(_settingsSheet);
        Content = new Panel { Children = { AmbientGlow(), root } };

        // ---- Wiring ----------------------------------------------------------------------------
        _hud.StopRequested += (_, _) => StopEverything("HUD");
        _hud.FinishListeningRequested += (_, _) => _voiceStop?.Cancel();
        _hud.CancelRequested += (_, _) => CancelVoice();
        _hud.StartNowRequested += async (_, _) => await StartPendingVoiceTaskAsync();
        _hud.EditRequested += (_, _) => EditPendingVoiceTask();
        _hud.OpenAppRequested += (_, _) => ShowFromTray();
        Opened += async (_, _) => await OnOpenedAsync();
        Closing += OnClosing;

        RefreshAccount();
        RefreshLimits();
        SetIdleUi();
    }

    // ---- Public entry points used by the tray icon --------------------------------------------

    public void ShowFromTray()
    {
        _hud.HideHud();
        if (!IsVisible)
            Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    public void TalkFromTray() => _ = ToggleVoiceAsync();

    public void StopFromTray() => StopEverything("tray");

    public async void QuitApplication()
    {
        _quitting = true;
        StopEverything("quit");
        _hud.Close();
        await _services.DisposeAsync();
        _supervisor.Dispose();
        _brandBitmap?.Dispose();
        _iconStream?.Dispose();
        _httpClient.Dispose();
        Close();
        _shutdown();
    }

    // ---- Lifecycle -----------------------------------------------------------------------------

    private async Task OnOpenedAsync()
    {
        if (TryGetPlatformHandle()?.Handle is { } handle)
            NativeWindowTraits.UseDarkFrame(handle);
        _greeting.Text = Greeting();
        if (Program.UiPreview is { } preview)
        {
            ShowUiPreview(preview);
            return;
        }
        await RegisterShortcutsAsync();
        if (_api is null)
            return;
        try
        {
            var features = await _api.GetFeaturesAsync(CancellationToken.None);
            if (!features.Transcription)
            {
                _voiceHint.Text = "Voice needs the updated Autobots service";
                ToolTip.SetTip(_micButton, "The connected Autobots service doesn't support voice yet. Deploy API 0.3 or later.");
            }
            if (!features.Live)
                SetStatus("The Autobots service is in mock mode; live AI is turned off.", Ui.WarningBrush);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            SetStatus("Can't reach the Autobots service right now. Check your internet connection.", Ui.WarningBrush);
        }
    }

    /// <summary>Developer preview of the pilot bar and activity feed with sample content; nothing runs.</summary>
    private void ShowUiPreview(string state)
    {
        AddRunHeader("Open VS Code, create hello.py and make it print hello");
        var now = DateTimeOffset.Now;
        AddTimelineRow(new AgentTimelineEntry(1, Icons.Keyboard, "Pressed Win", "Open the Start menu to find Visual Studio Code", TimelineTone.Neutral, now));
        AddTimelineRow(new AgentTimelineEntry(2, Icons.Keyboard, "Typed “visual studio code” and pressed Enter", "Launch VS Code from Start search", TimelineTone.Neutral, now));
        AddTimelineRow(new AgentTimelineEntry(3, Icons.Cursor, "Clicked", "Create a new file from the Welcome page", TimelineTone.Neutral, now));
        AddTimelineRow(new AgentTimelineEntry(3, Icons.Shield, "Skipped a step", "The focused control is not an accessible text-entry field, so no text was typed. No input was sent.", TimelineTone.Warning, now));
        switch (state)
        {
            case "listening":
                _hud.ShowListening(_talkHotkey);
                _hud.SetLevel(0.55);
                break;
            case "confirm":
                _pendingVoiceTask = "Open Notepad and write a short to-do list for today";
                _hud.ShowConfirm(_pendingVoiceTask, 3, autoStart: true);
                break;
            case "result":
                _hud.ShowResult(HudTone.Success, "Task complete", "hello.py is saved in your workspace and prints “hello”.", TimeSpan.FromMinutes(5));
                break;
            case "settings":
                ToggleSettings(true);
                break;
            default:
                _hud.ShowWorking("Clicking", "Create a new file from the VS Code Welcome page", 4, _settings.MaxStepsPerTask, spinning: false);
                break;
        }
        SetStatus($"UI preview ({state}) — no task is running and input is disarmed.", Ui.WarningBrush);
    }

    private void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        if (_quitting)
            return;
        if (_settings.KeepRunningInTray)
        {
            args.Cancel = true;
            Hide();
            if (!_trayNoticeShown && _runner?.IsRunning != true)
            {
                _trayNoticeShown = true;
                _hud.ShowResult(HudTone.Active, "Autobots is still here", $"Press {_talkHotkey} to talk, or open it from the tray.", TimeSpan.FromSeconds(6));
            }
            return;
        }
        args.Cancel = true;
        QuitApplication();
    }

    private async Task RegisterShortcutsAsync()
    {
        var problems = new List<string>();
        if (_services.StopShortcut is { } stop)
        {
            try
            {
                await stop.RegisterAsync(_ =>
                {
                    Dispatcher.UIThread.Post(() => StopEverything("global shortcut"));
                    return ValueTask.CompletedTask;
                }, CancellationToken.None);
            }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                problems.Add($"{_stopHotkey} is taken by another app; use the Stop button.");
            }
        }
        if (_services.TalkShortcut is { } talk)
        {
            try
            {
                await talk.RegisterAsync(pressed =>
                {
                    Dispatcher.UIThread.Post(() => { _ = OnTalkShortcutAsync(); });
                    return ValueTask.CompletedTask;
                }, CancellationToken.None);
            }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                problems.Add($"{_talkHotkey} is taken by another app; use the mic button.");
                _voiceHint.Text = "Click the mic to talk";
            }
        }
        if (problems.Count > 0)
            SetStatus(string.Join(" ", problems), Ui.WarningBrush);
        else if (_configurationError is null)
            SetIdleStatus();
    }

    // ---- Account -------------------------------------------------------------------------------

    private async Task<bool> SignInAsync()
    {
        if (_login is null || _signingIn)
            return false;
        if (_login.IsSignedIn)
            return true;
        _signingIn = true;
        RefreshAccount();
        SetStatus("Finish signing in to your Autobots account in the browser…", Ui.AccentBrush);
        try
        {
            await _login.SignInAsync(CancellationToken.None);
            SetIdleStatus();
            Activate();
            return true;
        }
        catch (Exception error) when (error is InvalidOperationException or TimeoutException or HttpRequestException or SocketException or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            SetStatus($"Sign-in wasn't completed: {error.Message}", Ui.WarningBrush);
            return false;
        }
        finally
        {
            _signingIn = false;
            RefreshAccount();
        }
    }

    private void RefreshAccount()
    {
        var connected = _login?.IsSignedIn == true;
        var label = _signingIn ? "Waiting for browser…" : connected ? "Connected" : "Connect account";
        _accountButton.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            Children =
            {
                connected
                    ? new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = Ui.SuccessBrush, VerticalAlignment = VerticalAlignment.Center }
                    : Ui.Icon(Icons.Account, 15, Ui.TextPrimary),
                Ui.Text(label, 12.5, Ui.TextPrimary, FontWeight.SemiBold)
            }
        };
        ToolTip.SetTip(_accountButton, connected
            ? "This PC is connected to your private Autobots service. Google and other app sign-ins stay in your browser."
            : "Sign in to your private Autobots account (separate from Google).");
        _accountButton.IsEnabled = _login is not null && !connected && !_signingIn;
    }

    // ---- Tasks ---------------------------------------------------------------------------------

    private async Task StartTaskFromComposerAsync()
    {
        var instruction = _composer.Text?.Trim();
        if (string.IsNullOrWhiteSpace(instruction))
        {
            SetStatus("Describe what Autobots should do first.", Ui.WarningBrush);
            _composer.Focus();
            return;
        }
        await StartTaskAsync(instruction);
    }

    private async Task StartTaskAsync(string instruction)
    {
        if (_runner is null)
        {
            SetStatus(_configurationError ?? "Desktop control isn't available on this system.", Ui.WarningBrush);
            return;
        }
        if (_runner.IsRunning)
            return;
        if (instruction.Length > 4000)
        {
            SetStatus("Keep the task under 4,000 characters.", Ui.WarningBrush);
            return;
        }
        if (_login?.IsSignedIn != true && !await SignInAsync())
            return;

        CancelVoice();
        _composer.Text = instruction;
        _services.Desktop!.PointerSpeed = _settings.PointerSpeed;
        var options = new AgentRunOptions(_settings.MaxStepsPerTask, TimeSpan.FromMinutes(_settings.MaxMinutesPerTask), _settings.ShowPointerHalo);
        AddRunHeader(instruction);
        SetRunningUi();

        // Hand the desktop back to the app the owner was using; Autobots works from the pilot bar.
        if (IsVisible && WindowState != WindowState.Minimized)
            WindowState = WindowState.Minimized;
        _hud.ShowWorking("Getting ready", "Connecting to your Autobots service…", 0, options.MaxSteps, spinning: true);

        AgentRunResult result;
        try
        {
            result = await _runner.RunAsync(instruction, options, CancellationToken.None);
        }
        catch (InvalidOperationException error)
        {
            result = new AgentRunResult(AgentOutcome.Failed, error.Message, 0, 0, TimeSpan.Zero);
        }

        SetIdleUi();
        var tone = result.Outcome switch
        {
            AgentOutcome.Completed => HudTone.Success,
            AgentOutcome.Failed or AgentOutcome.Uncertain => HudTone.Error,
            _ => HudTone.Warning
        };
        var summary = $"{result.Steps} step{(result.Steps == 1 ? string.Empty : "s")} · {FormatDuration(result.Duration)} · AI cost ≈ ${result.CostUsd.ToString("0.0000", CultureInfo.InvariantCulture)}";
        _hud.ShowResult(tone, AgentTaskRunner.OutcomeTitle(result.Outcome), result.Message, TimeSpan.FromSeconds(result.Outcome == AgentOutcome.Completed ? 8 : 14));
        SetStatus($"{AgentTaskRunner.OutcomeTitle(result.Outcome)} — {summary}", tone switch
        {
            HudTone.Success => Ui.SuccessBrush,
            HudTone.Error => Ui.DangerBrush,
            _ => Ui.WarningBrush
        });
    }

    private void StopEverything(string source)
    {
        _ = source;
        CancelVoice();
        if (_runner?.IsRunning == true)
        {
            _runner.RequestStop();
            _hud.ShowWorking("Stopping…", "Releasing the mouse and keyboard.", 0, _settings.MaxStepsPerTask, spinning: true);
            SetStatus("Stopped locally. No further actions will be sent.", Ui.WarningBrush);
        }
    }

    private void OnProgress(AgentProgress progress)
    {
        if (_runner?.IsRunning != true)
            return;
        var spinning = progress.Phase is AgentPhase.Starting or AgentPhase.Observing or AgentPhase.Thinking or AgentPhase.Settling;
        _hud.ShowWorking(progress.Title, progress.Detail, progress.Step, progress.MaxSteps, spinning);
        SetStatus($"Working · step {Math.Min(progress.Step, progress.MaxSteps)} of {progress.MaxSteps} · {progress.Title}", Ui.AccentBrush);
    }

    private ValueTask<bool> IsTaskCaptureAuthorizedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_runner?.IsCaptureAuthorized == true);
    }

    // ---- Voice ---------------------------------------------------------------------------------

    private async Task OnTalkShortcutAsync()
    {
        if (_countdown is not null && _pendingVoiceTask is not null)
        {
            await StartPendingVoiceTaskAsync();
            return;
        }
        await ToggleVoiceAsync();
    }

    private async Task ToggleVoiceAsync()
    {
        if (_voiceSession is not null)
        {
            // A second press finishes the recording.
            _voiceStop?.Cancel();
            return;
        }
        if (_runner?.IsRunning == true)
        {
            _hud.ShowResult(HudTone.Warning, "Autobots is busy", "Stop the current task before giving a new one.", TimeSpan.FromSeconds(4));
            return;
        }
        if (_services.Microphone is null || _api is null || _login is null)
        {
            _hud.ShowResult(HudTone.Warning, "Voice isn't available", _configurationError ?? "Voice capture isn't supported on this system yet.", TimeSpan.FromSeconds(6));
            return;
        }
        if (!_login.IsSignedIn)
        {
            ShowFromTray();
            SetStatus("Connect your Autobots account to use voice.", Ui.WarningBrush);
            if (!await SignInAsync())
                return;
        }

        CancelCountdown();
        _voiceSession = new CancellationTokenSource();
        _voiceStop = new CancellationTokenSource();
        var session = _voiceSession.Token;
        SetListeningUi(true);
        try
        {
            var features = await _api.GetFeaturesAsync(session);
            if (!features.Transcription)
            {
                _hud.ShowResult(HudTone.Warning, "Voice needs a service update", "Deploy the updated Autobots API (0.3 or later) to enable Gemini transcription.", TimeSpan.FromSeconds(8));
                return;
            }
            _hud.ShowListening(_talkHotkey);
            var level = new Progress<double>(_hud.SetLevel);
            var audio = await _services.Microphone.RecordAsync(
                new MicrophoneRecordingOptions(TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(1.4), _settings.StopListeningOnSilence),
                level,
                _voiceStop.Token,
                session);
            if (!audio.SpeechDetected || audio.Duration < TimeSpan.FromSeconds(0.5))
            {
                _hud.ShowResult(HudTone.Warning, "I didn't catch that", "Try again a little closer to the mic. If this keeps happening, allow desktop apps in Windows Settings > Privacy & security > Microphone.", TimeSpan.FromSeconds(8));
                return;
            }

            _hud.ShowTranscribing();
            var accessToken = await _login.GetAccessTokenAsync(session);
            var deviceId = LocalDeviceIdentity.GetOrCreate();
            await _api.EnrollDeviceAsync(accessToken, deviceId, session);
            var language = _settings.VoiceLanguage is null or "Auto-detect" ? null : _settings.VoiceLanguage;
            var transcription = await _api.TranscribeAsync(accessToken, deviceId, audio.WavBytes, language, session);
            if (string.IsNullOrWhiteSpace(transcription.Transcript))
            {
                _hud.ShowResult(HudTone.Warning, "I didn't catch that", "No clear speech was found in the recording. Try again.", TimeSpan.FromSeconds(6));
                return;
            }
            var transcript = transcription.Transcript.Trim();
            _composer.Text = transcript;
            _pendingVoiceTask = transcript;
            if (_settings.AutoStartVoiceTasks)
                BeginCountdown(transcript);
            else
                _hud.ShowConfirm(transcript, 0, autoStart: false);
        }
        catch (OperationCanceledException) when (session.IsCancellationRequested)
        {
            _hud.HideHud();
        }
        catch (Exception error) when (error is PlatformCapabilityUnavailableException or InvalidOperationException or HttpRequestException or OperationCanceledException)
        {
            _hud.ShowResult(HudTone.Error, "Voice didn't work", error is OperationCanceledException ? "The Autobots service took too long to respond." : error.Message, TimeSpan.FromSeconds(8));
        }
        finally
        {
            _voiceSession?.Dispose();
            _voiceSession = null;
            _voiceStop?.Dispose();
            _voiceStop = null;
            SetListeningUi(false);
        }
    }

    private void BeginCountdown(string transcript)
    {
        CancelCountdown();
        var remaining = (int)VoiceCountdown.TotalSeconds;
        _hud.ShowConfirm(transcript, remaining, autoStart: true);
        _countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdown.Tick += async (_, _) =>
        {
            remaining--;
            if (remaining > 0)
            {
                _hud.ShowConfirm(transcript, remaining, autoStart: true);
                return;
            }
            await StartPendingVoiceTaskAsync();
        };
        _countdown.Start();
    }

    private async Task StartPendingVoiceTaskAsync()
    {
        var instruction = _pendingVoiceTask;
        CancelCountdown();
        _pendingVoiceTask = null;
        if (!string.IsNullOrWhiteSpace(instruction))
            await StartTaskAsync(instruction);
    }

    private void EditPendingVoiceTask()
    {
        CancelCountdown();
        if (_pendingVoiceTask is { } transcript)
            _composer.Text = transcript;
        _pendingVoiceTask = null;
        ShowFromTray();
        _composer.Focus();
        _composer.CaretIndex = _composer.Text?.Length ?? 0;
    }

    private void CancelVoice()
    {
        CancelCountdown();
        _pendingVoiceTask = null;
        try
        {
            _voiceSession?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The voice session already ended.
        }
        if (_runner?.IsRunning != true)
            _hud.HideHud();
    }

    private void CancelCountdown()
    {
        _countdown?.Stop();
        _countdown = null;
    }

    // ---- UI state ------------------------------------------------------------------------------

    private void SetIdleUi()
    {
        _startButton.Content = Ui.ButtonContent(Icons.Play, "Start", 16, Ui.Solid("#03211C"));
        Ui.Skin(_startButton, Ui.ButtonKind.Accent);
        _startButton.IsEnabled = _runner is not null;
        AutomationProperties.SetName(_startButton, "Start task");
        _composer.IsReadOnly = false;
        _micButton.IsEnabled = _services.Microphone is not null;
    }

    private void SetRunningUi()
    {
        _startButton.Content = Ui.ButtonContent(Icons.Stop, "Stop", 16, Brushes.White);
        Ui.Skin(_startButton, Ui.ButtonKind.Danger);
        AutomationProperties.SetName(_startButton, "Stop task");
        _composer.IsReadOnly = true;
        _micButton.IsEnabled = false;
    }

    private void SetListeningUi(bool listening)
    {
        Ui.Skin(_micButton, listening ? Ui.ButtonKind.Danger : Ui.ButtonKind.Subtle);
        _micButton.Content = Ui.Icon(listening ? Icons.Stop : Icons.Mic, listening ? 16 : 20, Brushes.White);
        ToolTip.SetTip(_micButton, listening ? "Finish speaking" : $"Speak a task ({_talkHotkey})");
        if (listening)
            SetStatus("Listening… speak your task.", Ui.DangerBrush);
        else if (_runner?.IsRunning != true)
            SetIdleStatus();
    }

    private void SetIdleStatus()
    {
        if (_configurationError is not null)
            SetStatus("Setup needs attention — see the message above.", Ui.WarningBrush);
        else if (_login?.IsSignedIn == true)
            SetStatus("Ready. Your mouse and keyboard are yours until you start a task.", Ui.SuccessBrush);
        else
            SetStatus("Connect your Autobots account to start.", Ui.TextTertiary);
    }

    private void SetStatus(string text, IBrush dot)
    {
        _statusText.Text = text;
        _statusDot.Background = dot;
    }

    private void RefreshLimits() =>
        _limitsText.Text = $"{_settings.MaxStepsPerTask} steps · {_settings.MaxMinutesPerTask} min";

    private void AddRunHeader(string instruction)
    {
        _timelineEmpty.IsVisible = false;
        var header = new Border
        {
            Margin = new Thickness(0, _timeline.Children.Count == 0 ? 0 : 12, 0, 4),
            Padding = new Thickness(12, 9),
            CornerRadius = new CornerRadius(10),
            Background = Ui.Solid("#122EE6C8"),
            BorderBrush = Ui.Solid("#262EE6C8"),
            BorderThickness = new Thickness(1),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                ColumnSpacing = 10,
                Children =
                {
                    Ui.Icon(Icons.Play, 14, Ui.AccentBrush),
                    WithColumn(Ui.Text(ActionNarration.Clip(instruction, 160), 13, Ui.TextPrimary, FontWeight.SemiBold), 1),
                    WithColumn(Ui.Text(DateTime.Now.ToString("t", CultureInfo.CurrentCulture), 11, Ui.TextTertiary), 2)
                }
            }
        };
        AppendTimeline(header);
    }

    private void AddTimelineRow(AgentTimelineEntry entry)
    {
        _timelineEmpty.IsVisible = false;
        var tone = entry.Tone switch
        {
            TimelineTone.Success => Ui.SuccessBrush,
            TimelineTone.Warning => Ui.WarningBrush,
            TimelineTone.Error => Ui.DangerBrush,
            _ => Ui.AccentBrush
        };
        var detail = Ui.Text(entry.Detail, 12, Ui.TextSecondary);
        detail.MaxLines = 2;
        detail.TextTrimming = TextTrimming.CharacterEllipsis;
        detail.IsVisible = !string.IsNullOrWhiteSpace(entry.Detail);
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 12,
            Margin = new Thickness(4, 6),
            Children =
            {
                new Border
                {
                    Width = 30,
                    Height = 30,
                    CornerRadius = new CornerRadius(15),
                    Background = Ui.Solid("#14FFFFFF"),
                    VerticalAlignment = VerticalAlignment.Top,
                    Child = Ui.Icon(entry.Glyph, 14, tone)
                },
                WithColumn(new StackPanel { Spacing = 1, Children = { Ui.Text(entry.Title, 13, Ui.TextPrimary, FontWeight.SemiBold), detail } }, 1),
                WithColumn(Ui.Text(entry.At.ToLocalTime().ToString("T", CultureInfo.CurrentCulture), 11, Ui.TextTertiary), 2)
            }
        };
        AppendTimeline(row);
    }

    private void AppendTimeline(Control row)
    {
        _timeline.Children.Add(row);
        while (_timeline.Children.Count > MaxTimelineRows)
            _timeline.Children.RemoveAt(0);
        Dispatcher.UIThread.Post(() => _timelineScroll.ScrollToEnd(), DispatcherPriority.Background);
    }

    // ---- Settings sheet ------------------------------------------------------------------------

    private Border BuildSettingsSheet()
    {
        var stepsValue = Ui.Text(string.Empty, 13, Ui.TextPrimary, FontWeight.SemiBold);
        var stepsSlider = new Slider { Minimum = AppSettings.MinSteps, Maximum = AppSettings.MaxSteps, TickFrequency = 5, IsSnapToTickEnabled = true, Value = _settings.MaxStepsPerTask };
        stepsValue.Text = $"{_settings.MaxStepsPerTask}";
        stepsSlider.PropertyChanged += (_, args) =>
        {
            if (args.Property != RangeBase.ValueProperty)
                return;
            UpdateSettings(_settings with { MaxStepsPerTask = (int)stepsSlider.Value });
            stepsValue.Text = $"{_settings.MaxStepsPerTask}";
        };

        var minutesValue = Ui.Text($"{_settings.MaxMinutesPerTask} min", 13, Ui.TextPrimary, FontWeight.SemiBold);
        var minutesSlider = new Slider { Minimum = AppSettings.MinMinutes, Maximum = AppSettings.MaxMinutes, TickFrequency = 1, IsSnapToTickEnabled = true, Value = _settings.MaxMinutesPerTask };
        minutesSlider.PropertyChanged += (_, args) =>
        {
            if (args.Property != RangeBase.ValueProperty)
                return;
            UpdateSettings(_settings with { MaxMinutesPerTask = (int)minutesSlider.Value });
            minutesValue.Text = $"{_settings.MaxMinutesPerTask} min";
        };

        var speedOptions = new[] { ("Relaxed", 1.4), ("Normal", 1.0), ("Quick", 0.65) };
        var speedPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var speedButtons = new List<ToggleButton>();
        foreach (var (label, value) in speedOptions)
        {
            var toggle = new ToggleButton
            {
                Content = label,
                IsChecked = Math.Abs(_settings.PointerSpeed - value) < 0.05,
                Padding = new Thickness(14, 6),
                CornerRadius = new CornerRadius(14),
                FontSize = 12.5
            };
            toggle.Click += (_, _) =>
            {
                foreach (var other in speedButtons)
                    other.IsChecked = ReferenceEquals(other, toggle);
                UpdateSettings(_settings with { PointerSpeed = value });
            };
            speedButtons.Add(toggle);
            speedPanel.Children.Add(toggle);
        }

        var language = new ComboBox
        {
            ItemsSource = VoiceLanguages,
            SelectedItem = _settings.VoiceLanguage is { } saved && VoiceLanguages.Contains(saved) ? saved : VoiceLanguages[0],
            MinWidth = 180
        };
        language.SelectionChanged += (_, _) =>
            UpdateSettings(_settings with { VoiceLanguage = language.SelectedItem as string is { } value && value != VoiceLanguages[0] ? value : null });

        var closeButton = Ui.Skin(new Button { Content = Ui.Icon(Icons.Close, 16, Ui.TextPrimary), Padding = new Thickness(8), CornerRadius = new CornerRadius(16) }, Ui.ButtonKind.Ghost);
        closeButton.Click += (_, _) => ToggleSettings(false);
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(Ui.Text("Settings", 20, Ui.TextPrimary, FontWeight.SemiBold, display: true));
        header.Children.Add(WithColumn(closeButton, 1));

        var content = new StackPanel
        {
            Spacing = 18,
            Children =
            {
                header,
                Section("Task limits",
                    LabeledRow("Steps per task", stepsValue), stepsSlider,
                    LabeledRow("Minutes per task", minutesValue), minutesSlider,
                    Ui.Text("Autobots pauses when either limit is reached. The service also enforces its own AI spending limits.", 12, Ui.TextTertiary)),
                Section("Pointer",
                    Ui.Text("Pointer speed", 13, Ui.TextSecondary), speedPanel,
                    ToggleRow("Show the Autobots cursor halo", _settings.ShowPointerHalo, value => UpdateSettings(_settings with { ShowPointerHalo = value }))),
                Section("Voice",
                    ToggleRow("Start spoken tasks automatically after a 3-second countdown", _settings.AutoStartVoiceTasks, value => UpdateSettings(_settings with { AutoStartVoiceTasks = value })),
                    ToggleRow("Stop listening when I pause", _settings.StopListeningOnSilence, value => UpdateSettings(_settings with { StopListeningOnSilence = value })),
                    LabeledRow("Spoken language", language)),
                Section("App",
                    ToggleRow("Keep running in the tray when the window is closed", _settings.KeepRunningInTray, value => UpdateSettings(_settings with { KeepRunningInTray = value }))),
                Ui.Text("Screenshots and voice clips are sent only while you use Autobots and are never saved on this PC or the service.", 12, Ui.TextTertiary)
            }
        };

        return new Border
        {
            Width = 400,
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = Ui.Solid("#F5121A24"),
            BorderBrush = Ui.CardStroke,
            BorderThickness = new Thickness(1, 0, 0, 0),
            BoxShadow = BoxShadows.Parse("-20 0 40 0 #66000000"),
            IsVisible = false,
            Child = new ScrollViewer { Content = new Border { Padding = new Thickness(24, 20), Child = content } }
        };
    }

    private void ToggleSettings(bool open)
    {
        _settingsSheet.IsVisible = open;
        _settingsScrim.IsVisible = open;
    }

    private void UpdateSettings(AppSettings settings)
    {
        _settings = settings.Normalized();
        _settings.Save();
        RefreshLimits();
    }

    // ---- Small builders ------------------------------------------------------------------------

    private static Control Section(string title, params Control[] children)
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(Ui.Text(title.ToUpperInvariant(), 11, Ui.AccentBrush, FontWeight.Bold));
        foreach (var child in children)
            panel.Children.Add(child);
        return new Border
        {
            Background = Ui.CardFill,
            BorderBrush = Ui.CardStroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 14),
            Child = panel
        };
    }

    private static Control LabeledRow(string label, Control value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        var text = Ui.Text(label, 13, Ui.TextSecondary);
        text.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(text);
        grid.Children.Add(WithColumn(value, 1));
        return grid;
    }

    private static Control ToggleRow(string label, bool value, Action<bool> changed)
    {
        var toggle = new ToggleSwitch { IsChecked = value, OnContent = string.Empty, OffContent = string.Empty, MinWidth = 0 };
        toggle.IsCheckedChanged += (_, _) => changed(toggle.IsChecked == true);
        var text = Ui.Text(label, 13, Ui.TextSecondary);
        text.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        grid.Children.Add(text);
        grid.Children.Add(WithColumn(toggle, 1));
        return grid;
    }

    private static Control GuideRow(string icon, string title, string detail)
    {
        var detailText = Ui.Text(detail, 12, Ui.TextSecondary);
        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 12,
            Children =
            {
                new Border
                {
                    Width = 30,
                    Height = 30,
                    CornerRadius = new CornerRadius(9),
                    Background = Ui.Solid("#162EE6C8"),
                    VerticalAlignment = VerticalAlignment.Top,
                    Child = Ui.Icon(icon, 15, Ui.AccentBrush)
                },
                WithColumn(new StackPanel { Spacing = 2, Children = { Ui.Text(title, 13, Ui.TextPrimary, FontWeight.SemiBold), detailText } }, 1)
            }
        };
    }

    private static Control Banner(string icon, string title, string detail, IBrush tone) => new Border
    {
        Background = Ui.Solid("#1AFFC857"),
        BorderBrush = Ui.Solid("#40FFC857"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(14, 12),
        Child = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 12,
            Children =
            {
                Ui.Icon(icon, 18, tone),
                WithColumn(new StackPanel { Spacing = 2, Children = { Ui.Text(title, 13, Ui.TextPrimary, FontWeight.SemiBold), Ui.Text(detail, 12, Ui.TextSecondary) } }, 1)
            }
        }
    };

    /// <summary>A soft accent glow behind the hero area for depth.</summary>
    private static Control AmbientGlow() => new Border
    {
        IsHitTestVisible = false,
        Background = new RadialGradientBrush
        {
            Center = new RelativePoint(0.18, 0.02, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.18, 0.02, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.75, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.6, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.Parse("#222EE6C8"), 0), new GradientStop(Color.Parse("#0027B4F5"), 1) }
        }
    };

    private static TextBlock CenteredText(string text, double size, IBrush brush)
    {
        var block = Ui.Text(text, size, brush);
        block.TextAlignment = TextAlignment.Center;
        return block;
    }

    private static T WithColumn<T>(T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }

    private static string Greeting()
    {
        var hour = DateTime.Now.Hour;
        return hour < 12 ? "Good morning" : hour < 17 ? "Good afternoon" : "Good evening";
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalMinutes >= 1 ? $"{(int)duration.TotalMinutes}m {duration.Seconds}s" : $"{Math.Max(1, (int)duration.TotalSeconds)}s";

    private static Bitmap? LoadBrandBitmap()
    {
        using var stream = typeof(ShellWindow).Assembly.GetManifestResourceStream("Autobots.Desktop.Assets.AutobotsIcon.png");
        return stream is null ? null : new Bitmap(stream);
    }
}
