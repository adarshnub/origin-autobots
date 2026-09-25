using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Autobots.Desktop;

public sealed class CognitoOAuthLogin(HttpClient httpClient, DesktopConfiguration configuration)
{
    private static readonly TimeSpan CallbackTimeout = TimeSpan.FromMinutes(4);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private TokenSession? _session;

    public bool IsSignedIn => _session is not null;

    public async Task SignInAsync(CancellationToken cancellationToken)
    {
        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var authorize = new UriBuilder(configuration.CognitoHostedUiBaseUrl + "/oauth2/authorize")
        {
            Query = FormUrl(new Dictionary<string, string>
            {
                ["client_id"] = configuration.CognitoClientId,
                ["response_type"] = "code",
                ["scope"] = "openid email profile",
                ["redirect_uri"] = DesktopConfiguration.CallbackUri,
                ["state"] = state,
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256"
            })
        };

        using var listener = new TcpListener(IPAddress.Loopback, 53682);
        listener.Start(1);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CallbackTimeout);

        try
        {
            Process.Start(new ProcessStartInfo(authorize.Uri.AbsoluteUri) { UseShellExecute = true });
            using var client = await listener.AcceptTcpClientAsync(timeout.Token).ConfigureAwait(false);
            var callback = await ReadCallbackAsync(client, timeout.Token).ConfigureAwait(false);
            if (!callback.TryGetValue("state", out var returnedState) || !FixedTimeEquals(state, returnedState))
                throw new InvalidOperationException("The sign-in callback state did not match. Please try again.");
            if (callback.TryGetValue("error", out var oauthError))
                throw new InvalidOperationException($"Cognito sign-in was not completed ({SafeError(oauthError)})." );
            if (!callback.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
                throw new InvalidOperationException("Cognito did not return an authorization code.");

            using var response = await httpClient.PostAsync(
                configuration.CognitoHostedUiBaseUrl + "/oauth2/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["client_id"] = configuration.CognitoClientId,
                    ["code"] = code,
                    ["redirect_uri"] = DesktopConfiguration.CallbackUri,
                    ["code_verifier"] = verifier
                }),
                timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException("Cognito could not complete sign-in. Check the account and try again.");
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
            var root = body.RootElement;
            _session = new TokenSession(
                root.GetProperty("access_token").GetString() ?? throw new InvalidOperationException("Cognito returned no access token."),
                root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null,
                DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32()));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Sign-in timed out. Start sign-in again when ready.");
        }
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var current = _session ?? throw new InvalidOperationException("Sign in before using Autobots.");
        if (current.ExpiresAt > DateTimeOffset.UtcNow.AddSeconds(45))
            return current.AccessToken;
        if (string.IsNullOrWhiteSpace(current.RefreshToken))
            throw new InvalidOperationException("Your sign-in expired. Sign in again to continue.");

        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            current = _session ?? throw new InvalidOperationException("Sign in before using Autobots.");
            if (current.ExpiresAt > DateTimeOffset.UtcNow.AddSeconds(45))
                return current.AccessToken;

            using var response = await httpClient.PostAsync(
                configuration.CognitoHostedUiBaseUrl + "/oauth2/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["client_id"] = configuration.CognitoClientId,
                    ["refresh_token"] = current.RefreshToken!
                }), cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _session = null;
                throw new InvalidOperationException("Your sign-in expired. Sign in again to continue.");
            }
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var root = body.RootElement;
            _session = current with
            {
                AccessToken = root.GetProperty("access_token").GetString() ?? throw new InvalidOperationException("Cognito returned no access token."),
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32()),
                RefreshToken = root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : current.RefreshToken
            };
            return _session.AccessToken;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public void SignOut() => _session = null;

    private static async Task<Dictionary<string, string>> ReadCallbackAsync(TcpClient client, CancellationToken cancellationToken)
    {
        await using var stream = client.GetStream();
        using var request = new MemoryStream();
        var buffer = new byte[1024];
        while (request.Length < 16 * 1024)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            request.Write(buffer, 0, read);
            if (request.ToArray().AsSpan().IndexOf("\r\n\r\n"u8) >= 0)
                break;
        }

        var requestLine = Encoding.ASCII.GetString(request.ToArray()).Split("\r\n", 2)[0].Split(' ');
        if (requestLine.Length < 2 || requestLine[0] != "GET")
            throw new InvalidOperationException("The sign-in callback request was invalid.");
        var callbackUri = new Uri("http://127.0.0.1" + requestLine[1]);
        if (callbackUri.AbsolutePath != "/auth/callback")
            throw new InvalidOperationException("The sign-in callback path did not match the configured redirect.");

        var responseBody = Encoding.UTF8.GetBytes("<html><body><h2>Autobots sign-in complete</h2><p>You can close this tab and return to the app.</p></body></html>");
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {responseBody.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(responseBody, cancellationToken).ConfigureAwait(false);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in callbackUri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var components = pair.Split('=', 2);
            values[Uri.UnescapeDataString(components[0].Replace('+', ' '))] = components.Length == 2
                ? Uri.UnescapeDataString(components[1].Replace('+', ' '))
                : string.Empty;
        }
        return values;
    }

    private static string FormUrl(Dictionary<string, string> fields) =>
        string.Join('&', fields.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool FixedTimeEquals(string first, string second) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(first), Encoding.UTF8.GetBytes(second));

    private static string SafeError(string value) => value.Length <= 64 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-') ? value : "oauth_error";

    private sealed record TokenSession(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt);
}
