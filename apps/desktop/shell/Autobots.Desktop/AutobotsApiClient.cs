using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autobots.Contracts;

namespace Autobots.Desktop;

public sealed class AutobotsApiException(int statusCode, string code, string? reason, string message) : InvalidOperationException(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
    public string? Reason { get; } = reason;
}

/// <summary>Optional API capabilities advertised by <c>/healthz</c>; older deployments advertise none.</summary>
public sealed record ApiFeatures(string ApiVersion, bool Live, bool StepHistory, bool DesktopActionsV2, bool Transcription)
{
    public static ApiFeatures Legacy { get; } = new("0.2", Live: true, StepHistory: false, DesktopActionsV2: false, Transcription: false);
}

public sealed record StepHistoryEntry(
    [property: JsonPropertyName("step")] long Step,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("detail")] string? Detail);

public sealed class AutobotsApiClient(HttpClient httpClient, DesktopConfiguration configuration)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private ApiFeatures? _features;

    public async Task<ApiFeatures> GetFeaturesAsync(CancellationToken cancellationToken)
    {
        if (_features is not null)
            return _features;
        using var response = await httpClient.GetAsync(configuration.ApiBaseUrl + "/healthz", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var root = document.RootElement;
        var features = root.TryGetProperty("features", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(item => item.GetString()).OfType<string>().ToHashSet(StringComparer.Ordinal)
            : [];
        _features = new ApiFeatures(
            root.TryGetProperty("api_version", out var version) ? version.GetString() ?? "0.2" : "0.2",
            Live: root.TryGetProperty("mode", out var mode) && mode.GetString() == "live",
            StepHistory: features.Contains("step-history"),
            DesktopActionsV2: features.Contains("desktop-actions-v2"),
            Transcription: features.Contains("transcription"));
        return _features;
    }

    public async Task EnrollDeviceAsync(string accessToken, Guid deviceId, CancellationToken cancellationToken)
    {
        _ = await SendJsonAsync<DeviceEnrollmentResponse>(
            HttpMethod.Post,
            "/v1/devices/enroll",
            accessToken,
            new DeviceEnrollmentRequest(deviceId, "Windows desktop", "windows"),
            cancellationToken).ConfigureAwait(false);
    }

    public Task<TaskResponse> CreateTaskAsync(string accessToken, Guid deviceId, string instruction, string idempotencyKey, CancellationToken cancellationToken) =>
        SendJsonAsync<TaskResponse>(
            HttpMethod.Post,
            "/v1/tasks",
            accessToken,
            new CreateTaskRequest(deviceId, instruction),
            cancellationToken,
            idempotencyKey);

    public Task<TaskResponse> StopTaskAsync(string accessToken, Guid taskId, CancellationToken cancellationToken) =>
        SendJsonAsync<TaskResponse>(HttpMethod.Post, $"/v1/tasks/{taskId:D}/stop", accessToken, null, cancellationToken);

    public Task<JsonElement> GetUsageAsync(string accessToken, CancellationToken cancellationToken) =>
        SendJsonAsync<JsonElement>(HttpMethod.Get, "/v1/usage", accessToken, null, cancellationToken);

    public async Task<ActionProposalResponse> ProposeActionAsync(
        string accessToken,
        Guid taskId,
        Guid deviceId,
        Guid observationId,
        Guid leaseId,
        long epoch,
        long sequence,
        string imageMimeType,
        byte[] image,
        IReadOnlyList<StepHistoryEntry> history,
        string? foregroundTitle,
        CancellationToken cancellationToken)
    {
        var features = await GetFeaturesAsync(cancellationToken).ConfigureAwait(false);
        var request = new ActionProposalRequest(
            deviceId,
            observationId,
            leaseId,
            epoch,
            sequence,
            imageMimeType,
            Convert.ToBase64String(image),
            features.StepHistory ? history.TakeLast(15).ToArray() : null,
            features.StepHistory && !string.IsNullOrWhiteSpace(foregroundTitle) ? Truncate(foregroundTitle, 300) : null);
        return await SendJsonAsync<ActionProposalResponse>(
            HttpMethod.Post,
            $"/v1/tasks/{taskId:D}/proposals",
            accessToken,
            request,
            cancellationToken).ConfigureAwait(false);
    }

    public Task<TranscriptionResponse> TranscribeAsync(string accessToken, Guid deviceId, byte[] wav, string? languageHint, CancellationToken cancellationToken) =>
        SendJsonAsync<TranscriptionResponse>(
            HttpMethod.Post,
            "/v1/transcriptions",
            accessToken,
            new TranscriptionRequest(deviceId, "audio/wav", Convert.ToBase64String(wav), string.IsNullOrWhiteSpace(languageHint) ? null : languageHint),
            cancellationToken);

    private async Task<T> SendJsonAsync<T>(
        HttpMethod method,
        string path,
        string accessToken,
        object? body,
        CancellationToken cancellationToken,
        string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(method, configuration.ApiBaseUrl + path);
        if (body is not null)
            request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptions);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (idempotencyKey is not null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw ApiError((int)response.StatusCode, content);
        return JsonSerializer.Deserialize<T>(content, JsonOptions)
            ?? throw new InvalidOperationException("The Autobots API returned an empty response.");
    }

    private static AutobotsApiException ApiError(int statusCode, string content)
    {
        var code = "request_failed";
        string? reason = null;
        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.Object)
            {
                if (detail.TryGetProperty("code", out var codeElement))
                    code = codeElement.GetString() ?? code;
                if (detail.TryGetProperty("reason", out var reasonElement))
                    reason = Truncate(reasonElement.GetString() ?? string.Empty, 200);
            }
        }
        catch (JsonException)
        {
            // Do not surface proxy or server response bodies that might contain sensitive details.
        }
        var message = code switch
        {
            "owner_confirmation_required" => "The AI service asked for your confirmation before continuing, so Autobots stopped without acting. Review the screen and start again if appropriate.",
            "proposal_unavailable" => "Autobots couldn't use the AI's last suggestion.",
            "task_stopped_while_model_was_running" => "The task was stopped before the AI replied; no late action was sent.",
            "model_budget_exhausted" => "Today's AI budget for Autobots is used up. Try again later or raise the limit on the service.",
            "speech_budget_exhausted" => "Today's voice transcription budget is used up. Type your task instead.",
            "live_inference_disabled" => "The Autobots service has live AI turned off.",
            "invalid_audio" => "That recording couldn't be used. Try speaking again.",
            "device_not_enrolled" => "This PC isn't enrolled with your Autobots account yet.",
            _ when statusCode is 401 => "Your Autobots sign-in expired. Connect your account again.",
            _ when statusCode is 403 => "This account isn't allowed to use Autobots.",
            _ when statusCode >= 500 => $"The Autobots service had a problem ({statusCode}). Try again in a moment.",
            _ => $"The Autobots service rejected the request ({statusCode}, {code})."
        };
        return new AutobotsApiException(statusCode, code, reason, message);
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];

    private sealed record DeviceEnrollmentRequest(
        [property: JsonPropertyName("device_id")] Guid DeviceId,
        [property: JsonPropertyName("display_name")] string DisplayName,
        [property: JsonPropertyName("platform")] string Platform);

    private sealed record DeviceEnrollmentResponse(
        [property: JsonPropertyName("device_id")] Guid DeviceId,
        [property: JsonPropertyName("enrolled")] bool Enrolled);

    private sealed record CreateTaskRequest(
        [property: JsonPropertyName("device_id")] Guid DeviceId,
        [property: JsonPropertyName("instruction")] string Instruction);

    public sealed record TaskResponse(
        [property: JsonPropertyName("task_id")] Guid TaskId,
        [property: JsonPropertyName("device_id")] Guid DeviceId,
        [property: JsonPropertyName("instruction")] string Instruction,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("epoch")] long Epoch,
        [property: JsonPropertyName("created_at")] string CreatedAt,
        [property: JsonPropertyName("updated_at")] string UpdatedAt);

    private sealed record ActionProposalRequest(
        [property: JsonPropertyName("device_id")] Guid DeviceId,
        [property: JsonPropertyName("observation_id")] Guid ObservationId,
        [property: JsonPropertyName("lease_id")] Guid LeaseId,
        [property: JsonPropertyName("epoch")] long Epoch,
        [property: JsonPropertyName("sequence")] long Sequence,
        [property: JsonPropertyName("image_mime_type")] string ImageMimeType,
        [property: JsonPropertyName("image_base64")] string ImageBase64,
        [property: JsonPropertyName("history")] StepHistoryEntry[]? History,
        [property: JsonPropertyName("foreground_title")] string? ForegroundTitle);

    public sealed record ActionProposalResponse(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("proposal")] ActionEnvelope? Proposal,
        [property: JsonPropertyName("completion_message")] string? CompletionMessage,
        [property: JsonPropertyName("intent")] string? Intent,
        [property: JsonPropertyName("model_id")] string ModelId,
        [property: JsonPropertyName("input_tokens")] int InputTokens,
        [property: JsonPropertyName("output_tokens")] int OutputTokens,
        [property: JsonPropertyName("actual_cost_usd")] string ActualCostUsd,
        [property: JsonPropertyName("execution")] string Execution);

    private sealed record TranscriptionRequest(
        [property: JsonPropertyName("device_id")] Guid DeviceId,
        [property: JsonPropertyName("audio_mime_type")] string AudioMimeType,
        [property: JsonPropertyName("audio_base64")] string AudioBase64,
        [property: JsonPropertyName("language_hint")] string? LanguageHint);

    public sealed record TranscriptionResponse(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("transcript")] string? Transcript,
        [property: JsonPropertyName("audio_seconds")] double AudioSeconds,
        [property: JsonPropertyName("model_id")] string ModelId,
        [property: JsonPropertyName("input_tokens")] int InputTokens,
        [property: JsonPropertyName("output_tokens")] int OutputTokens,
        [property: JsonPropertyName("actual_cost_usd")] string ActualCostUsd);
}
