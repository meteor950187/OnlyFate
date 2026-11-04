using System.Diagnostics;
using System.Text.Json;

namespace OnlyFate.Database;

public sealed class Step2Service
{
    private readonly CaseRepository cases;
    private readonly ChartRunRepository runs;
    private readonly Func<CancellationToken, Task<IOpenFateClient>> clientFactory;

    public Step2Service(string databasePath, Func<CancellationToken, Task<IOpenFateClient>>? clientFactory = null)
    {
        cases = new(databasePath);
        runs = new(databasePath);
        this.clientFactory = clientFactory ?? OpenFateMcpClient.CreateAsync;
    }

    private StoredCase Current(long caseId, long revisionId)
    {
        StoredCase saved = cases.Load(caseId);
        if (saved.CaseRevisionID != revisionId) throw new CaseConflictException();
        return saved;
    }

    public IReadOnlyList<BirthplaceCandidate> FindPlaces(long caseId, long revisionId)
    {
        CaseInput input = Current(caseId, revisionId).Input;
        return BirthplaceCatalog.Default.Find(input.BirthCountry, input.BirthCity);
    }

    public ChartRunResult? Latest(long revisionId) => runs.Latest(revisionId);

    public async Task<ChartRunResult> ExecuteAsync(long caseId, long revisionId, long geoNameId, bool acceptCityPoint, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CaseInput original = Current(caseId, revisionId).Input;
        if (!acceptCityPoint) throw new Step2Exception("LOCATION_CONFIRMATION_REQUIRED", "請確認採用城市代表點；詳細地址尚未定位，不能直接開始排盤。");
        BirthplaceCandidate place = BirthplaceCatalog.Default.Find(original.BirthCountry, original.BirthCity).SingleOrDefault(p => p.GeoNameID == geoNameId)
            ?? throw new Step2Exception("LOCATION_NOT_CONFIRMED", "請從這個案件的出生地候選中重新選擇城市。");
        var policy = new
        {
            scope = "step2_only", version = "step2-v1", enableTrueSolarTime = true, dayBoundaryMode = "ZI_HOUR_23",
            calculationSecond = 0, originalSecond = (int?)null, inputPrecision = "minute",
            locationPrecision = "city_representative_point", locationConfirmed = true
        };
        JsonElement input = Step2Json.Element(new { caseId, revisionId, original, place, policy, detailsResolved = false });
        (long run, long step) = runs.Start(caseId, revisionId, input);
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            BirthTimeReceipt time = BirthTimeResolver.Resolve(original, place.TimeZoneID);
            IReadOnlyDictionary<string, object?> arguments = BirthTimeResolver.McpArguments(original, place, time);
            input = Step2Json.Element(new { caseId, revisionId, original, place, policy, detailsResolved = false, time, arguments });
            runs.SetInput(step, input);
            JsonElement response;
            await using (IOpenFateClient client = await clientFactory(deadline.Token))
            {
                JsonElement receipt = await Invoke(client, step, 1, "get_openfate_bazi_policy", new Dictionary<string, object?>(), deadline.Token);
                JsonElement actualPolicy = ChartValidator.Payload(receipt).GetProperty("data").GetProperty("policy");
                if (actualPolicy.GetProperty("daYunTimingVersion").GetString() != "DAYUN_SECOND_V2")
                    throw new Step2Exception("MCP_POLICY_MISMATCH", "OpenFate 的時間計算政策與本次驗證版本不同。");
                response = await Invoke(client, step, 2, "calculate_bazi_chart", arguments, deadline.Token);
            }
            JsonElement chart = ChartValidator.Validate(response, arguments, time);
            cancellationToken.ThrowIfCancellationRequested();
            runs.End(run, step, "completed", chart, null, null, caseId, revisionId);
            return new(run, revisionId, "completed", input, chart, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            runs.End(run, step, "cancelled", null, "CANCELLED", "使用者已取消排盤。");
            throw;
        }
        catch (CaseConflictException)
        {
            runs.End(run, step, "cancelled", null, "CASE_REVISION_CHANGED", "排盤期間案件已更新，結果未套用到新版。");
            throw;
        }
        catch (Exception exception)
        {
            string code = exception is Step2Exception failure ? failure.Code : exception is OperationCanceledException ? "MCP_TIMEOUT" : "MCP_CALL_FAILED";
            string message = exception is Step2Exception ? exception.Message : exception is OperationCanceledException
                ? "排盤超過 45 秒，請稍後重試。" : "OpenFate 排盤未完成，請確認 Node.js 與本機 MCP 依賴，再重試。";
            runs.End(run, step, "failed", null, code, message);
            throw new Step2Exception(code, message);
        }
    }

    private async Task<JsonElement> Invoke(IOpenFateClient client, long step, int number, string name, IReadOnlyDictionary<string, object?> arguments, CancellationToken token)
    {
        long call = runs.StartCall(step, number, name, arguments, client.ServerVersion);
        Stopwatch watch = Stopwatch.StartNew();
        JsonElement response;
        try { response = await client.CallAsync(name, arguments, token); }
        catch (Exception exception)
        {
            runs.FinishCall(call, exception is OperationCanceledException ? "cancelled" : "failed", null, watch.ElapsedMilliseconds,
                exception is OperationCanceledException ? "CANCELLED" : "MCP_TRANSPORT_ERROR");
            throw;
        }
        bool failed = response.TryGetProperty("isError", out JsonElement error) && error.ValueKind == JsonValueKind.True;
        runs.FinishCall(call, failed ? "failed" : "succeeded", response, watch.ElapsedMilliseconds, failed ? "MCP_TOOL_ERROR" : null);
        if (failed) throw new Step2Exception("MCP_TOOL_ERROR", "OpenFate 回報工具錯誤，原始回傳已保留。");
        return response;
    }
}
