using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using OnlyFate.Database;
using Xunit;

namespace OnlyFate.Database.Tests;

public sealed class Step2Tests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "OnlyFate.Database.Tests", Guid.NewGuid().ToString("N"));
    private string PathName => Path.Combine(directory, "step2.db");
    private static CaseInput Input => new() { Surname = "林", Gender = "male", BirthDate = "2026-06-15", BirthHour = "12", BirthMinute = "00", BirthCountry = "台灣", BirthCity = "台北市" };
    private static BirthplaceCandidate Taipei => BirthplaceCatalog.Default.Find("台灣", "台北市").Single();

    [Fact]
    public void CityAliasesResolveToDocumentedPointAndAmbiguousCitiesStayMultiple()
    {
        BirthplaceCandidate place = Taipei;
        Assert.Equal(1668341, place.GeoNameID);
        Assert.Equal(121.52639, place.Longitude);
        Assert.Equal("Asia/Taipei", place.TimeZoneID);
        Assert.Equal(place.GeoNameID, BirthplaceCatalog.Default.Find("TW", "臺北市").Single().GeoNameID);
        Assert.True(BirthplaceCatalog.Default.Find("US", "Springfield").Count > 1);
        Assert.Empty(BirthplaceCatalog.Default.Find("台灣", "虛構不存在的城市"));
        Assert.Throws<Step2Exception>(() => BirthplaceCatalog.Default.Find("不知道的國家", "台北"));
    }

    [Theory]
    [InlineData("2026-06-15", "12", "00", "America/New_York", -5, 1, -4, "2026-06-15T16:00:00Z")]
    [InlineData("2026-01-15", "12", "00", "America/New_York", -5, 0, -5, "2026-01-15T17:00:00Z")]
    [InlineData("1979-07-01", "12", "00", "Asia/Taipei", 8, 1, 9, "1979-07-01T03:00:00Z")]
    [InlineData("2026-06-15", "12", "00", "Asia/Kolkata", 5.5, 0, 5.5, "2026-06-15T06:30:00Z")]
    public void BirthDateTimezoneIncludesHistoricalDstAndFractionalOffsets(string date, string hour, string minute, string zone, double standard, double dst, double total, string utc)
    {
        BirthTimeReceipt receipt = BirthTimeResolver.Resolve(Input with { BirthDate = date, BirthHour = hour, BirthMinute = minute }, zone);
        Assert.Equal((standard, dst, total, utc), (receipt.StandardOffsetHours, receipt.DstOffsetHours, receipt.UtcOffsetHours, receipt.BirthUtc));
        Assert.Equal("minute", receipt.InputPrecision);
    }

    [Theory]
    [InlineData("2026-03-08", "02", "30", "NONEXISTENT_CIVIL_TIME")]
    [InlineData("2026-11-01", "01", "30", "AMBIGUOUS_CIVIL_TIME")]
    public void SkippedOrRepeatedClockTimeIsRejected(string date, string hour, string minute, string code) =>
        Assert.Equal(code, Assert.Throws<Step2Exception>(() => BirthTimeResolver.Resolve(Input with { BirthDate = date, BirthHour = hour, BirthMinute = minute }, "America/New_York")).Code);

    [Fact]
    public async Task SuccessfulAttemptKeepsOriginalFactsAndAllToolReceipts()
    {
        StoredCase saved = new CaseRepository(PathName).Save(Input);
        FakeClient fake = new();
        Step2Service service = Service(fake);
        ChartRunResult result = await service.ExecuteAsync(saved.NamingCaseID, saved.CaseRevisionID, Taipei.GeoNameID, true);
        Assert.Equal("completed", result.Status);
        Assert.Equal("庚申", result.Chart!.Value.GetProperty("pillars").GetProperty("day").GetProperty("ganZhi").GetString());
        Assert.Equal(2, fake.Calls.Count);
        Assert.Equal("get_openfate_bazi_policy", fake.Calls[0].Tool);
        Assert.Equal(0, fake.Calls[1].Arguments["second"]);
        Assert.Equal("ZI_HOUR_23", fake.Calls[1].Arguments["dayBoundaryMode"]);
        Assert.False(fake.Calls[1].Arguments.ContainsKey("surname"));
        Assert.False(fake.Calls[1].Arguments.ContainsKey("isLeapMonth"));
        Assert.Equal(result.ProcessingRunID, new Step2Service(PathName).Latest(saved.CaseRevisionID)!.ProcessingRunID);
        Assert.Equal("waiting", Scalar("SELECT Status FROM ProcessingRun"));
        Assert.Equal(2L, Scalar("SELECT count(*) FROM ExecutionCall WHERE Status='succeeded' AND ResponseJson IS NOT NULL"));
        Assert.Equal(0L, Scalar("SELECT count(*) FROM ExecutionCall WHERE CallKind='model'"));
        Assert.Equal("2026-06-15", Scalar("SELECT BirthDate FROM CaseRevision"));
        Assert.Equal(1L, Scalar("SELECT json_extract(CalendarDataJson,'$.day') FROM CaseRevision"));
        Assert.Equal(DBNull.Value, Scalar("SELECT BirthTimeZoneID FROM CaseRevision"));
    }

    [Fact]
    public async Task CityPrecisionMustBeExplicitlyAcceptedBeforeStartingARun()
    {
        StoredCase saved = new CaseRepository(PathName).Save(Input with { BirthLocationDetail = "虛構醫院，不能宣稱已定位" });
        FakeClient fake = new();
        await Assert.ThrowsAsync<Step2Exception>(() => Service(fake).ExecuteAsync(saved.NamingCaseID, saved.CaseRevisionID, Taipei.GeoNameID, false));
        Assert.Equal(0L, Scalar("SELECT count(*) FROM ProcessingRun"));
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task MissingHourInToolResultIsRecordedButNeverMarkedComplete()
    {
        StoredCase saved = new CaseRepository(PathName).Save(Input);
        FakeClient fake = new(response =>
        {
            JsonNode node = JsonNode.Parse(response.GetRawText())!;
            JsonNode payload = JsonNode.Parse(node["content"]![0]!["text"]!.GetValue<string>())!;
            payload["data"]!["chart"]!["pillars"]!["hour"] = null;
            node["content"]![0]!["text"] = payload.ToJsonString();
            node["structuredContent"] = payload.DeepClone();
            return JsonSerializer.SerializeToElement(node);
        });
        await Assert.ThrowsAsync<Step2Exception>(() => Service(fake).ExecuteAsync(saved.NamingCaseID, saved.CaseRevisionID, Taipei.GeoNameID, true));
        Assert.Equal("failed", Scalar("SELECT Status FROM ProcessingStep"));
        Assert.Equal(2L, Scalar("SELECT count(*) FROM ExecutionCall WHERE ResponseJson IS NOT NULL"));
    }

    [Fact]
    public async Task ToolFailurePreservesTheOriginalErrorResponse()
    {
        StoredCase saved = new CaseRepository(PathName).Save(Input);
        FakeClient fake = new(_ => Step2Json.Element(new { isError = true, content = new[] { new { type = "text", text = "synthetic tool failure" } } }));
        await Assert.ThrowsAsync<Step2Exception>(() => Service(fake).ExecuteAsync(saved.NamingCaseID, saved.CaseRevisionID, Taipei.GeoNameID, true));
        Assert.Equal("failed", Scalar("SELECT Status FROM ProcessingRun"));
        Assert.Equal(1L, Scalar("SELECT count(*) FROM ExecutionCall WHERE Status='failed' AND json_extract(ResponseJson,'$.isError')=1"));
    }

    [Fact]
    public async Task CancellationKeepsPartialReceiptsAndAllowsANewAttempt()
    {
        StoredCase saved = new CaseRepository(PathName).Save(Input);
        using CancellationTokenSource stop = new();
        FakeClient fake = new(onChart: () => stop.Cancel());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(fake).ExecuteAsync(saved.NamingCaseID, saved.CaseRevisionID, Taipei.GeoNameID, true, stop.Token));
        Assert.Equal("cancelled", Scalar("SELECT Status FROM ProcessingRun"));
        Assert.Equal(2L, Scalar("SELECT count(*) FROM ExecutionCall"));
        await Service(new FakeClient()).ExecuteAsync(saved.NamingCaseID, saved.CaseRevisionID, Taipei.GeoNameID, true);
        Assert.Equal(2L, Scalar("SELECT count(*) FROM ProcessingRun"));
    }

    [Fact]
    public async Task ChangingTheCaseDuringCalculationCannotAttachResultToNewRevision()
    {
        CaseRepository cases = new(PathName);
        StoredCase saved = cases.Save(Input);
        FakeClient fake = new(onChart: () => cases.Save(Input with { BirthMinute = "15" }, saved.NamingCaseID, saved.CaseRevisionID));
        await Assert.ThrowsAsync<CaseConflictException>(() => Service(fake).ExecuteAsync(saved.NamingCaseID, saved.CaseRevisionID, Taipei.GeoNameID, true));
        Assert.Equal("cancelled", Scalar("SELECT Status FROM ProcessingRun"));
        Assert.Null(new Step2Service(PathName).Latest(cases.Load(saved.NamingCaseID).CaseRevisionID));
        Assert.Equal(2L, Scalar("SELECT count(*) FROM ExecutionCall WHERE ResponseJson IS NOT NULL"));
    }

    [Fact]
    public async Task ActualCSharpMcpClientCanCalculateAndPersistWithoutOpeningAWindow()
    {
        StoredCase saved = new CaseRepository(PathName).Save(Input);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(45));
        ChartRunResult result = await new Step2Service(PathName).ExecuteAsync(saved.NamingCaseID, saved.CaseRevisionID, Taipei.GeoNameID, true, deadline.Token);
        Assert.Equal("丙午", result.Chart!.Value.GetProperty("pillars").GetProperty("year").GetProperty("ganZhi").GetString());
        Assert.Equal("壬午", result.Chart.Value.GetProperty("pillars").GetProperty("hour").GetProperty("ganZhi").GetString());
        Assert.Equal(2L, Scalar("SELECT count(*) FROM ExecutionCall WHERE ToolVersion='0.3.1' AND Status='succeeded'"));
    }

    [Theory]
    [InlineData("US", "New York City", "2026-06-15")]
    [InlineData("台灣", "台北市", "1979-07-01")]
    public async Task ActualMcpAgreesWithLocalHistoricalDst(string country, string city, string date)
    {
        StoredCase saved = new CaseRepository(PathName).Save(Input with { BirthCountry = country, BirthCity = city, BirthDate = date });
        Step2Service service = new(PathName);
        var place = service.FindPlaces(saved.NamingCaseID, saved.CaseRevisionID).Single();
        ChartRunResult result = await service.ExecuteAsync(saved.NamingCaseID, saved.CaseRevisionID, place.GeoNameID, true);
        Assert.Equal("completed", result.Status);
        Assert.Equal(1, result.Input.GetProperty("time").GetProperty("dstOffsetHours").GetDouble());
        Assert.Equal(1, result.Chart!.Value.GetProperty("metadata").GetProperty("dstOffset").GetDouble());
    }

    [Fact]
    public async Task ActualSolarTimeCanCrossMidnightWithoutChangingOriginalBirthday()
    {
        StoredCase saved = new CaseRepository(PathName).Save(Input with { BirthHour = "23", BirthMinute = "58" });
        ChartRunResult result = await new Step2Service(PathName).ExecuteAsync(saved.NamingCaseID, saved.CaseRevisionID, Taipei.GeoNameID, true);
        Assert.Equal(16, result.Chart!.Value.GetProperty("calendar").GetProperty("calculationSolar").GetProperty("day").GetInt32());
        Assert.Equal(15, result.Chart.Value.GetProperty("calendar").GetProperty("civilSolar").GetProperty("day").GetInt32());
        Assert.Equal("2026-06-15", Scalar("SELECT BirthDate FROM CaseRevision"));
        Assert.Equal(1L, Scalar("SELECT json_extract(CalendarDataJson,'$.day') FROM CaseRevision"));
    }

    [Theory]
    [InlineData("timezone", "TIMEZONE_RULE_MISMATCH")]
    [InlineData("policy", "MCP_POLICY_MISMATCH")]
    [InlineData("fallback", "MCP_TIMING_UNAVAILABLE")]
    [InlineData("civil", "MCP_INPUT_MISMATCH")]
    [InlineData("calculation", "MCP_RESULT_INCOMPLETE")]
    [InlineData("timezoneBasis", "MCP_POLICY_MISMATCH")]
    [InlineData("stem", "MCP_RESULT_INCOMPLETE")]
    [InlineData("dayMaster", "MCP_RESULT_INCOMPLETE")]
    [InlineData("hiddenStem", "MCP_RESULT_INCOMPLETE")]
    public async Task MismatchedToolResultsKeepRawDataButCannotComplete(string problem, string code)
    {
        StoredCase saved = new CaseRepository(PathName).Save(Input);
        FakeClient fake = new(response =>
        {
            JsonNode node = JsonNode.Parse(response.GetRawText())!;
            JsonNode chart = node["structuredContent"]!["data"]!["chart"]!;
            switch (problem)
            {
                case "timezone": chart["daYun"]!["timing"]!["resolvedOffsetSeconds"] = 0; break;
                case "policy": chart["metadata"]!["dayBoundaryMode"] = "MIDNIGHT_00"; break;
                case "fallback": chart["daYun"]!["timing"]!["status"] = "UNAVAILABLE"; break;
                case "civil": chart["calendar"]!["civilSolar"]!["day"] = 16; break;
                case "calculation": chart["calendar"]!["calculationSolar"] = null; break;
                case "timezoneBasis": chart["metadata"]!["timezoneBasis"] = 9; break;
                case "stem": chart["pillars"]!["hour"]!["ganZhi"] = "不一致"; break;
                case "dayMaster": chart["dayMaster"]!["char"] = "錯"; break;
                case "hiddenStem": chart["pillars"]!["hour"]!["hiddenStems"]![0] = null; break;
            }
            node["content"]![0]!["text"] = node["structuredContent"]!.ToJsonString();
            return JsonSerializer.SerializeToElement(node);
        });
        Step2Exception error = await Assert.ThrowsAsync<Step2Exception>(() => Service(fake).ExecuteAsync(saved.NamingCaseID, saved.CaseRevisionID, Taipei.GeoNameID, true));
        Assert.Equal(code, error.Code);
        Assert.Equal("failed", Scalar("SELECT Status FROM ProcessingRun"));
        Assert.Equal(DBNull.Value, Scalar("SELECT OutputJson FROM ProcessingStep"));
        Assert.Equal(2L, Scalar("SELECT count(*) FROM ExecutionCall WHERE ResponseJson IS NOT NULL"));
    }

    [Fact]
    public async Task AmbiguousTimeFailsBeforeAnyMcpCallAndKeepsFailureReason()
    {
        StoredCase saved = new CaseRepository(PathName).Save(Input with { BirthCountry = "US", BirthCity = "New York City", BirthDate = "2026-11-01", BirthHour = "01", BirthMinute = "30" });
        FakeClient fake = new(); Step2Service service = Service(fake);
        long place = service.FindPlaces(saved.NamingCaseID, saved.CaseRevisionID).Single().GeoNameID;
        Step2Exception error = await Assert.ThrowsAsync<Step2Exception>(() => service.ExecuteAsync(saved.NamingCaseID, saved.CaseRevisionID, place, true));
        Assert.Equal("AMBIGUOUS_CIVIL_TIME", error.Code);
        Assert.Equal("failed", Scalar("SELECT Status FROM ProcessingRun"));
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task MissingRuntimeRecordsFailureAndCanBeRetried()
    {
        StoredCase saved = new CaseRepository(PathName).Save(Input);
        Step2Service unavailable = new(PathName, _ => throw new Step2Exception("MCP_RUNTIME_MISSING", "測試：缺少執行依賴"));
        Step2Exception error = await Assert.ThrowsAsync<Step2Exception>(() => unavailable.ExecuteAsync(saved.NamingCaseID, saved.CaseRevisionID, Taipei.GeoNameID, true));
        Assert.Equal("MCP_RUNTIME_MISSING", error.Code);
        Assert.Equal("failed", Scalar("SELECT Status FROM ProcessingStep"));
        Assert.Equal(0L, Scalar("SELECT count(*) FROM ExecutionCall"));
        await Service(new FakeClient()).ExecuteAsync(saved.NamingCaseID, saved.CaseRevisionID, Taipei.GeoNameID, true);
        Assert.Equal(2L, Scalar("SELECT count(*) FROM ProcessingRun"));
    }

    private Step2Service Service(FakeClient fake) => new(PathName, _ => Task.FromResult<IOpenFateClient>(fake));
    private object Scalar(string sql)
    {
        using SqliteConnection db = new(new SqliteConnectionStringBuilder { DataSource = PathName, Pooling = false }.ToString());
        db.Open(); using SqliteCommand command = db.CreateCommand(); command.CommandText = sql;
        return command.ExecuteScalar()!;
    }

    private sealed class FakeClient(Func<JsonElement, JsonElement>? change = null, Action? onChart = null) : IOpenFateClient
    {
        public List<(string Tool, IReadOnlyDictionary<string, object?> Arguments)> Calls { get; } = [];
        public string ServerVersion => "0.3.1";
        public Task<JsonElement> CallAsync(string tool, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
        {
            Calls.Add((tool, arguments));
            using Stream fixture = Assembly.GetExecutingAssembly().GetManifestResourceStream("Step2Fixture")!;
            using JsonDocument document = JsonDocument.Parse(fixture);
            JsonElement result = document.RootElement.GetProperty(tool == "get_openfate_bazi_policy" ? "policy" : "response").Clone();
            if (tool == "calculate_bazi_chart")
            {
                onChart?.Invoke(); cancellationToken.ThrowIfCancellationRequested();
                if (change is not null) result = change(result);
            }
            return Task.FromResult(result);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "OnlyFate.Database.Tests")) + Path.DirectorySeparatorChar;
        string target = Path.GetFullPath(directory);
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe cleanup path.");
        if (Directory.Exists(target)) Directory.Delete(target, true);
    }
}
