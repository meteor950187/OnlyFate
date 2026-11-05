using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace OnlyFate.Database;

public sealed record AiResponse(int HttpStatus, JsonElement Body);

/// <summary>送出廠商 request JSON 並取回原始回應；格式轉換由 <see cref="IModelAdapter"/> 負責。</summary>
public interface IAnalysisClient
{
    Task<AiResponse> SendAsync(JsonElement request, CancellationToken token);
}

public sealed class OpenAiResponsesClient(HttpClient http, string apiKey) : IAnalysisClient
{
    public Task<AiResponse> SendAsync(JsonElement request, CancellationToken token)
    {
        HttpRequestMessage message = new(HttpMethod.Post, "https://api.openai.com/v1/responses");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return JsonTransport.SendAsync(http, message, request, apiKey, token);
    }
}

public sealed class ClaudeMessagesClient(HttpClient http, string apiKey) : IAnalysisClient
{
    public const string ApiVersion = "2026-10-21";

    public Task<AiResponse> SendAsync(JsonElement request, CancellationToken token)
    {
        HttpRequestMessage message = new(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        message.Headers.Add("x-api-key", apiKey);
        message.Headers.Add("anthropic-version", ApiVersion);
        return JsonTransport.SendAsync(http, message, request, apiKey, token);
    }
}

internal static class JsonTransport
{
    public static async Task<AiResponse> SendAsync(HttpClient http, HttpRequestMessage message, JsonElement request, string apiKey, CancellationToken token)
    {
        using (message)
        {
            message.Content = new StringContent(request.GetRawText(), Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await http.SendAsync(message, token);
            string body = await response.Content.ReadAsStringAsync(token);
            // Never persist credential material even if an upstream error unexpectedly echoes it.
            if (!string.IsNullOrEmpty(apiKey)) body = body.Replace(apiKey, "[REDACTED]", StringComparison.Ordinal);
            try { using JsonDocument json = JsonDocument.Parse(body); return new((int)response.StatusCode, JsonSerializer.SerializeToElement(Redact(json.RootElement, apiKey))); }
            catch (JsonException) { return new((int)response.StatusCode, Step2Json.Element(new { error = "non_json_response" })); }
        }
    }

    private static object? Redact(JsonElement value, string apiKey) => value.ValueKind switch
    {
        JsonValueKind.Object => value.EnumerateObject().ToDictionary(p => p.Name, p => Redact(p.Value, apiKey)),
        JsonValueKind.Array => value.EnumerateArray().Select(v => Redact(v, apiKey)).ToArray(),
        JsonValueKind.String => string.IsNullOrEmpty(apiKey) ? value.GetString() : value.GetString()!.Replace(apiKey, "[REDACTED]", StringComparison.Ordinal),
        _ => value.Clone()
    };
}
