using System.Text.Json;

namespace Autobots.Desktop;

/// <summary>
/// Owner preferences stored in the local app-data folder. No credentials, instructions or screen
/// content are written here.
/// </summary>
public sealed record AppSettings
{
    public const int MinSteps = 5;
    public const int MaxSteps = 200;
    public const int MinMinutes = 1;
    public const int MaxMinutes = 30;

    public int MaxStepsPerTask { get; init; } = 100;
    public int MaxMinutesPerTask { get; init; } = 10;
    /// <summary>Pointer travel speed multiplier; 1 is the default pace.</summary>
    public double PointerSpeed { get; init; } = 1.0;
    public bool ShowPointerHalo { get; init; } = true;
    public bool AutoStartVoiceTasks { get; init; } = true;
    public bool StopListeningOnSilence { get; init; } = true;
    public bool KeepRunningInTray { get; init; } = true;
    public string? VoiceLanguage { get; init; }

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Origin Studios", "Autobots", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return (JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? new AppSettings()).Normalized();
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            // Fall back to defaults when preferences are missing or unreadable.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Normalized(), JsonOptions));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Preferences are a convenience; the current session keeps working without them.
        }
    }

    public AppSettings Normalized() => this with
    {
        MaxStepsPerTask = Math.Clamp(MaxStepsPerTask, MinSteps, MaxSteps),
        MaxMinutesPerTask = Math.Clamp(MaxMinutesPerTask, MinMinutes, MaxMinutes),
        PointerSpeed = Math.Clamp(PointerSpeed, 0.5, 1.6),
        VoiceLanguage = string.IsNullOrWhiteSpace(VoiceLanguage) ? null : VoiceLanguage.Trim()
    };
}
