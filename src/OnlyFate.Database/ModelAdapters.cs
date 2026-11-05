using System.Text.Json;
using System.Text.Json.Nodes;

namespace OnlyFate.Database;

/// <summary>與廠商無關的單次結構化輸出請求；Step 3–5 契約只產生這份資料。</summary>
public sealed record ModelRequest(string Model, string Instructions, string UserContent, string SchemaName, JsonElement Schema, int MaxOutputTokens);

public enum ModelOutcome { Completed, Refused, NotCompleted }

/// <summary>由 adapter 從廠商原始回應整理出的統一結果；原始回應另行保存。</summary>
public sealed record ModelResult(ModelOutcome Outcome, string OutputText, string? ProviderRequestId, string? ActualModel, long? InputTokens, long? OutputTokens);

/// <summary>把中性請求轉成廠商 request，並把廠商回應讀成 <see cref="ModelResult"/>；不做 HTTP。</summary>
public interface IModelAdapter
{
    string Provider { get; }
    string Operation { get; }
    JsonElement BuildRequest(ModelRequest request);
    ModelResult ReadResponse(AiResponse response);
}

/// <summary>一次執行使用的 adapter 與實際傳輸；測試以假 transport 取代 HTTP。</summary>
public sealed record ModelConnection(IModelAdapter Adapter, IAnalysisClient Transport);

public sealed class OpenAiResponsesAdapter : IModelAdapter
{
    public static readonly OpenAiResponsesAdapter Instance = new();
    public string Provider => "OpenAI";
    public string Operation => "responses.create";

    public JsonElement BuildRequest(ModelRequest r) => Step2Json.Element(new
    {
        model = r.Model, instructions = r.Instructions, store = false, max_output_tokens = r.MaxOutputTokens,
        input = new[] { new { role = "user", content = r.UserContent } },
        text = new { format = new { type = "json_schema", name = r.SchemaName, strict = true, schema = r.Schema } }
    });

    public ModelResult ReadResponse(AiResponse response)
    {
        var body = response.Body;
        bool done = response.HttpStatus is >= 200 and < 300 && body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String && status.GetString() == "completed";
        List<string> texts = [];
        bool refused = false;
        if (done && body.TryGetProperty("output", out var items) && items.ValueKind == JsonValueKind.Array)
            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("type", out var kind) || kind.GetString() != "message" || !item.TryGetProperty("content", out var contents) || contents.ValueKind != JsonValueKind.Array) continue;
                foreach (var content in contents.EnumerateArray())
                {
                    if (!content.TryGetProperty("type", out var type)) continue;
                    if (type.GetString() == "refusal") refused = true;
                    if (type.GetString() == "output_text" && content.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String) texts.Add(text.GetString()!);
                }
            }
        var outcome = !done ? ModelOutcome.NotCompleted : refused ? ModelOutcome.Refused : ModelOutcome.Completed;
        return ModelAdapterJson.Result(outcome, string.Concat(texts), body);
    }
}

/// <summary>
/// Claude Messages API。structured outputs 只支援 minItems 0／1 且不支援 maxItems，
/// 送出前移除這些限制並寫入 description；數量仍由各階段 C# 驗證強制檢查。
/// </summary>
public sealed class ClaudeMessagesAdapter : IModelAdapter
{
    public static readonly ClaudeMessagesAdapter Instance = new();
    /// <summary>Claude 新模型預設會思考，思考 token 計入 max_tokens；低於此值容易在 JSON 完成前截斷。</summary>
    public const int MinimumMaxTokens = 16000;
    public string Provider => "Anthropic";
    public string Operation => "messages.create";

    public JsonElement BuildRequest(ModelRequest r) => Step2Json.Element(new
    {
        model = r.Model, max_tokens = Math.Max(r.MaxOutputTokens, MinimumMaxTokens), system = r.Instructions,
        messages = new[] { new { role = "user", content = r.UserContent } },
        output_config = new { format = new { type = "json_schema", schema = SupportedSchema(r.Schema) } }
    });

    public ModelResult ReadResponse(AiResponse response)
    {
        var body = response.Body;
        bool ok = response.HttpStatus is >= 200 and < 300 && body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "message";
        string? stop = ok && body.TryGetProperty("stop_reason", out var reason) && reason.ValueKind == JsonValueKind.String ? reason.GetString() : null;
        List<string> texts = [];
        if (stop == "end_turn" && body.TryGetProperty("content", out var blocks) && blocks.ValueKind == JsonValueKind.Array)
            foreach (var block in blocks.EnumerateArray())
                if (block.TryGetProperty("type", out var kind) && kind.GetString() == "text" && block.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    texts.Add(text.GetString()!);
        var outcome = stop switch { "end_turn" => ModelOutcome.Completed, "refusal" => ModelOutcome.Refused, _ => ModelOutcome.NotCompleted };
        return ModelAdapterJson.Result(outcome, string.Concat(texts), body);
    }

    public static JsonElement SupportedSchema(JsonElement schema)
    {
        var node = JsonNode.Parse(schema.GetRawText())!;
        Strip(node);
        return JsonSerializer.SerializeToElement(node);
    }

    private static void Strip(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj["type"]?.GetValue<string>() == "array")
                {
                    int? min = obj["minItems"]?.GetValue<int>(), max = obj["maxItems"]?.GetValue<int>();
                    if (min > 1 || max is not null)
                    {
                        obj.Remove("maxItems");
                        if (min > 1) obj["minItems"] = 1;
                        string note = min == max ? $"必須恰好 {min} 項" : $"項目數 {min ?? 0}–{(max is null ? "不限" : max.ToString())}";
                        obj["description"] = obj["description"] is null ? note : $"{obj["description"]}；{note}";
                    }
                }
                foreach (string key in new[] { "minLength", "maxLength", "minimum", "maximum", "multipleOf" }) obj.Remove(key);
                foreach (var child in obj.Select(p => p.Value).ToArray()) Strip(child);
                break;
            case JsonArray array:
                foreach (var child in array) Strip(child);
                break;
        }
    }
}

internal static class ModelAdapterJson
{
    // OpenAI Responses 與 Claude Messages 的 id、model、usage.input_tokens／output_tokens 欄位名稱相同。
    public static ModelResult Result(ModelOutcome outcome, string text, JsonElement body)
    {
        string? Text(string name) => body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        long? Usage(string name) => body.ValueKind == JsonValueKind.Object && body.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object
            && usage.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long count) && count >= 0 ? count : null;
        return new(outcome, text, Text("id"), Text("model"), Usage("input_tokens"), Usage("output_tokens"));
    }
}

/// <summary>桌面介面可選的模型服務；環境變數依廠商分開。</summary>
public sealed record ModelProvider(string Id, string DisplayName, string ModelVariable, string KeyVariable, IModelAdapter Adapter, Func<HttpClient, string, IAnalysisClient> Transport)
{
    public static readonly ModelProvider OpenAi = new("openai", "OpenAI", "OPENAI_MODEL", "OPENAI_API_KEY", OpenAiResponsesAdapter.Instance, (http, key) => new OpenAiResponsesClient(http, key));
    public static readonly ModelProvider Claude = new("anthropic", "Claude（Anthropic）", "ANTHROPIC_MODEL", "ANTHROPIC_API_KEY", ClaudeMessagesAdapter.Instance, (http, key) => new ClaudeMessagesClient(http, key));
    public static IReadOnlyList<ModelProvider> All { get; } = [OpenAi, Claude];
    public ModelConnection Connect(HttpClient http, string apiKey) => new(Adapter, Transport(http, apiKey));
    public override string ToString() => DisplayName;
}
