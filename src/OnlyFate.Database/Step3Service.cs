using System.Text.Json;

namespace OnlyFate.Database;

public sealed class Step3Service(string databasePath)
{
    private readonly CaseRepository cases = new(databasePath);
    private readonly ChartRunRepository runs = new(databasePath);
    public ChartRunResult? Latest(long revisionId)
    {
        var result = runs.Latest(revisionId, 3);
        var chart = runs.Latest(revisionId);
        if (result?.Status == "completed" && (chart?.Status != "completed" || chart.ProcessingRunID != result.Input.GetProperty("sourceChartRunId").GetInt64()))
            return result with { Status = "cancelled", Chart = null, ErrorMessage = "此判讀使用的命盤已被重排取代，請重新判讀。" };
        return result;
    }

    public JsonElement Prepare(long caseId, long revisionId)
    {
        StoredCase saved = cases.Load(caseId);
        if (saved.CaseRevisionID != revisionId) throw new CaseConflictException();
        ChartRunResult chart = runs.Latest(revisionId) ?? throw new InvalidOperationException("請先完成這個版本的排盤。");
        if (chart.Status != "completed" || chart.Chart is null) throw new InvalidOperationException("最新排盤未成功，不能開始判讀。");
        var input = saved.Input;
        var c = chart.Chart.Value;
        return Step2Json.Element(new {
            caseId, revisionId, sourceChartRunId = chart.ProcessingRunID,
            policy = new { scope = "step3_trial", ruleVersion = AnalysisContract.Version, schemaVersion = "analysis-output-v2", rulesValidated = false },
            caseData = new { input.Surname, input.Gender, nameLength = 2, input.AvoidCharacters, input.AvoidMatchMode,
                input.NoAvoidanceCharacters, input.RequiredCharacter, input.RequiredPosition, input.NoRequiredCharacter },
            facts = new { pillars = c.GetProperty("pillars"), dayMaster = c.GetProperty("dayMaster"), calendar = c.GetProperty("calendar"), metadata = c.GetProperty("metadata") },
            remarksRaw = input.Remarks
        });
    }

    public Task<ChartRunResult> ExecuteAsync(long caseId,long revisionId,string model,ModelConnection connection,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();
        var context=Prepare(caseId,revisionId);
        return new ModelCallRunner(databasePath).RunAsync(caseId,revisionId,3,model,connection,context,AnalysisContract.Version,AnalysisContract.Request,
            (prose,input)=>AnalysisContract.Validate(prose,input.GetProperty("facts"),input.GetProperty("remarksRaw").GetString()!),
            (run,step,prose)=>{
                if(Prepare(caseId,revisionId).GetProperty("sourceChartRunId").GetInt64()!=context.GetProperty("sourceChartRunId").GetInt64())throw new CaseConflictException();
                var output=Step2Json.Element(new{canProceed=false,canTrialProceed=AnalysisContract.CanTrialProceed(prose),ruleStatus="experimental",analysis=prose});
                runs.End(run,step,"completed",output,null,null,caseId,revisionId,context.GetProperty("sourceChartRunId").GetInt64());
                return output;
            },token);
    }
}
