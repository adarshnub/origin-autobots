using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Autobots.Desktop;

/// <summary>Private owner usage. Cloud invoices and model estimates remain separate.</summary>
public sealed class UsageWindow : Window
{
    private readonly AutobotsApiClient _api;
    private readonly CognitoOAuthLogin _login;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly StackPanel _body = new() { Spacing = 14 };
    private readonly Button _refresh;
    private readonly TextBlock _status = Ui.Text("Loading usage…", 12, Ui.TextSecondary);

    public UsageWindow(AutobotsApiClient api, CognitoOAuthLogin login)
    {
        _api = api;
        _login = login;
        Title = "Autobots — Usage & costs";
        Width = 840;
        Height = 720;
        MinWidth = 640;
        MinHeight = 480;
        Background = Ui.WindowFallback;
        FontFamily = Ui.TextFont;
        RequestedThemeVariant = ThemeVariant.Dark;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _refresh = Ui.Skin(new Button { Content = "Refresh", Padding = new Thickness(14, 8) }, Ui.ButtonKind.Subtle);
        AutomationProperties.SetName(_refresh, "Refresh usage");
        _refresh.Click += async (_, _) => await LoadAsync();
        var close = Ui.Skin(new Button { Content = "Done", Padding = new Thickness(14, 8) }, Ui.ButtonKind.Accent);
        close.Click += (_, _) => Close();
        var heading = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 20) };
        heading.Children.Add(Ui.Text("Usage & costs", 28, Ui.TextPrimary, FontWeight.SemiBold));
        heading.Children.Add(Ui.Text("Your AI requests, budgets and cloud billing in one place.", 14, Ui.TextSecondary));
        heading.Children.Add(_status);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        footer.Children.Add(_refresh);
        footer.Children.Add(close);
        var layout = new DockPanel { Margin = new Thickness(28) };
        DockPanel.SetDock(heading, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(heading);
        layout.Children.Add(footer);
        layout.Children.Add(new ScrollViewer { Content = _body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        Content = layout;
        Opened += async (_, _) => await LoadAsync();
        Closed += (_, _) => { _lifetime.Cancel(); _lifetime.Dispose(); };
    }

    private async Task LoadAsync()
    {
        _refresh.IsEnabled = false;
        _status.Text = "Loading usage…";
        try
        {
            var token = await _login.GetAccessTokenAsync(_lifetime.Token);
            var report = await _api.GetUsageAsync(token, _lifetime.Token);
            _body.Children.Clear();
            _status.Text = $"UTC month {Value(report, "utc_month")} · updated {Value(report, "as_of")}";
            foreach (var channel in report.GetProperty("channels").EnumerateArray())
            {
                var name = Value(channel, "operation") == "speech" ? "GCP · Voice transcription" : "GCP · Desktop AI";
                Card(name, $"{Money(channel, "day_budget_accounted_usd")} today / {Money(channel, "day_limit_usd")} daily limit",
                    $"{Money(channel, "month_budget_accounted_usd")} this month / {Money(channel, "month_limit_usd")} monthly limit\n" +
                    $"{Value(channel, "day_requests")} requests today · {Value(channel, "month_requests")} this month · {Value(channel, "pending_requests")} pending\nBudget accounting estimate, including reserved requests.");
            }
            foreach (var bill in report.GetProperty("billing").EnumerateArray())
                Card($"{Value(bill, "provider").ToUpperInvariant()} · Reported cloud spend",
                    Value(bill, "status") == "available" ? $"{Money(bill, "amount")} month to date" : "Billing data unavailable",
                    $"{Value(bill, "scope")}\n{Value(bill, "note")}\nReported at: {Value(bill, "as_of")}");
            _body.Children.Add(Ui.Text("Request details · this UTC month", 18, Ui.TextPrimary, FontWeight.SemiBold));
            var models = report.GetProperty("models");
            if (models.GetArrayLength() == 0)
                _body.Children.Add(Ui.Text("Detailed tracking is ready. Your next AI request will appear here.", 13, Ui.TextSecondary));
            foreach (var model in models.EnumerateArray())
                Card($"{Value(model, "provider").ToUpperInvariant()} · {Value(model, "operation")} · {Value(model, "model")}",
                    $"{Value(model, "requests")} requests · {Money(model, "estimated_usd")} known estimated cost",
                    $"{Value(model, "input_tokens")} input tokens · {Value(model, "output_tokens")} output tokens\n" +
                    $"{Value(model, "failed_requests")} failed/blocked · {Value(model, "unknown_cost_requests")} with unknown cost");
            Card("Other APIs", "No other billable AI provider configured", Value(report.GetProperty("other_apis"), "note"));
            _body.Children.Add(Ui.Text(Value(report, "note"), 12, Ui.TextSecondary));
        }
        catch (OperationCanceledException) { if (!_lifetime.IsCancellationRequested) _status.Text = "Usage request timed out. Refresh to try again."; }
        catch (Exception error) when (error is HttpRequestException or InvalidOperationException or JsonException)
        {
            _status.Text = "Usage could not be loaded. Check your connection and refresh.";
        }
        finally { _refresh.IsEnabled = true; }
    }

    private void Card(string title, string value, string detail)
    {
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(Ui.Text(title, 13, Ui.AccentBrush, FontWeight.SemiBold));
        content.Children.Add(Ui.Text(value, 19, Ui.TextPrimary, FontWeight.SemiBold));
        content.Children.Add(Ui.Text(detail, 12.5, Ui.TextSecondary));
        _body.Children.Add(new Border { Background = Ui.CardFill, BorderBrush = Ui.CardStroke, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(18), Child = content });
    }

    private static string Value(JsonElement item, string key) => item.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : "Not available";
    private static string Money(JsonElement item, string key) => decimal.TryParse(Value(item, key), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? "$" + value.ToString("0.0000", CultureInfo.InvariantCulture) : "Not available";
}
