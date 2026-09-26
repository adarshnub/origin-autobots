using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Autobots.Desktop;

/// <summary>Autobots' dark, Mica-friendly palette and control skins layered over the Fluent theme.</summary>
public static class Ui
{
    private static readonly Lazy<Bitmap?> EyesBitmap = new(() => LoadArtwork("AutobotsIcon.png"));
    private static readonly Lazy<Bitmap?> CompanionBitmap = new(() => LoadArtwork("BotCompanion.png"));

    private static Bitmap? LoadArtwork(string name)
    {
        using var stream = typeof(Ui).Assembly.GetManifestResourceStream($"Autobots.Desktop.Assets.{name}");
        return stream is null ? null : new Bitmap(stream);
    }

    public static Control BrandMark(double size) => EyesBitmap.Value is { } image
        ? new Image { Source = image, Width = size, Height = size, Stretch = Stretch.Uniform }
        : Icon(Icons.Autobots, size, Solid("#E9E6FF"));

    public static Control Companion(double height) => CompanionBitmap.Value is { } image
        ? new Image { Source = image, Height = height, Width = height * 0.89, Stretch = Stretch.Uniform }
        : BrandMark(height * 0.6);

    public static readonly Color Accent = Color.Parse("#2EE6C8");
    public static readonly Color AccentBlue = Color.Parse("#27B4F5");
    public static readonly Color Danger = Color.Parse("#F0525C");
    public static readonly Color Warning = Color.Parse("#FFC857");
    public static readonly Color Success = Color.Parse("#3DDC97");

    public static readonly FontFamily TextFont = new("Segoe UI Variable Text, Segoe UI, Inter, sans-serif");
    public static readonly FontFamily DisplayFont = new("Segoe UI Variable Display, Segoe UI, Inter, sans-serif");

    public static IBrush TextPrimary { get; } = Solid("#F3F6F9");
    public static IBrush TextSecondary { get; } = Solid("#B4C0CC");
    public static IBrush TextTertiary { get; } = Solid("#7F8C99");
    public static IBrush CardFill { get; } = Solid("#0EFFFFFF");
    public static IBrush CardFillStrong { get; } = Solid("#16FFFFFF");
    public static IBrush CardStroke { get; } = Solid("#17FFFFFF");
    public static IBrush Divider { get; } = Solid("#12FFFFFF");
    public static IBrush WindowFallback { get; } = Solid("#0E131A");
    public static IBrush AccentBrush { get; } = new SolidColorBrush(Accent);
    public static IBrush DangerBrush { get; } = new SolidColorBrush(Danger);
    public static IBrush WarningBrush { get; } = new SolidColorBrush(Warning);
    public static IBrush SuccessBrush { get; } = new SolidColorBrush(Success);

    public static IBrush AccentGradient(double lighten = 0) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Lighten(Accent, lighten), 0),
            new GradientStop(Lighten(AccentBlue, lighten), 1)
        }
    };

    public static IBrush Solid(string color) => new SolidColorBrush(Color.Parse(color));

    public static Color Lighten(Color color, double amount) => amount >= 0
        ? Color.FromArgb(color.A, (byte)(color.R + ((255 - color.R) * amount)), (byte)(color.G + ((255 - color.G) * amount)), (byte)(color.B + ((255 - color.B) * amount)))
        : Color.FromArgb(color.A, (byte)(color.R * (1 + amount)), (byte)(color.G * (1 + amount)), (byte)(color.B * (1 + amount)));

    /// <summary>Application-wide Fluent overrides: accent color and softer corners.</summary>
    public static void ApplyApplicationResources(IResourceDictionary resources)
    {
        resources["SystemAccentColor"] = Accent;
        resources["SystemAccentColorLight1"] = Lighten(Accent, 0.15);
        resources["SystemAccentColorLight2"] = Lighten(Accent, 0.3);
        resources["SystemAccentColorLight3"] = Lighten(Accent, 0.45);
        resources["SystemAccentColorDark1"] = Lighten(Accent, -0.15);
        resources["SystemAccentColorDark2"] = Lighten(Accent, -0.3);
        resources["SystemAccentColorDark3"] = Lighten(Accent, -0.45);
        resources["ControlCornerRadius"] = new CornerRadius(8);
        resources["OverlayCornerRadius"] = new CornerRadius(12);
    }

    public enum ButtonKind
    {
        Accent,
        Subtle,
        Ghost,
        Danger,
        Chip
    }

    /// <summary>Skins a Fluent button for every interaction state by overriding its theme resources.</summary>
    public static T Skin<T>(T button, ButtonKind kind) where T : Button
    {
        var (normal, hover, pressed, foreground, border) = kind switch
        {
            ButtonKind.Accent => (AccentGradient(), AccentGradient(0.12), AccentGradient(-0.12), Solid("#03211C"), (IBrush)Brushes.Transparent),
            ButtonKind.Danger => (new SolidColorBrush(Danger), new SolidColorBrush(Lighten(Danger, 0.1)), new SolidColorBrush(Lighten(Danger, -0.12)), Solid("#FFFFFF"), Brushes.Transparent),
            ButtonKind.Ghost => (Brushes.Transparent, Solid("#14FFFFFF"), Solid("#0AFFFFFF"), Solid("#D5DEE7"), Brushes.Transparent),
            ButtonKind.Chip => (Solid("#0CFFFFFF"), Solid("#1AFFFFFF"), Solid("#08FFFFFF"), Solid("#DCE5EE"), Solid("#1CFFFFFF")),
            _ => (Solid("#12FFFFFF"), Solid("#1DFFFFFF"), Solid("#0BFFFFFF"), Solid("#F1F5F9"), Solid("#16FFFFFF"))
        };
        var resources = button.Resources;
        resources["ButtonBackground"] = normal;
        resources["ButtonBackgroundPointerOver"] = hover;
        resources["ButtonBackgroundPressed"] = pressed;
        resources["ButtonBackgroundDisabled"] = Solid("#08FFFFFF");
        resources["ButtonForeground"] = foreground;
        resources["ButtonForegroundPointerOver"] = foreground;
        resources["ButtonForegroundPressed"] = foreground;
        resources["ButtonForegroundDisabled"] = Solid("#5E6B78");
        resources["ButtonBorderBrush"] = border;
        resources["ButtonBorderBrushPointerOver"] = border;
        resources["ButtonBorderBrushPressed"] = border;
        resources["ButtonBorderBrushDisabled"] = Brushes.Transparent;
        button.Background = normal;
        button.Foreground = foreground;
        button.BorderBrush = border;
        button.BorderThickness = new Thickness(1);
        button.FontFamily = TextFont;
        button.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
        if (kind == ButtonKind.Chip)
            button.CornerRadius = new CornerRadius(16);
        return button;
    }

    /// <summary>A borderless, transparent text box for use inside a composer card.</summary>
    public static TextBox SkinComposer(TextBox textBox)
    {
        var resources = textBox.Resources;
        foreach (var state in new[] { string.Empty, "PointerOver", "Focused", "Disabled" })
        {
            resources["TextControlBackground" + state] = Brushes.Transparent;
            resources["TextControlBorderBrush" + state] = Brushes.Transparent;
            resources["TextControlForeground" + state] = TextPrimary;
            resources["TextControlPlaceholderForeground" + state] = TextTertiary;
        }
        resources["TextControlBorderThemeThickness"] = new Thickness(0);
        resources["TextControlBorderThemeThicknessFocused"] = new Thickness(0);
        textBox.Background = Brushes.Transparent;
        textBox.BorderThickness = new Thickness(0);
        textBox.Foreground = TextPrimary;
        textBox.PlaceholderForeground = TextTertiary;
        textBox.CaretBrush = AccentBrush;
        textBox.SelectionBrush = new SolidColorBrush(Color.FromArgb(0x66, Accent.R, Accent.G, Accent.B));
        textBox.FontFamily = TextFont;
        return textBox;
    }

    public static Control Icon(string data, double size, IBrush? fill = null) => new Viewbox
    {
        Width = size,
        Height = size,
        Stretch = Stretch.Uniform,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        Child = new Canvas
        {
            Width = 24,
            Height = 24,
            Children = { new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(data), Fill = fill ?? TextPrimary } }
        }
    };

    public static TextBlock Text(string text, double size = 13, IBrush? foreground = null, FontWeight? weight = null, bool display = false) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = foreground ?? TextPrimary,
        FontWeight = weight ?? FontWeight.Normal,
        FontFamily = display ? DisplayFont : TextFont,
        TextWrapping = TextWrapping.Wrap
    };

    public static Border Card(Control child, double padding = 20, IBrush? fill = null) => new()
    {
        Background = fill ?? CardFill,
        BorderBrush = CardStroke,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(14),
        Padding = new Thickness(padding),
        Child = child
    };

    public static Control ButtonContent(string? icon, string? label, double iconSize = 16, IBrush? iconFill = null)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        if (icon is not null)
            panel.Children.Add(Icon(icon, iconSize, iconFill ?? Brushes.White));
        if (label is not null)
            panel.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, FontFamily = TextFont });
        return panel;
    }
}
