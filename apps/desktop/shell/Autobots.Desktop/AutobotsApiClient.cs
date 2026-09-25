using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autobots.Contracts;

namespace Autobots.Desktop;

public sealed class AutobotsApiClient(HttpClient httpClient, DesktopConfiguration configuration)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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

    public Task<ActionProposalResponse> ProposeActionAsync(
        string accessToken,
        Guid taskId,
        Guid deviceId,
        Guid observationId,
        Guid leaseId,
        long epoch,
        long sequence,
        byte[] png,
        CancellationToken cancellationToken) =>
        SendJsonAsync<ActionProposalResponse>(
            HttpMethod.Post,
            $"/v1/tasks/{taskId:D}/proposals",
            accessToken,
            new ActionProposalRequest(deviceId, observationId, leaseId, epoch, sequence, "image/png", Convert.ToBase64String(png)),
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
            request.Content = JsonContent.Create(body, options: JsonOptions);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (idempotencyKey is not null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw ApiError(response.StatusCode, content);
        return JsonSerializer.Deserialize<T>(content, JsonOptions)
            ?? throw new InvalidOperationException("The Autobots API returned an empty response.");
    }

    private static InvalidOperationException ApiError(System.Net.HttpStatusCode statusCode, string content)
    {
        var code = "request_failed";
        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.Object && detail.TryGetProperty("code", out var codeElement))
                code = codeElement.GetString() ?? code;
        }
        catch (JsonException)
        {
            // Do not surface proxy or server response bodies that might contain sensitive details.
        }
        var message = code switch
        {
            "owner_confirmation_required" => "The model requested owner confirmation. Autobots stopped before sending the action; review the task and resubmit if appropriate.",
            "proposal_unavailable" => "Autobots could not safely use the latest model response, so it stopped without sending an action.",
            "task_stopped_while_model_was_running" => "The task was stopped before the model response arrived; no late action was sent.",
            _ => $"Autobots API request failed ({(int)statusCode}, {code})."
        };
        return new InvalidOperationException(message);
    }

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
        [property: JsonPropertyName("image_base64")] string ImageBase64);

    public sealed record ActionProposalResponse(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("proposal")] ActionEnvelope? Proposal,
        [property: JsonPropertyName("completion_message")] string? CompletionMessage,
        [property: JsonPropertyName("model_id")] string ModelId,
        [property: JsonPropertyName("input_tokens")] int InputTokens,
        [property: JsonPropertyName("output_tokens")] int OutputTokens,
        [property: JsonPropertyName("actual_cost_usd")] string ActualCostUsd,
        [property: JsonPropertyName("execution")] string Execution);
}
