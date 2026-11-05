using System.Text.Json;
using OnlyFate.Database;
using Xunit;

namespace OnlyFate.Database.Tests;

public sealed class ModelAdapterTests
{
    private static readonly ModelRequest Sample = new("m", "規則", "{\"context\":{}}", "report_prose", ReportContract.Schema, 6000);

    public static TheoryData<string> Schemas => new() { "analysis", "naming", "report" };
    private static JsonElement SchemaOf(string name) => name switch { "analysis" => AnalysisContract.Schema, "naming" => NamingContract.Schema, _ => ReportContract.Schema };

    [Fact] public void OpenAiRequestKeepsResponsesShape()
    {
        var request = OpenAiResponsesAdapter.Instance.BuildRequest(Sample);
        Assert.False(request.GetProperty("store").GetBoolean());
        Assert.Equal(6000, request.GetProperty("max_output_tokens").GetInt32());
        Assert.Equal("規則", request.GetProperty("instructions").GetString());
        Assert.Equal(Sample.UserContent, request.GetProperty("input")[0].GetProperty("content").GetString());
        var format = request.GetProperty("text").GetProperty("format");
        Assert.Equal("report_prose", format.GetProperty("name").GetString());
        Assert.True(format.GetProperty("strict").GetBoolean());
        Assert.True(JsonElement.DeepEquals(ReportContract.Schema, format.GetProperty("schema")));
    }

    [Fact] public void ClaudeRequestUsesMessagesShapeWithoutOpenAiFields()
    {
        var request = ClaudeMessagesAdapter.Instance.BuildRequest(Sample);
        Assert.Equal("規則", request.GetProperty("system").GetString());
        Assert.Equal("user", request.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.Equal(Sample.UserContent, request.GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.Equal(ClaudeMessagesAdapter.MinimumMaxTokens, request.GetProperty("max_tokens").GetInt32());
        Assert.Equal("json_schema", request.GetProperty("output_config").GetProperty("format").GetProperty("type").GetString());
        foreach (string field in new[] { "store", "instructions", "input", "text", "tools", "thinking", "temperature" }) Assert.False(request.TryGetProperty(field, out _), field);
    }

    [Theory] [MemberData(nameof(Schemas))]
    public void ClaudeSchemaDropsOnlyUnsupportedArrayLimits(string name)
    {
        var original = SchemaOf(name); var sent = ClaudeMessagesAdapter.SupportedSchema(original);
        Walk(sent, node =>
        {
            Assert.False(node.TryGetProperty("maxItems", out _));
            if (node.TryGetProperty("minItems", out var min)) Assert.InRange(min.GetInt32(), 0, 1);
            if (node.TryGetProperty("type", out var type) && type.GetString() == "object") Assert.False(node.GetProperty("additionalProperties").GetBoolean());
        });
        Assert.Equal(Count(original, "enum"), Count(sent, "enum"));
        Assert.Equal(Count(original, "required"), Count(sent, "required"));
    }

    [Fact] public void ClaudeSchemaDescribesRemovedCounts()
    {
        var groups = ClaudeMessagesAdapter.SupportedSchema(NamingContract.Schema).GetProperty("properties").GetProperty("groups");
        Assert.Equal(1, groups.GetProperty("minItems").GetInt32());
        Assert.Contains("恰好 2 項", groups.GetProperty("description").GetString());
    }

    [Theory]
    [InlineData(200, "end_turn", ModelOutcome.Completed)]
    [InlineData(200, "refusal", ModelOutcome.Refused)]
    [InlineData(200, "max_tokens", ModelOutcome.NotCompleted)]
    [InlineData(200, "pause_turn", ModelOutcome.NotCompleted)]
    [InlineData(529, "end_turn", ModelOutcome.NotCompleted)]
    public void ClaudeResponseMapsStopReasons(int status, string stop, ModelOutcome expected)
    {
        var body = Step2Json.Element(new
        {
            id = "msg_1", type = "message", model = "claude-x", stop_reason = stop,
            content = new object[] { new { type = "thinking", thinking = "" }, new { type = "text", text = "{\"a\":" }, new { type = "text", text = "1}" } },
            usage = new { input_tokens = 7, output_tokens = 9 }
        });
        var result = ClaudeMessagesAdapter.Instance.ReadResponse(new(status, body));
        Assert.Equal(expected, result.Outcome);
        Assert.Equal(expected == ModelOutcome.Completed ? "{\"a\":1}" : "", result.OutputText);
        Assert.Equal(("msg_1", "claude-x", 7L, 9L), (result.ProviderRequestId, result.ActualModel, result.InputTokens, result.OutputTokens));
    }

    [Fact] public void ClaudeErrorBodyIsNotCompletedWithUnknownUsage()
    {
        var result = ClaudeMessagesAdapter.Instance.ReadResponse(new(400, Step2Json.Element(new { type = "error", error = new { type = "invalid_request_error", message = "x" } })));
        Assert.Equal(ModelOutcome.NotCompleted, result.Outcome);
        Assert.Null(result.InputTokens); Assert.Null(result.OutputTokens);
    }

    [Fact] public void OpenAiRefusalAndIncompleteAreDistinguished()
    {
        JsonElement Body(string status, string part) => Step2Json.Element(new { status, output = new[] { new { type = "message", content = new[] { new { type = part, text = "{}" } } } } });
        Assert.Equal(ModelOutcome.Refused, OpenAiResponsesAdapter.Instance.ReadResponse(new(200, Body("completed", "refusal"))).Outcome);
        Assert.Equal(ModelOutcome.NotCompleted, OpenAiResponsesAdapter.Instance.ReadResponse(new(200, Body("incomplete", "output_text"))).Outcome);
        Assert.Equal("{}", OpenAiResponsesAdapter.Instance.ReadResponse(new(200, Body("completed", "output_text"))).OutputText);
    }

    private static void Walk(JsonElement node, Action<JsonElement> visit)
    {
        if (node.ValueKind == JsonValueKind.Object) { visit(node); foreach (var p in node.EnumerateObject()) Walk(p.Value, visit); }
        else if (node.ValueKind == JsonValueKind.Array) foreach (var item in node.EnumerateArray()) Walk(item, visit);
    }
    private static int Count(JsonElement node, string key) { int n = 0; Walk(node, o => { if (o.TryGetProperty(key, out _)) n++; }); return n; }
}
