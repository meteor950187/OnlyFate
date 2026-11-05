using System.Text.Json;
using OnlyFate.Database;
using Xunit;

namespace OnlyFate.Database.Tests;

public class AnalysisTests
{
    private static JsonElement Facts => Step2Json.Element(new { day = "庚" });
    private static JsonElement Output(string path = "/day", string quote = "希望文雅") => Step2Json.Element(new {
        summary = "試行摘要", direction = "尚待方法驗證", limitations = new[] { "規則試行" },
        evidence = new[] { new { ruleId = "R1", factPath = path, factValue = "庚", reason = "引用日干" } },
        remarks = new[] { new { quote, disposition = "defer", reason = "供選名使用" } },
        namingElements = Array.Empty<string>(), blockingIssues = new[] { "規則未驗證" }
    });

    [Fact] public void ValidEvidenceIsAccepted() => Assert.Empty(AnalysisContract.Validate(Output(), Facts, "希望文雅"));
    [Fact] public void InventedEvidenceIsRejected() => Assert.NotEmpty(AnalysisContract.Validate(Output("/missing"), Facts, "希望文雅"));
    [Fact] public void InventedRemarksAreRejected() => Assert.NotEmpty(AnalysisContract.Validate(Output(quote: "必須補水"), Facts, "希望文雅"));
    [Fact] public void HugeArrayIndexReturnsValidationError() => Assert.NotEmpty(AnalysisContract.Validate(Output("/items/999999999999999999999"), Step2Json.Element(new { items = new[] { "庚" } }), "希望文雅"));
    [Fact] public void RequestHasNoHistoryToolsOrSecrets()
    {
        var request = OpenAiResponsesAdapter.Instance.BuildRequest(AnalysisContract.Request("configured-model", Facts));
        Assert.False(request.GetProperty("store").GetBoolean());
        Assert.False(request.TryGetProperty("tools", out _));
        Assert.False(request.TryGetProperty("previous_response_id", out _));
        Assert.Equal("json_schema", request.GetProperty("text").GetProperty("format").GetProperty("type").GetString());
    }
}
