using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace OnlyFate.Database;

public sealed record AiResponse(int HttpStatus, JsonElement Body);
public interface IAnalysisClient
{
    Task<AiResponse> SendAsync(JsonElement request, CancellationToken token);
}

public sealed class OpenAiResponsesClient(HttpClient http, string apiKey) : IAnalysisClient
{
    public async Task<AiResponse> SendAsync(JsonElement request, CancellationToken token)
    {
        using HttpRequestMessage message = new(HttpMethod.Post, "https://api.openai.com/v1/responses");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        message.Content = new StringContent(request.GetRawText(), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await http.SendAsync(message, token);
        string body = await response.Content.ReadAsStringAsync(token);
        // Never persist credential material even if an upstream error unexpectedly echoes it.
        if (!string.IsNullOrEmpty(apiKey)) body = body.Replace(apiKey, "[REDACTED]", StringComparison.Ordinal);
        try { using JsonDocument json = JsonDocument.Parse(body); return new((int)response.StatusCode, JsonSerializer.SerializeToElement(Redact(json.RootElement))); }
        catch (JsonException) { return new((int)response.StatusCode, Step2Json.Element(new { error = "non_json_response" })); }
    }

    private object? Redact(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => value.EnumerateObject().ToDictionary(p => p.Name, p => Redact(p.Value)),
        JsonValueKind.Array => value.EnumerateArray().Select(Redact).ToArray(),
        JsonValueKind.String => string.IsNullOrEmpty(apiKey) ? value.GetString() : value.GetString()!.Replace(apiKey, "[REDACTED]", StringComparison.Ordinal),
        _ => value.Clone()
    };
}
