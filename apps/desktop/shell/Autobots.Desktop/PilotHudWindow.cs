using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Autobots.Desktop;

public enum HudTone
{
    Active,
    Listening,
    Success,
    Warning,
    Error
}

/// <summary>
/// The floating pilot bar shown while Autobots listens or works. It stays on top without taking focus
/// from the app being controlled, moves out of the way of pointer targets and is hidden for screenshots.
/// </summary>
public sealed class PilotHudWindow : Window
{
    private const double HudWidth = 640;
    private const int LevelBars = 32;
    private static readonly PixelPoint OffScreen = new(-32000, -32000);

    private readonly Border _card;
    private readonly Grid _orb;
    private readonly Ellipse _orbGlow;
    private readonly Arc _orbArc;
    private readonly RotateTransform _orbRotation = new();
    private readonly ContentControl _orbIcon = new();
    private readonly TextBlock _title;
    private readonly TextBlock _detail;
    private readonly Border _stepPill;
    private readonly TextBlock _stepText;
    private readonly StackPanel _levelMeter;
    private readonly double[] _levels = new double[LevelBars];
    private readonly StackPanel _actions;
    private readonly DispatcherTimer _animation;
    private readonly DispatcherTimer _autoHide;
    private HudTone _tone = HudTone.Active;
    private bool _spinning;
    private bool _anchorTop;
    private bool _hiddenForCapture;
    private bool _userVisible;
    private double _phase;
    private double _level;

    public PilotHudWindow()
    {
        Title = "Autobots pilot bar";
        Width = HudWidth + 40;
        SizeToContent = SizeToContent.Height;
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = OffScreen;

        _orbGlow = new Ellipse { Width = 44, Height = 44, Fill = Ui.AccentGradient(), Opacity = 0.22 };
        _orbArc = new Arc
        {
            Width = 44,
            Height = 44,
            StartAngle = 0,
            SweepAngle = 110,
            Stroke = Ui.AccentGradient(),
            StrokeThickness = 3,
            StrokeLineCap = PenLineCap.Round,
            RenderTransform = _orbRotation,
            RenderTransformOrigin = RelativePoint.Center
        };
        _orb = new Grid
        {
            Width = 44,
            Height = 44,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                _orbGlow,
                new Ellipse { Width = 34, Height = 34, Fill = Ui.Solid("#E6101A26"), Stroke = Ui.Solid("#33FFFFFF"), StrokeThickness = 1 },
                _orbArc,
                _orbIcon
            }
        };

        _title = Ui.Text("Autobots", 15, Ui.TextPrimary, FontWeight.SemiBold);
        _title.TextWrapping = TextWrapping.NoWrap;
        _title.TextTrimming = TextTrimming.CharacterEllipsis;
        _title.MaxLines = 1;
        _detail = Ui.Text(string.Empty, 12.5, Ui.TextSecondary);
        _detail.TextTrimming = TextTrimming.CharacterEllipsis;
        _detail.MaxLines = 2;

        _levelMeter = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Height = 22, IsVisible = false, Margin = new Thickness(0, 6, 0, 0) };
        for (var index = 0; index < LevelBars; index++)
        {
            _levelMeter.Children.Add(new Border
            {
                Width = 4,
                Height = 3,
                CornerRadius = new CornerRadius(2),
                VerticalAlignment = VerticalAlignment.Center,
                Background = Ui.AccentGradient()
            });
        }

        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { _title, _detail, _levelMeter } };
        _stepText = Ui.Text(string.Empty, 11.5, Ui.TextSecondary, FontWeight.SemiBold);
        _stepText.TextWrapping = TextWrapping.NoWrap;
        _stepPill = new Border
        {
            Background = Ui.Solid("#14FFFFFF"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(9, 4),
            VerticalAlignment = VerticalAlignment.Center,
            Child = _stepText,
            IsVisible = false
        };
        _actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };

        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 14 };
        Grid.SetColumn(_orb, 0);
        Grid.SetColumn(text, 1);
        Grid.SetColumn(_stepPill, 2);
        Grid.SetColumn(_actions, 3);
        layout.Children.Add(_orb);
        layout.Children.Add(text);
        layout.Children.Add(_stepPill);
        layout.Children.Add(_actions);

        _card = new Border
        {
            Width = HudWidth,
            Background = Ui.Solid("#F20D141E"),
            BorderBrush = Ui.Solid("#2EFFFFFF"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(24),
            Padding = new Thickness(14, 12, 12, 12),
            BoxShadow = BoxShadows.Parse("0 14 38 0 #99000000"),
            Child = layout
        };
        Content = new Border { Padding = new Thickness(20, 12, 20, 26), Child = _card };

        _animation = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _animation.Tick += (_, _) => Animate();
        _autoHide = new DispatcherTimer();
        _autoHide.Tick += (_, _) =>
        {
            _autoHide.Stop();
            HideHud();
        };
        Resized += (_, _) => Reposition();
        Opened += (_, _) =>
        {
            if (TryGetPlatformHandle()?.Handle is { } handle)
                NativeWindowTraits.MakeFloatingPanel(handle);
        };
    }

    public event EventHandler? StopRequested;
    public event EventHandler? ResumeRequested;
    public event EventHandler? FinishListeningRequested;
    public event EventHandler? CancelRequested;
    public event EventHandler? StartNowRequested;
    public event EventHandler? EditRequested;
    public event EventHandler? OpenAppRequested;

    public bool IsShowing => _userVisible;

    public void ShowWorking(string title, string detail, int step, int maxSteps, bool spinning)
    {
        _autoHide.Stop();
        SetTone(HudTone.Active, Icons.Sparkle, spinning);
        _title.Text = title;
        _detail.Text = detail;
        _detail.IsVisible = !string.IsNullOrWhiteSpace(detail);
        _stepText.Text = step > 0 ? $"Step {Math.Min(step, maxSteps)} of {maxSteps}" : "Starting";
        _stepPill.IsVisible = true;
        _levelMeter.IsVisible = false;
        SetActions(("Stop", Icons.Stop, Ui.ButtonKind.Danger, () => StopRequested?.Invoke(this, EventArgs.Empty)));
        Reveal();
    }

    public void ShowWaitingForOwner(string detail, int step, int maxSteps)
    {
        _autoHide.Stop();
        SetTone(HudTone.Warning, Icons.Warning, spinning: false);
        _title.Text = "Waiting for you";
        _detail.Text = detail;
        _detail.IsVisible = true;
        _stepText.Text = $"Step {Math.Min(step, maxSteps)} of {maxSteps}";
        _stepPill.IsVisible = true;
        _levelMeter.IsVisible = false;
        SetActions(
            ("Resume", Icons.Play, Ui.ButtonKind.Accent, () => ResumeRequested?.Invoke(this, EventArgs.Empty)),
            ("Stop", Icons.Stop, Ui.ButtonKind.Danger, () => StopRequested?.Invoke(this, EventArgs.Empty)));
        Reveal();
    }

    public void ShowListening(string hotkey)
    {
        _autoHide.Stop();
        SetTone(HudTone.Listening, Icons.Mic, spinning: false);
        _title.Text = "Listening…";
        _detail.Text = $"Say what you'd like done. Press {hotkey} or Done when you finish.";
        _detail.IsVisible = true;
        _stepPill.IsVisible = false;
        Array.Clear(_levels);
        _levelMeter.IsVisible = true;
        SetActions(
            ("Done", Icons.Check, Ui.ButtonKind.Accent, () => FinishListeningRequested?.Invoke(this, EventArgs.Empty)),
            (null, Icons.Close, Ui.ButtonKind.Ghost, () => CancelRequested?.Invoke(this, EventArgs.Empty)));
        Reveal();
    }

    public void SetLevel(double level) => _level = Math.Clamp(level, 0, 1);

    public void ShowTranscribing()
    {
        _autoHide.Stop();
        SetTone(HudTone.Active, Icons.Sparkle, spinning: true);
        _title.Text = "Understanding what you said…";
        _detail.Text = "Transcribing with Gemini";
        _detail.IsVisible = true;
        _stepPill.IsVisible = false;
        _levelMeter.IsVisible = false;
        SetActions((null, Icons.Close, Ui.ButtonKind.Ghost, () => CancelRequested?.Invoke(this, EventArgs.Empty)));
        Reveal();
    }

    public void ShowConfirm(string transcript, int secondsLeft, bool autoStart)
    {
        _autoHide.Stop();
        SetTone(HudTone.Active, Icons.Mic, spinning: false);
        _title.Text = $"“{transcript}”";
        _detail.Text = autoStart ? $"Starting in {secondsLeft}… your mouse and keyboard will be used." : "Start when you're ready.";
        _detail.IsVisible = true;
        _stepPill.IsVisible = false;
        _levelMeter.IsVisible = false;
        SetActions(
            ("Start", Icons.Play, Ui.ButtonKind.Accent, () => StartNowRequested?.Invoke(this, EventArgs.Empty)),
            (null, Icons.Edit, Ui.ButtonKind.Ghost, () => EditRequested?.Invoke(this, EventArgs.Empty)),
            (null, Icons.Close, Ui.ButtonKind.Ghost, () => CancelRequested?.Invoke(this, EventArgs.Empty)));
        Reveal();
    }

    public void ShowResult(HudTone tone, string title, string detail, TimeSpan visibleFor)
    {
        SetTone(tone, tone switch
        {
            HudTone.Success => Icons.Check,
            HudTone.Warning => Icons.Warning,
            HudTone.Error => Icons.Warning,
            _ => Icons.Sparkle
        }, spinning: false);
        _title.Text = title;
        _detail.Text = detail;
        _detail.IsVisible = !string.IsNullOrWhiteSpace(detail);
        _stepPill.IsVisible = false;
        _levelMeter.IsVisible = false;
        SetActions(
            ("Open", Icons.Launch, Ui.ButtonKind.Subtle, () => OpenAppRequested?.Invoke(this, EventArgs.Empty)),
            (null, Icons.Close, Ui.ButtonKind.Ghost, HideHud));
        Reveal();
        _autoHide.Interval = visibleFor;
        _autoHide.Start();
    }

    public void HideHud()
    {
        _autoHide.Stop();
        _userVisible = false;
        _animation.Stop();
        if (IsVisible)
            Hide();
    }

    /// <summary>Moves the bar off-screen for the instant Autobots captures its own observation.</summary>
    public void SetHiddenForCapture(bool hidden)
    {
        if (_hiddenForCapture == hidden)
            return;
        _hiddenForCapture = hidden;
        if (!_userVisible || !IsVisible)
            return;
        if (hidden)
            Position = OffScreen;
        else
            Reposition();
    }

    /// <summary>Moves the bar to the other edge when a pointer target lies underneath it.</summary>
    public bool MoveAwayFrom(int desktopX, int desktopY)
    {
        if (!_userVisible || !IsVisible || _hiddenForCapture)
            return false;
        var scaling = RenderScaling;
        var margin = (int)(24 * scaling);
        var left = Position.X - margin;
        var top = Position.Y - margin;
        var right = Position.X + (int)(Bounds.Width * scaling) + margin;
        var bottom = Position.Y + (int)(Bounds.Height * scaling) + margin;
        if (desktopX < left || desktopX > right || desktopY < top || desktopY > bottom)
            return false;
        _anchorTop = !_anchorTop;
        Reposition();
        return true;
    }

    private void Reveal()
    {
        _userVisible = true;
        if (!IsVisible)
            Show();
        Reposition();
        if (!_animation.IsEnabled)
            _animation.Start();
    }

    private void Reposition()
    {
        if (!_userVisible || _hiddenForCapture)
            return;
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is null)
            return;
        var area = screen.WorkingArea;
        var scaling = screen.Scaling;
        var width = (int)((Bounds.Width > 0 ? Bounds.Width : Width) * scaling);
        var height = (int)((Bounds.Height > 0 ? Bounds.Height : 120) * scaling);
        var x = area.X + ((area.Width - width) / 2);
        var y = _anchorTop ? area.Y + (int)(4 * scaling) : area.Bottom - height;
        Position = new PixelPoint(x, y);
    }

    private void SetTone(HudTone tone, string icon, bool spinning)
    {
        _tone = tone;
        _spinning = spinning;
        var accent = tone switch
        {
            HudTone.Listening => Ui.DangerBrush,
            HudTone.Success => Ui.SuccessBrush,
            HudTone.Warning => Ui.WarningBrush,
            HudTone.Error => Ui.DangerBrush,
            _ => Ui.AccentGradient()
        };
        _orbGlow.Fill = accent;
        _orbArc.Stroke = accent;
        _orbArc.IsVisible = spinning;
        _orbIcon.Content = tone == HudTone.Active ? Ui.BrandMark(32) : Ui.Icon(icon, 18, accent);
        _card.BorderBrush = tone switch
        {
            HudTone.Listening => Ui.Solid("#66F0525C"),
            HudTone.Success => Ui.Solid("#553DDC97"),
            HudTone.Warning or HudTone.Error => Ui.Solid("#55FFC857"),
            _ => Ui.Solid("#402EE6C8")
        };
    }

    private void SetActions(params (string? Label, string Icon, Ui.ButtonKind Kind, Action OnClick)[] actions)
    {
        _actions.Children.Clear();
        foreach (var (label, icon, kind, onClick) in actions)
        {
            var foreground = kind == Ui.ButtonKind.Accent ? Ui.Solid("#03211C") : Brushes.White;
            var button = Ui.Skin(new Button
            {
                Content = Ui.ButtonContent(icon, label, 14, foreground),
                Padding = label is null ? new Thickness(9) : new Thickness(14, 8),
                CornerRadius = new CornerRadius(18),
                MinHeight = 36,
                MinWidth = 36,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Focusable = false
            }, kind);
            AutomationProperties.SetName(button, label ?? (icon == Icons.Close ? "Dismiss" : icon == Icons.Edit ? "Edit in Autobots" : "Pilot action"));
            if (label is null)
                ToolTip.SetTip(button, icon == Icons.Close ? "Dismiss" : icon == Icons.Edit ? "Edit in Autobots" : null);
            button.Click += (_, _) => onClick();
            _actions.Children.Add(button);
        }
    }

    private void Animate()
    {
        _phase += 0.033;
        var pulse = (Math.Sin(_phase * 3.2) + 1) / 2;
        _orbGlow.Opacity = _tone == HudTone.Listening ? 0.25 + (_level * 0.6) : 0.16 + (pulse * 0.2);
        var scale = _tone == HudTone.Listening ? 0.85 + (_level * 0.35) : 0.9 + (pulse * 0.1);
        _orbGlow.RenderTransform = new ScaleTransform(scale, scale);
        if (_spinning)
            _orbRotation.Angle = (_orbRotation.Angle + 9) % 360;

        if (_levelMeter.IsVisible)
        {
            Array.Copy(_levels, 1, _levels, 0, LevelBars - 1);
            _levels[^1] = _level;
            for (var index = 0; index < LevelBars; index++)
            {
                var value = _levels[index];
                ((Border)_levelMeter.Children[index]).Height = 3 + (value * 19);
                _levelMeter.Children[index].Opacity = 0.35 + (value * 0.65);
            }
        }
    }
}
