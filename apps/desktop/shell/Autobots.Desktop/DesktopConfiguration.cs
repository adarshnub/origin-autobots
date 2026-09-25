using System.Text.Json;

namespace Autobots.Desktop;

public sealed record DesktopConfiguration(string ApiBaseUrl, string CognitoHostedUiBaseUrl, string CognitoClientId)
{
    public const string CallbackUri = "http://127.0.0.1:53682/auth/callback";

    public static DesktopConfiguration Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "autobots.dev.json");
        DesktopConfiguration? configuration = File.Exists(path)
            ? JsonSerializer.Deserialize<DesktopConfiguration>(File.ReadAllText(path), JsonOptions)
            : null;

        configuration = new DesktopConfiguration(
            Environment.GetEnvironmentVariable("AUTOBOTS_API_BASE_URL") ?? configuration?.ApiBaseUrl ?? string.Empty,
            Environment.GetEnvironmentVariable("AUTOBOTS_COGNITO_HOSTED_UI_URL") ?? configuration?.CognitoHostedUiBaseUrl ?? string.Empty,
            Environment.GetEnvironmentVariable("AUTOBOTS_COGNITO_CLIENT_ID") ?? configuration?.CognitoClientId ?? string.Empty);

        if (!Uri.TryCreate(configuration.ApiBaseUrl, UriKind.Absolute, out var apiUri) || apiUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("The Autobots API URL is missing or is not HTTPS. Install the deployment's autobots.dev.json beside the app.");
        if (!Uri.TryCreate(configuration.CognitoHostedUiBaseUrl, UriKind.Absolute, out var hostedUiUri) || hostedUiUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("The Cognito hosted UI URL is missing or is not HTTPS. Install the deployment's autobots.dev.json beside the app.");
        if (string.IsNullOrWhiteSpace(configuration.CognitoClientId))
            throw new InvalidOperationException("The Cognito app client ID is missing from autobots.dev.json.");

        return configuration with
        {
            ApiBaseUrl = apiUri.ToString().TrimEnd('/'),
            CognitoHostedUiBaseUrl = hostedUiUri.ToString().TrimEnd('/'),
            CognitoClientId = configuration.CognitoClientId.Trim()
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
