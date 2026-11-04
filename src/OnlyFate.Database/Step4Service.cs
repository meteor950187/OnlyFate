using System.Text.Json;

namespace OnlyFate.Database;

public sealed class Step4Service(string databasePath)
{
    private readonly CaseRepository cases = new(databasePath);
    private readonly ChartRunRepository runs = new(databasePath);
    public ChartRunResult? Latest(long revisionId)
    {
        var result=runs.Latest(revisionId,4);
        if(result?.Status!="completed") return result;
        if(!result.Input.TryGetProperty("policy",out var policy) || !policy.TryGetProperty("schemaVersion",out var schema) || schema.GetString()!="naming-output-v2" || !policy.TryGetProperty("ruleVersion",out var rule) || rule.GetString()!=NamingContract.Version)
            return result with {Status="cancelled",Chart=null,ErrorMessage="舊版選名已過期，請重新選名；示範請建立新案例。"};
        var chart=runs.Latest(revisionId);
        var analysis=new Step3Service(databasePath).Latest(revisionId);
        if(chart?.Status!="completed" || analysis?.Status!="completed"
            || chart.ProcessingRunID!=result.Input.GetProperty("sourceChartRunId").GetInt64()
            || analysis.ProcessingRunID!=result.Input.GetProperty("sourceAnalysisRunId").GetInt64())
            return result with {Status="cancelled",Chart=null,ErrorMessage="案件、命盤或判讀已更新，請重新選名。"};
        return result;
    }

    public JsonElement Prepare(long caseId,long revisionId)
    {
        StoredCase saved=cases.Load(caseId);
        if(saved.CaseRevisionID!=revisionId) throw new CaseConflictException();
        var chart=runs.Latest(revisionId);
        var analysis=new Step3Service(databasePath).Latest(revisionId);
        if(chart?.Status!="completed" || analysis?.Status!="completed" || analysis.Chart is null)
            throw new InvalidOperationException("請先完成目前版本的排盤與判讀。");
        var result=analysis.Chart.Value;
        if(!result.TryGetProperty("canTrialProceed",out var trial) || trial.ValueKind!=JsonValueKind.True
            || !result.TryGetProperty("analysis",out var content) || !AnalysisContract.CanTrialProceed(content))
            throw new InvalidOperationException("判讀缺少選字五行或存在實際命局阻礙，不能開始試行選名；請重新判讀。");
        string[] elements=content.GetProperty("namingElements").EnumerateArray().Select(e=>e.GetString()!).ToArray();
        var pool=NamingEngine.Generate(saved.Input,elements);
        if(pool.Candidates.Count==0) throw new InvalidOperationException(string.Join("；",pool.Warnings));
        var pools=NamingPools.Generate(pool.Candidates);
        NamingPools.RequireTwo(pools);
        return Step2Json.Element(new{
            caseId,revisionId,sourceChartRunId=chart.ProcessingRunID,sourceAnalysisRunId=analysis.ProcessingRunID,
            policy=new{scope="step4_trial",ruleVersion=NamingContract.Version,schemaVersion="naming-output-v2",rulesValidated=false,dictionaryVersion=pool.DictionaryVersion},
            caseData=new { saved.Input.Surname, saved.Input.Gender, nameLength=2,
                saved.Input.AvoidCharacters, saved.Input.AvoidMatchMode, saved.Input.NoAvoidanceCharacters,
                saved.Input.RequiredCharacter, saved.Input.RequiredPosition, saved.Input.NoRequiredCharacter },
            analysis=new { namingElements=elements },
            pools,candidates=pool.Candidates,coverage=pool.Coverage,warnings=pool.Warnings,
            remarksRaw=saved.Input.Remarks
        });
    }

    public Task<ChartRunResult> ExecuteAsync(long caseId,long revisionId,string model,IAnalysisClient client,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();
        var context=Prepare(caseId,revisionId);
        return new ModelCallRunner(databasePath).RunAsync(caseId,revisionId,4,model,client,context,NamingContract.Version,NamingContract.Request,NamingContract.Validate,
            (run,step,prose)=> {
                var current=Prepare(caseId,revisionId);
                foreach(string field in new[]{"sourceChartRunId","sourceAnalysisRunId"})
                    if(current.GetProperty(field).GetInt64()!=context.GetProperty(field).GetInt64())throw new CaseConflictException();
                var output=Step2Json.Element(new{
                    canProceed=false,canTrialProceed=true,ruleStatus="experimental",naming=prose,
                    dictionaryVersion=context.GetProperty("policy").GetProperty("dictionaryVersion"),ruleVersion=NamingContract.Version,
                    schemaVersion="naming-output-v2",pools=context.GetProperty("pools"),candidates=context.GetProperty("candidates"),coverage=context.GetProperty("coverage"),warnings=context.GetProperty("warnings")
                });
                runs.End(run,step,"completed",output,null,null,caseId,revisionId,context.GetProperty("sourceChartRunId").GetInt64(),context.GetProperty("sourceAnalysisRunId").GetInt64());
                return output;
            },token);
    }
}
