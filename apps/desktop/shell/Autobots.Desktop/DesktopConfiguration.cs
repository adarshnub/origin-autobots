using System.Text.Json;
using System.Reflection;

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
        // Release builds carry public endpoint identifiers inside the executable. A single-file
        // .NET app may extract to a temporary directory, and Windows can launch an EXE directly
        // from a ZIP without extracting the adjacent file. Neither case should break sign-in.
        using var embedded = Assembly.GetExecutingAssembly().GetManifestResourceStream("Autobots.Desktop.PublicConfig.json");
        DesktopConfiguration? configuration = embedded is not null
            ? JsonSerializer.Deserialize<DesktopConfiguration>(embedded, JsonOptions)
            : null;
        if (configuration is null)
        {
            var path = paths.FirstOrDefault(File.Exists);
            if (path is not null)
                configuration = JsonSerializer.Deserialize<DesktopConfiguration>(File.ReadAllText(path), JsonOptions);
        }

        configuration = new DesktopConfiguration(
            GetConfigurationValue("AUTOBOTS_API_BASE_URL", configuration?.ApiBaseUrl),
            GetConfigurationValue("AUTOBOTS_COGNITO_HOSTED_UI_URL", configuration?.CognitoHostedUiBaseUrl),
            GetConfigurationValue("AUTOBOTS_COGNITO_CLIENT_ID", configuration?.CognitoClientId));

        if (!Uri.TryCreate(configuration.ApiBaseUrl, UriKind.Absolute, out var apiUri) || apiUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("The Autobots service configuration is missing or invalid. Reinstall Autobots from the official Windows installer.");
        if (!Uri.TryCreate(configuration.CognitoHostedUiBaseUrl, UriKind.Absolute, out var hostedUiUri) || hostedUiUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("The Autobots sign-in configuration is missing or invalid. Reinstall Autobots from the official Windows installer.");
        if (string.IsNullOrWhiteSpace(configuration.CognitoClientId))
            throw new InvalidOperationException("The Autobots account client ID is missing. Reinstall Autobots from the official Windows installer.");

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
