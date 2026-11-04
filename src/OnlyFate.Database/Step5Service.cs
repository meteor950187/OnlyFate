using System.Text.Json;
namespace OnlyFate.Database;

// Composes only validated fixed data. Dictionary facts never come from model prose.
public static class ReportDataAssembler
{
    public const string TemplateVersion="parent-report-pools-v12";
    public static JsonElement Build(StoredCase saved,JsonElement chart,ChartRunResult naming,JsonElement prose,ChartRunResult analysis,string sourceKind="model")
    {
        if(sourceKind is not ("model" or "offline-fictional-demo"))throw new ArgumentException("未知來源標記。");
        if(saved.CaseRevisionID!=naming.CaseRevisionID||saved.NamingCaseID!=naming.Input.GetProperty("caseId").GetInt64()||naming.Status!="completed"||naming.Chart is null)throw new InvalidOperationException("選名來源無效。");
        var basis=ReportNamingBasis.Build(saved,chart,naming,analysis);
        var selection=naming.Chart.Value;
        if(NamingContract.Validate(selection.GetProperty("naming"),naming.Input).Count!=0)throw new InvalidOperationException("選名資料未通過檢查。");
        var context=ProseContext(naming);var errors=ReportContract.Validate(prose,context);
        if(errors.Count!=0)throw new InvalidOperationException(string.Join("；",errors));
        var candidates=naming.Input.GetProperty("candidates").EnumerateArray().ToDictionary(x=>x.GetProperty("id").GetString()!);
        var pools=naming.Input.GetProperty("pools").EnumerateArray().ToDictionary(x=>x.GetProperty("poolId").GetString()!);
        var proseGroups=prose.GetProperty("groups").EnumerateArray().ToDictionary(x=>x.GetProperty("poolId").GetString()!);
        var groups=selection.GetProperty("naming").GetProperty("groups").EnumerateArray().Select(g=>{
            string id=g.GetProperty("poolId").GetString()!;var pool=pools[id];
            var text=proseGroups[id].GetProperty("entries").EnumerateArray().ToDictionary(x=>x.GetProperty("candidateId").GetString()!);
            object Entry(JsonElement choice) {string candidateId=choice.GetProperty("candidateId").GetString()!;return new{candidate=candidates[candidateId],wish=text[candidateId].GetProperty("wish").GetString(),reason=text[candidateId].GetProperty("reason").GetString()};}
            var first=pool.GetProperty("firstCharacters")[0];var last=pool.GetProperty("lastCharacters")[0];
            var candidate=candidates[pool.GetProperty("pairIds")[0].GetString()!];
            return new{id,label=g.GetProperty("label").GetString(),direction=g.GetProperty("direction").GetString(),
                firstCharacters=pool.GetProperty("firstCharacters"),lastCharacters=pool.GetProperty("lastCharacters"),
                recommendation=Entry(g.GetProperty("recommendation")),alternatives=g.GetProperty("alternatives").EnumerateArray().Select(Entry).ToArray(),
                combinations=pool.GetProperty("pairIds").EnumerateArray().Select(x=>candidates[x.GetString()!]).ToArray(),
                configuration=new{surnameStrokes=candidate.GetProperty("surname").GetProperty("kangxiStrokes").GetInt32(),firstStrokes=first.GetProperty("kangxiStrokes").GetInt32(),lastStrokes=last.GetProperty("kangxiStrokes").GetInt32(),grids=candidate.GetProperty("grids")}};
        }).ToArray();
        return Step2Json.Element(new{
            schemaVersion="report-data-v2",formalDeliverable=false,ruleStatus="experimental",sourceKind,
            source=new{caseId=saved.NamingCaseID,revisionId=saved.CaseRevisionID,sourceChartRunId=naming.Input.GetProperty("sourceChartRunId").GetInt64(),sourceAnalysisRunId=naming.Input.GetProperty("sourceAnalysisRunId").GetInt64(),sourceNamingRunId=naming.ProcessingRunID},
            versions=new{templateVersion=TemplateVersion,ruleVersion=selection.GetProperty("ruleVersion"),dictionaryVersion=selection.GetProperty("dictionaryVersion"),proseVersion=ReportContract.Version},
            caseData=new{saved.Input.Surname,saved.Input.Gender,saved.Input.BirthDate,saved.Input.BirthHour,saved.Input.BirthMinute,saved.Input.BirthCity},
            pillars=chart.GetProperty("pillars"),namingBasis=basis,groups,summary=prose.GetProperty("summary").GetString(),coverage=selection.GetProperty("coverage"),warnings=selection.GetProperty("warnings")
        });
    }
    public static JsonElement ProseContext(ChartRunResult naming)
    {
        var data=naming.Chart!.Value;
        if(NamingContract.Validate(data.GetProperty("naming"),naming.Input).Count!=0)throw new InvalidOperationException("選名來源未通過檢查。");
        var candidates=naming.Input.GetProperty("candidates").EnumerateArray().ToDictionary(x=>x.GetProperty("id").GetString()!);
        return Step2Json.Element(new{
            caseId=naming.Input.GetProperty("caseId"),revisionId=naming.CaseRevisionID,
            sourceChartRunId=naming.Input.GetProperty("sourceChartRunId"),sourceAnalysisRunId=naming.Input.GetProperty("sourceAnalysisRunId"),sourceNamingRunId=naming.ProcessingRunID,
            policy=new{ruleVersion=ReportContract.Version,ruleStatus="experimental"},
            groups=data.GetProperty("naming").GetProperty("groups").EnumerateArray().Select(g=>new{poolId=g.GetProperty("poolId").GetString(),entries=new[]{g.GetProperty("recommendation")}.Concat(g.GetProperty("alternatives").EnumerateArray()).Select(x=>{
                string id=x.GetProperty("candidateId").GetString()!;return new{candidateId=id,selectionReason=x.GetProperty("reason").GetString(),first=Meaning(candidates[id].GetProperty("first")),last=Meaning(candidates[id].GetProperty("last"))};
            }).ToArray()}).ToArray(),
            appliedPreferences=data.GetProperty("naming").GetProperty("remarks").EnumerateArray().Where(x=>x.GetProperty("disposition").GetString()=="applied").Select(x=>new{quote=x.GetProperty("quote").GetString(),reason=x.GetProperty("reason").GetString()}).ToArray(),
            forbiddenNames=candidates.Values.Select(x=>x.GetProperty("name").GetString()).ToArray()
        });
        static object Meaning(JsonElement c)=>new{character=c.GetProperty("character").GetString(),meaning=c.GetProperty("meaning").GetString(),profile=new{literalMeaning=c.GetProperty("profile").GetProperty("literalMeaning").GetString(),namingWish=c.GetProperty("profile").GetProperty("namingWish").GetString(),style=c.GetProperty("profile").GetProperty("style").GetString()}};
    }
}

public sealed class Step5Service(string databasePath)
{
    public JsonElement Prepare(long caseId,long revisionId)
    {
        var saved=new CaseRepository(databasePath).Load(caseId);
        if(saved.CaseRevisionID!=revisionId)throw new CaseConflictException();
        var naming=new Step4Service(databasePath).Latest(revisionId);
        if(naming?.Status!="completed"||naming.Chart is null)throw new InvalidOperationException("請先完成目前版本的選名。");
        return ReportDataAssembler.ProseContext(naming);
    }
    public Task<ChartRunResult> ExecuteAsync(long caseId,long revisionId,string model,IAnalysisClient client,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();var context=Prepare(caseId,revisionId);
        return new ModelCallRunner(databasePath).RunAsync(caseId,revisionId,5,model,client,context,ReportContract.Version,ReportContract.Request,ReportContract.Validate,
            (run,step,prose)=>{
                var current=Prepare(caseId,revisionId);
                if(current.GetProperty("sourceNamingRunId").GetInt64()!=context.GetProperty("sourceNamingRunId").GetInt64())throw new CaseConflictException();
                var naming=new Step4Service(databasePath).Latest(revisionId)!;
                var chart=new ChartRunRepository(databasePath).Latest(revisionId)!;
                var analysis=new Step3Service(databasePath).Latest(revisionId)!;
                var data=ReportDataAssembler.Build(new CaseRepository(databasePath).Load(caseId),chart.Chart!.Value,naming,prose,analysis);
                var report=new ReportRepository(databasePath).Complete(run,step,caseId,revisionId,context,data);
                return Step2Json.Element(new{reportId=report.NamingReportID,formalDeliverable=false,ruleStatus="experimental",reportData=data});
            },token);
    }
    public ChartRunResult? Latest(long revisionId)
    {
        var result=new ChartRunRepository(databasePath).Latest(revisionId,5);
        if(result?.Status!="completed")return result;
        if(!result.Input.TryGetProperty("policy",out var policy)||!policy.TryGetProperty("ruleVersion",out var version)||version.GetString()!=ReportContract.Version)
            return result with{Status="cancelled",Chart=null,ErrorMessage="舊版報告已過期，請重新選名及撰寫報告；示範請建立新案例。"};
        var naming=new Step4Service(databasePath).Latest(revisionId);
        if(naming?.Status!="completed"||naming.ProcessingRunID!=result.Input.GetProperty("sourceNamingRunId").GetInt64())
            return result with{Status="cancelled",Chart=null,ErrorMessage="案件或上游資料已更新，請重新撰寫報告。"};
        var report=new ReportRepository(databasePath).LatestForRun(result.ProcessingRunID);
        return result with{Chart=Step2Json.Element(new{reportId=report.NamingReportID,formalDeliverable=false,ruleStatus="experimental",reportData=report.Data})};
    }
}
