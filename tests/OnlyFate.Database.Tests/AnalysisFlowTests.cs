using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OnlyFate.Database;
using Xunit;

namespace OnlyFate.Database.Tests;

public sealed class AnalysisFlowTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "OnlyFate.Analysis.Tests", Guid.NewGuid().ToString("N"));
    private string Db => Path.Combine(folder, "test.db");
    private static CaseInput Input => new() { Surname = "林", Gender = "male", BirthDate = "2026-06-15", BirthHour = "12", BirthMinute = "00", BirthCountry = "台灣", BirthCity = "台北市", Remarks = "希望文雅" };
    private StoredCase Seed()
    {
        var saved = new CaseRepository(Db).Save(Input);
        using var stream = typeof(AnalysisFlowTests).Assembly.GetManifestResourceStream("Step2Fixture")!;
        using var fixture = JsonDocument.Parse(stream);
        string chart = fixture.RootElement.GetProperty("response").GetProperty("structuredContent").GetProperty("data").GetProperty("chart").GetRawText();
        using var db = new SqliteConnection($"Data Source={Db};Pooling=False"); db.Open();
        using var command = db.CreateCommand();
        command.CommandText = "INSERT INTO ProcessingRun(forCaseRevisionID,Status) VALUES($revision,'waiting'); INSERT INTO ProcessingStep(forProcessingRunID,StepNumber,InputJson,OutputJson,Status) VALUES(last_insert_rowid(),2,'{}',$chart,'completed');";
        command.Parameters.AddWithValue("$revision", saved.CaseRevisionID); command.Parameters.AddWithValue("$chart", chart); command.ExecuteNonQuery();
        return saved;
    }
    private object Scalar(string sql)
    {
        using var db = new SqliteConnection($"Data Source={Db};Pooling=False"); db.Open(); using var c = db.CreateCommand(); c.CommandText = sql; return c.ExecuteScalar()!;
    }
    private static JsonElement Envelope(bool valid = true, bool trial = false) => Step2Json.Element(new {
        id = "test-response", model = "test-model", status = "completed", usage = new { input_tokens = 100, output_tokens = 50 },
        output = new[] { new { type = "message", content = new[] { new { type = "output_text", text = Step2Json.Element(new {
            summary = "試行摘要", direction = "未定案", limitations = new[] { "試行規則" },
            evidence = new[] { new { ruleId = "R1", factPath = valid ? "/dayMaster/char" : "/missing", factValue = "庚", reason = "依據日干" } },
            remarks = new[] { new { quote = "希望文雅", disposition = "defer", reason = "交選名處理" } }, namingElements = trial ? new[] { "金", "土" } : Array.Empty<string>(), blockingIssues = trial ? Array.Empty<string>() : new[] { "規則未驗證" }
        }).GetRawText() } } } }
    });
    private sealed class Client(Func<int, AiResponse> send) : IAnalysisClient
    {
        public List<JsonElement> Requests { get; } = [];
        public Task<AiResponse> SendAsync(JsonElement request, CancellationToken token) { Requests.Add(request); return Task.FromResult(send(Requests.Count)); }
        public static implicit operator ModelConnection(Client client) => new(OpenAiResponsesAdapter.Instance, client);
    }
    [Fact] public async Task RepairsOncePersistsUsageAndReopensTrialWithoutAllowingSelection()
    {
        var saved = Seed(); var client = new Client(n => new(200, Envelope(n == 2)));
        var result = await new Step3Service(Db).ExecuteAsync(saved.NamingCaseID, saved.CaseRevisionID, "test-model", client);
        Assert.Equal(2, client.Requests.Count);
        Assert.Contains("previousOutput", client.Requests[1].GetRawText());
        Assert.False(result.Chart!.Value.GetProperty("canProceed").GetBoolean());
        Assert.Equal(result.ProcessingRunID, new Step3Service(Db).Latest(saved.CaseRevisionID)!.ProcessingRunID);
        Assert.Equal(2L, Scalar("SELECT count(*) FROM ExecutionCall WHERE CallKind='model' AND InputTokens=100"));
        Assert.Equal(0L, Scalar("SELECT count(*) FROM CaseRevision WHERE PendingIssuesJson LIKE '%remarks_interpretation_pending%'"));
    }
    private static JsonElement ClaudeEnvelope(JsonElement openAi, string stop = "end_turn") => Step2Json.Element(new {
        id = "msg_test", type = "message", role = "assistant", model = "claude-test", stop_reason = stop,
        content = new[] { new { type = "text", text = openAi.GetProperty("output")[0].GetProperty("content")[0].GetProperty("text").GetString() } },
        usage = new { input_tokens = 120, output_tokens = 60 }
    });
    [Fact] public async Task ClaudeAdapterRunsSameRepairFlowAndRecordsProvider()
    {
        var saved = Seed(); var client = new Client(n => new(200, ClaudeEnvelope(Envelope(n == 2))));
        var result = await new Step3Service(Db).ExecuteAsync(saved.NamingCaseID, saved.CaseRevisionID, "claude-test", new ModelConnection(ClaudeMessagesAdapter.Instance, client));
        Assert.Equal("completed", result.Status);
        Assert.Equal(2, client.Requests.Count);
        Assert.True(client.Requests[0].TryGetProperty("system", out _));
        Assert.Equal("json_schema", client.Requests[0].GetProperty("output_config").GetProperty("format").GetProperty("type").GetString());
        Assert.False(client.Requests[0].TryGetProperty("text", out _));
        Assert.Contains("previousOutput", client.Requests[1].GetRawText());
        Assert.Equal(2L, Scalar("SELECT count(*) FROM ExecutionCall WHERE Provider='Anthropic' AND Operation='messages.create' AND ModelName='claude-test' AND ProviderRequestID='msg_test' AND InputTokens=120 AND OutputTokens=60"));
    }
    [Theory] [InlineData("refusal", "MODEL_REFUSAL")] [InlineData("max_tokens", "API_NOT_COMPLETED")]
    public async Task ClaudeRefusalOrTruncationStopsWithoutRepair(string stop, string code)
    {
        var saved = Seed(); var client = new Client(_ => new(200, ClaudeEnvelope(Envelope(trial: true), stop)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new Step3Service(Db).ExecuteAsync(saved.NamingCaseID, saved.CaseRevisionID, "claude-test", new ModelConnection(ClaudeMessagesAdapter.Instance, client)));
        Assert.Single(client.Requests);
        Assert.Equal(1L, Scalar($"SELECT count(*) FROM ExecutionCall WHERE Provider='Anthropic' AND Status='failed' AND ErrorCode='{code}'"));
        Assert.Null(new Step3Service(Db).Latest(saved.CaseRevisionID)!.Chart);
    }
    [Fact] public async Task TrialDirectionCanProceedWithoutOpeningFormalDelivery()
    {
        var saved = Seed(); var client = new Client(_ => new(200, Envelope(trial: true)));
        var result = await new Step3Service(Db).ExecuteAsync(saved.NamingCaseID, saved.CaseRevisionID, "test-model", client);
        Assert.True(result.Chart!.Value.GetProperty("canTrialProceed").GetBoolean());
        Assert.False(result.Chart.Value.GetProperty("canProceed").GetBoolean());
        Assert.Equal(AnalysisContract.Version, Scalar("SELECT RuleSetVersion FROM ProcessingRun WHERE RuleSetVersion IS NOT NULL"));
        Assert.NotEmpty(new Step4Service(Db).Prepare(saved.NamingCaseID, saved.CaseRevisionID).GetProperty("candidates").EnumerateArray());
    }
    [Fact] public async Task InvalidResultsStopAfterTwoCallsWithoutOutput()
    {
        var saved = Seed(); var client = new Client(_ => new(200, Envelope(false)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new Step3Service(Db).ExecuteAsync(saved.NamingCaseID, saved.CaseRevisionID, "test-model", client));
        Assert.Equal(2, client.Requests.Count);
        Assert.Equal(DBNull.Value, Scalar("SELECT OutputJson FROM ProcessingStep WHERE StepNumber=3"));
    }
    [Theory] [InlineData("[]")] [InlineData("null")] [InlineData("{\"status\":42}")]
    public async Task MalformedEnvelopeAlwaysClosesCall(string body)
    {
        var saved = Seed(); using var parsed = JsonDocument.Parse(body); var client = new Client(_ => new(200, parsed.RootElement.Clone()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new Step3Service(Db).ExecuteAsync(saved.NamingCaseID, saved.CaseRevisionID, "test-model", client));
        Assert.Equal(0L, Scalar("SELECT count(*) FROM ExecutionCall WHERE Status='running'"));
        Assert.Equal(1L, Scalar("SELECT count(*) FROM ExecutionCall WHERE ResponseJson IS NOT NULL"));
    }
    [Fact] public async Task RevisionChangeCancelsApplicationOfResult()
    {
        var saved = Seed(); var client = new Client(_ => { new CaseRepository(Db).Save(Input with { BirthMinute = "15" }, saved.NamingCaseID, saved.CaseRevisionID); return new(200, Envelope()); });
        await Assert.ThrowsAsync<InvalidOperationException>(() => new Step3Service(Db).ExecuteAsync(saved.NamingCaseID, saved.CaseRevisionID, "test-model", client));
        Assert.Equal("cancelled", Scalar("SELECT Status FROM ProcessingStep WHERE StepNumber=3"));
    }
    [Fact] public async Task CancellationClosesRunAndCall()
    {
        var saved = Seed(); using var stop = new CancellationTokenSource();
        var client = new Client(_ => { stop.Cancel(); throw new OperationCanceledException(stop.Token); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new Step3Service(Db).ExecuteAsync(saved.NamingCaseID, saved.CaseRevisionID, "test-model", client, stop.Token));
        Assert.Equal("cancelled", Scalar("SELECT Status FROM ProcessingStep WHERE StepNumber=3"));
        Assert.Equal(0L, Scalar("SELECT count(*) FROM ExecutionCall WHERE Status='running'"));
    }
    [Fact] public async Task ReplacingChartDuringCallRejectsTheResult()
    {
        var saved = Seed();
        var client = new Client(_ => { Scalar("INSERT INTO ProcessingStep(forProcessingRunID,StepNumber,AttemptNumber,InputJson,Status) VALUES(1,2,2,'{}','running'); SELECT 1;"); return new(200, Envelope()); });
        await Assert.ThrowsAsync<InvalidOperationException>(() => new Step3Service(Db).ExecuteAsync(saved.NamingCaseID, saved.CaseRevisionID, "test-model", client));
        Assert.NotEqual("completed", Scalar("SELECT Status FROM ProcessingStep WHERE StepNumber=3"));
    }
    [Fact] public async Task RefusalDoesNotTriggerRepair()
    {
        var saved = Seed(); var client = new Client(_ => new(200, Step2Json.Element(new { status = "completed", output = new[] { new { type = "message", content = new[] { new { type = "refusal", refusal = "test" } } } } })));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new Step3Service(Db).ExecuteAsync(saved.NamingCaseID, saved.CaseRevisionID, "test-model", client));
        Assert.Single(client.Requests); Assert.Equal("failed", Scalar("SELECT Status FROM ExecutionCall"));
    }
    private sealed class Handler : HttpMessageHandler
    {
        public string? Body; public string? Authorization;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal("https://api.openai.com/v1/responses", request.RequestUri!.AbsoluteUri);
            Body = await request.Content!.ReadAsStringAsync(token); Authorization = request.Headers.Authorization!.Parameter;
            return new(HttpStatusCode.Unauthorized) { Content = new StringContent("{\"error\":\"secret-test-key\"}", Encoding.UTF8, "application/json") };
        }
    }
    private sealed class ClaudeHandler : HttpMessageHandler
    {
        public string? Body, Key, Version; public bool HasAuthorization;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal("https://api.anthropic.com/v1/messages", request.RequestUri!.AbsoluteUri);
            Body = await request.Content!.ReadAsStringAsync(token); HasAuthorization = request.Headers.Authorization is not null;
            Key = request.Headers.GetValues("x-api-key").Single(); Version = request.Headers.GetValues("anthropic-version").Single();
            return new(HttpStatusCode.Unauthorized) { Content = new StringContent("{\"error\":\"secret-test-key\"}", Encoding.UTF8, "application/json") };
        }
    }
    [Fact] public async Task ClaudeTransportUsesApiKeyHeaderAndRedactsEchoedCredential()
    {
        using var handler = new ClaudeHandler(); using var http = new HttpClient(handler);
        var reply = await new ClaudeMessagesClient(http, "secret-test-key").SendAsync(ClaudeMessagesAdapter.Instance.BuildRequest(AnalysisContract.Request("claude-test", Step2Json.Element(new { }))), default);
        Assert.Equal("secret-test-key", handler.Key); Assert.Equal(ClaudeMessagesClient.ApiVersion, handler.Version); Assert.False(handler.HasAuthorization);
        Assert.DoesNotContain("secret-test-key", handler.Body!); Assert.DoesNotContain("secret-test-key", reply.Body.GetRawText());
        Assert.Equal(401, reply.HttpStatus);
    }
    [Fact] public async Task TransportUsesHeaderOnlyAndRedactsEchoedCredential()
    {
        using var handler = new Handler(); using var http = new HttpClient(handler);
        var reply = await new OpenAiResponsesClient(http, "secret-test-key").SendAsync(OpenAiResponsesAdapter.Instance.BuildRequest(AnalysisContract.Request("test-model", Step2Json.Element(new { }))), default);
        Assert.Equal("secret-test-key", handler.Authorization); Assert.DoesNotContain("secret-test-key", handler.Body!); Assert.DoesNotContain("secret-test-key", reply.Body.GetRawText());
    }
    public void Dispose()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "OnlyFate.Analysis.Tests")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(folder).StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException();
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
    }
}
