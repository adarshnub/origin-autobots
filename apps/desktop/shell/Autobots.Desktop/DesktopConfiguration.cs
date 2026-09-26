using System.Text.Json;

namespace Autobots.Desktop;

public sealed record DesktopConfiguration(string ApiBaseUrl, string CognitoHostedUiBaseUrl, string CognitoClientId)
{
    public const string CallbackUri = "http://127.0.0.1:53682/auth/callback";

    public static DesktopConfiguration Load()
    {
        var fileName = "autobots.dev.json";
        var paths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, fileName),
            Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory, fileName)
        };
        var path = paths.FirstOrDefault(File.Exists) ?? paths[0];
        DesktopConfiguration? configuration = File.Exists(path)
            ? JsonSerializer.Deserialize<DesktopConfiguration>(File.ReadAllText(path), JsonOptions)
            : null;

        configuration = new DesktopConfiguration(
            GetConfigurationValue("AUTOBOTS_API_BASE_URL", configuration?.ApiBaseUrl),
            GetConfigurationValue("AUTOBOTS_COGNITO_HOSTED_UI_URL", configuration?.CognitoHostedUiBaseUrl),
            GetConfigurationValue("AUTOBOTS_COGNITO_CLIENT_ID", configuration?.CognitoClientId));

        if (!Uri.TryCreate(configuration.ApiBaseUrl, UriKind.Absolute, out var apiUri) || apiUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException($"The Autobots API URL is missing or is not HTTPS. Check autobots.dev.json beside the app at {path}.");
        if (!Uri.TryCreate(configuration.CognitoHostedUiBaseUrl, UriKind.Absolute, out var hostedUiUri) || hostedUiUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException($"The sign-in service URL is missing or is not HTTPS. Check autobots.dev.json beside the app at {path}.");
        if (string.IsNullOrWhiteSpace(configuration.CognitoClientId))
            throw new InvalidOperationException($"The Autobots account client ID is missing from autobots.dev.json at {path}.");

        return configuration with
        {
            ApiBaseUrl = apiUri.ToString().TrimEnd('/'),
            CognitoHostedUiBaseUrl = hostedUiUri.ToString().TrimEnd('/'),
            CognitoClientId = configuration.CognitoClientId.Trim()
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static string GetConfigurationValue(string environmentVariable, string? fileValue)
    {
        var environmentValue = Environment.GetEnvironmentVariable(environmentVariable);
        return !string.IsNullOrWhiteSpace(environmentValue)
            ? environmentValue.Trim()
            : fileValue?.Trim() ?? string.Empty;
    }
}
