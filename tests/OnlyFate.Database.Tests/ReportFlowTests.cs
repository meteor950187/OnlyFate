using System.Text.Json;
using Microsoft.Data.Sqlite;
using OnlyFate.Database;
using Xunit;

namespace OnlyFate.Database.Tests;

public sealed class ReportFlowTests : IDisposable
{
    private readonly string folder=Path.Combine(Path.GetTempPath(),"OnlyFate.Report.Tests",Guid.NewGuid().ToString("N"));
    private string Db=>Path.Combine(folder,"test.db");
    private static CaseInput Input=>new(){Surname="林",Gender="male",BirthDate="2026-06-15",BirthHour="12",BirthMinute="00",BirthCountry="台灣",BirthCity="台北市",Remarks="希望文雅",NoAvoidanceCharacters=true,NoRequiredCharacter=true};
    private StoredCase Seed(bool blocked=false,bool legacy=false)
    {
        var saved=new CaseRepository(Db).Save(Input);
        using var fixture=JsonDocument.Parse(typeof(ReportFlowTests).Assembly.GetManifestResourceStream("Step2Fixture")!);
        var chart=fixture.RootElement.GetProperty("response").GetProperty("structuredContent").GetProperty("data").GetProperty("chart");
        Write("INSERT INTO ProcessingRun(forCaseRevisionID,Status) VALUES($revision,'waiting'); INSERT INTO ProcessingStep(forProcessingRunID,StepNumber,InputJson,OutputJson,Status) VALUES(last_insert_rowid(),2,'{}',$output,'completed');",saved.CaseRevisionID,chart.GetRawText());
        var analysis=Step2Json.Element(new {canProceed=false,canTrialProceed=true,ruleStatus="experimental",analysis=new{summary="依已記錄日主試用土金。",direction="研究用土金方向，尚未驗證。",limitations=new[]{"試行判讀"},evidence=new[]{new{ruleId="R1",factPath="/dayMaster/char",factValue="庚",reason="已記錄日主為庚。"}},remarks=Array.Empty<object>(),blockingIssues=blocked?new[]{"寒暖方向矛盾"}:Array.Empty<string>(),namingElements=new[]{"金","土"}}});
        if(legacy) analysis=Step2Json.Element(new{canProceed=false,analysis=new{blockingIssues=Array.Empty<string>()}});
        Write("INSERT INTO ProcessingRun(forCaseRevisionID,Status) VALUES($revision,'waiting'); INSERT INTO ProcessingStep(forProcessingRunID,StepNumber,InputJson,OutputJson,Status) VALUES(last_insert_rowid(),3,'{\"sourceChartRunId\":1}',$output,'completed');",saved.CaseRevisionID,analysis.GetRawText());
        return saved;
    }
    private void Write(string sql,long revision=0,string output="{}")
    {
        using var db=new SqliteConnection($"Data Source={Db};Pooling=False"); db.Open(); using var c=db.CreateCommand();c.CommandText=sql;c.Parameters.AddWithValue("$revision",revision);c.Parameters.AddWithValue("$output",output);c.ExecuteNonQuery();
    }
    private object Scalar(string sql)
    {
        using var db=new SqliteConnection($"Data Source={Db};Pooling=False");db.Open();using var c=db.CreateCommand();c.CommandText=sql;return c.ExecuteScalar()!;
    }
    private sealed class Client(Func<JsonElement,int,AiResponse> send):IAnalysisClient
    {
        public List<JsonElement> Requests{get;}=[];
        public Task<AiResponse> SendAsync(JsonElement request,CancellationToken token){Requests.Add(request);return Task.FromResult(send(request,Requests.Count));}
        public static implicit operator ModelConnection(Client client) => new(OpenAiResponsesAdapter.Instance, client);
    }
    private static AiResponse Reply(JsonElement request,bool valid=true)
    {
        using var input=JsonDocument.Parse(request.GetProperty("input")[0].GetProperty("content").GetString()!);
        var output=PoolTestData.Selection(input.RootElement.GetProperty("context"),valid);
        return new(200,Step2Json.Element(new{id="fake-test",status="completed",output=new[]{new{type="message",content=new[]{new{type="output_text",text=output.GetRawText()}}}}}));
    }
    private async Task<(StoredCase Saved,long Report)> CreateReport()
    {
        var saved=Seed();await new Step4Service(Db).ExecuteAsync(saved.NamingCaseID,saved.CaseRevisionID,"fake",new Client((r,n)=>Reply(r)));
        var result=await new Step5Service(Db).ExecuteAsync(saved.NamingCaseID,saved.CaseRevisionID,"fake",new Client((r,n)=>ReportReply(r)));
        return(saved,result.Chart!.Value.GetProperty("reportId").GetInt64());
    }
    private static AiResponse ReportReply(JsonElement request,string? text=null,bool illegal=false)
    {
        using var body=JsonDocument.Parse(request.GetProperty("input")[0].GetProperty("content").GetString()!);
        var node=System.Text.Json.Nodes.JsonNode.Parse(PoolTestData.Prose(body.RootElement.GetProperty("context")).GetRawText())!;
        if(illegal)node["groups"]![0]!["entries"]![0]!["candidateId"]="invented";
        var output=JsonSerializer.SerializeToElement(node);
        return new(200,Step2Json.Element(new{status="completed",output=new[]{new{type="message",content=new[]{new{type="output_text",text=text??output.GetRawText()}}}}}));
    }
    [Fact] public async Task SuccessReopensWithHashAndSmallContext()
    {
        var (saved,id)=await CreateReport();var report=new ReportRepository(Db).Load(id);
        Assert.False(report.Data.GetProperty("formalDeliverable").GetBoolean());
        Assert.Equal("experimental",report.Data.GetProperty("ruleStatus").GetString());
        Assert.Equal(id,new Step5Service(Db).Latest(saved.CaseRevisionID)!.Chart!.Value.GetProperty("reportId").GetInt64());
        Assert.Equal(64,report.ContentSha256.Length);
        string context=new Step5Service(Db).Prepare(saved.NamingCaseID,saved.CaseRevisionID).GetRawText();
        foreach(string field in new[]{"birthDate","birthHour","birthCity","birthLocationDetail","kangxiStrokes","grids","calendar"})Assert.DoesNotContain(field,context);
        Assert.Contains("formalDeliverable",Scalar("SELECT ValidationJson FROM NamingReport").ToString());
        Write("UPDATE NamingReport SET ReportDataJson=json_set(ReportDataJson,'$.summary','changed')");
        Assert.Throws<InvalidOperationException>(()=>new ReportRepository(Db).Load(id));
    }
    [Theory] [InlineData("bad json")] [InlineData("{}")]
    public async Task MalformedRepairsOnlyOnce(string bad)
    {
        var saved=Seed();await new Step4Service(Db).ExecuteAsync(saved.NamingCaseID,saved.CaseRevisionID,"fake",new Client((r,n)=>Reply(r)));
        var c=new Client((r,n)=>ReportReply(r,bad));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new Step5Service(Db).ExecuteAsync(saved.NamingCaseID,saved.CaseRevisionID,"fake",c));
        Assert.Equal(2,c.Requests.Count);Assert.Equal(0L,Scalar("SELECT count(*) FROM NamingReport"));
    }
    [Fact] public async Task IllegalIdRepairsAndPersistsBothCalls()
    {
        var saved=Seed();await new Step4Service(Db).ExecuteAsync(saved.NamingCaseID,saved.CaseRevisionID,"fake",new Client((r,n)=>Reply(r)));
        var c=new Client((r,n)=>ReportReply(r,illegal:n==1));
        await new Step5Service(Db).ExecuteAsync(saved.NamingCaseID,saved.CaseRevisionID,"fake",c);
        Assert.Equal(2,c.Requests.Count);Assert.Contains("previousOutput",c.Requests[1].GetRawText());
        Assert.Equal(2L,Scalar("SELECT count(*) FROM ExecutionCall WHERE PromptVersion='report-prose-pools-v2' AND ResponseJson IS NOT NULL"));
    }
    private void Change(StoredCase saved,int change)
    {
        if(change==1)new CaseRepository(Db).Save(Input with{BirthMinute="15"},saved.NamingCaseID,saved.CaseRevisionID);
        else Write($"INSERT INTO ProcessingRun(forCaseRevisionID,Status) VALUES($revision,'running'); INSERT INTO ProcessingStep(forProcessingRunID,StepNumber,InputJson,Status) VALUES(last_insert_rowid(),{change},'{{}}','running');",saved.CaseRevisionID);
    }
    [Theory] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task ChangesDuringModelCallNeverInsertReadyReport(int change)
    {
        var saved=Seed();await new Step4Service(Db).ExecuteAsync(saved.NamingCaseID,saved.CaseRevisionID,"fake",new Client((r,n)=>Reply(r)));
        var c=new Client((r,n)=>{Change(saved,change);return ReportReply(r);});
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new Step5Service(Db).ExecuteAsync(saved.NamingCaseID,saved.CaseRevisionID,"fake",c));
        Assert.Equal(0L,Scalar("SELECT count(*) FROM NamingReport"));
    }
    [Theory] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task ReopeningRejectsEveryUpstreamChange(int change)
    {
        var(saved,id)=await CreateReport();Change(saved,change);
        Assert.Equal("cancelled",new Step5Service(Db).Latest(saved.CaseRevisionID)!.Status);
        Assert.ThrowsAny<InvalidOperationException>(()=>new ReportRepository(Db).Load(id));
    }
    [Theory] [InlineData(200)] [InlineData(429)]
    public async Task RefusalAndHttpDoNotRepair(int status)
    {
        var saved=Seed();await new Step4Service(Db).ExecuteAsync(saved.NamingCaseID,saved.CaseRevisionID,"fake",new Client((r,n)=>Reply(r)));
        var c=new Client((r,n)=>new(status,Step2Json.Element(new{status="completed",output=new[]{new{type="message",content=new[]{new{type="refusal",refusal="test"}}}}})));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new Step5Service(Db).ExecuteAsync(saved.NamingCaseID,saved.CaseRevisionID,"fake",c));Assert.Single(c.Requests);
    }
    [Fact] public async Task CancellationClosesReportRun()
    {
        var saved=Seed();await new Step4Service(Db).ExecuteAsync(saved.NamingCaseID,saved.CaseRevisionID,"fake",new Client((r,n)=>Reply(r)));
        using var stop=new CancellationTokenSource();var c=new Client((r,n)=>{stop.Cancel();throw new OperationCanceledException(stop.Token);});
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new Step5Service(Db).ExecuteAsync(saved.NamingCaseID,saved.CaseRevisionID,"fake",c,stop.Token));
        Assert.Equal("cancelled",Scalar("SELECT Status FROM ProcessingStep WHERE StepNumber=5"));Assert.Equal(0L,Scalar("SELECT count(*) FROM NamingReport"));
    }
    [Fact] public async Task HtmlEncodesInjectionAndDoesNotRenderUnselectedOrInternalIds()
    {
        var(_,id)=await CreateReport();var d=new ReportRepository(Db).Load(id).Data;
        var mutable=System.Text.Json.Nodes.JsonNode.Parse(d.GetRawText())!;
        mutable["groups"]![0]!["recommendation"]!["reason"]="<img src=https://evil>";
        string html=ReportHtmlRenderer.Render(JsonSerializer.SerializeToElement(mutable));
        Assert.DoesNotContain("<img",html);Assert.Contains("&lt;img",html);Assert.Contains("default-src 'none'",html);Assert.Contains("size:A4",html);
        Assert.DoesNotContain("candidateId",html);Assert.DoesNotContain("limited-zh-TW-v1-",html);
        Assert.DoesNotContain("<script",html);Assert.Contains("試行",html);
    }
    [Fact] public async Task HtmlAndPdfCompleteSeparatelyAndRetryIsImmutable()
    {
        var(_,id)=await CreateReport();var service=new ReportArtifactService(Db,Path.Combine(folder,"reports"));
        var html=service.EnsureHtml(id);Assert.True(File.Exists(html.FullPath));Assert.Null(service.GetReady(id,"pdf"));
        Assert.Equal(html,service.EnsureHtml(id));
        // Deliberately tiny fake PDF: storage contract only, not PDF visual verification.
        var pdf=await service.GeneratePdfAsync(id,(rendered,t)=>{Assert.Equal(File.ReadAllText(html.FullPath),rendered);return Task.FromResult(System.Text.Encoding.ASCII.GetBytes("%PDF-1.4 fake storage fixture"));});
        var again=await service.GeneratePdfAsync(id,(_,_)=>throw new Exception("must not call again"));Assert.Equal(pdf,again);
        Assert.Equal(2L,Scalar("SELECT count(*) FROM ReportArtifact WHERE Status='ready'"));
        Assert.Contains("pdfReady",Scalar("SELECT OutputJson FROM ProcessingStep WHERE StepNumber=6 ORDER BY ProcessingStepID DESC LIMIT 1").ToString());
        File.AppendAllText(html.FullPath,"tampered");Assert.Throws<InvalidOperationException>(()=>service.EnsureHtml(id));
    }
    [Fact] public async Task MissingReadyArtifactIsNotSilentlyOverwritten()
    {
        var(_,id)=await CreateReport();var service=new ReportArtifactService(Db,Path.Combine(folder,"reports"));var html=service.EnsureHtml(id);File.Delete(html.FullPath);
        Assert.Throws<InvalidOperationException>(()=>service.EnsureHtml(id));Assert.False(File.Exists(html.FullPath));
    }
    [Fact] public async Task PdfFailureAndCancellationRecordActualFailureAndCanRetry()
    {
        var(_,id)=await CreateReport();var service=new ReportArtifactService(Db,Path.Combine(folder,"reports"));
        await Assert.ThrowsAsync<IOException>(()=>service.GeneratePdfAsync(id,(_,_)=>throw new IOException("fixture")));
        Assert.Equal("failed",Scalar("SELECT Status FROM ReportArtifact WHERE Format='pdf'"));
        using var stop=new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>service.GeneratePdfAsync(id,(_,_)=>{stop.Cancel();throw new OperationCanceledException(stop.Token);},stop.Token));
        Assert.Equal("cancelled",Scalar("SELECT Status FROM ProcessingStep WHERE StepNumber=6 ORDER BY ProcessingStepID DESC LIMIT 1"));
        Assert.NotNull(await service.GeneratePdfAsync(id,(_,_)=>Task.FromResult(System.Text.Encoding.ASCII.GetBytes("%PDF-fixture"))));
        Assert.Equal(0L,Scalar("SELECT count(*) FROM ExecutionCall c JOIN ProcessingStep s ON s.ProcessingStepID=c.forProcessingStepID WHERE s.StepNumber=6"));
    }
    [Theory] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public async Task OutputRaceCannotRegisterOldReportAsReady(int change)
    {
        var(saved,id)=await CreateReport();var service=new ReportArtifactService(Db,Path.Combine(folder,"reports"));
        await Assert.ThrowsAnyAsync<InvalidOperationException>(()=>service.GeneratePdfAsync(id,(_,_)=>{Change(saved,change);return Task.FromResult(System.Text.Encoding.ASCII.GetBytes("%PDF-fixture"));}));
        Assert.Equal(0L,Scalar("SELECT count(*) FROM ReportArtifact WHERE Format='pdf' AND Status='ready'"));Assert.Single(Directory.GetFiles(Path.Combine(folder,"reports")));
    }
    [Fact] public async Task PathEscapeFromTamperedReceiptIsRejected()
    {
        var(_,id)=await CreateReport();var service=new ReportArtifactService(Db,Path.Combine(folder,"reports"));service.EnsureHtml(id);
        Write("UPDATE ReportArtifact SET FilePath='../outside.html'");Assert.Throws<InvalidOperationException>(()=>service.EnsureHtml(id));
    }
    [Fact] public async Task FileWriteFailureNeverReady()
    {
        var(_,id)=await CreateReport();string root=Path.Combine(folder,"reports");var service=new ReportArtifactService(Db,root);
        Directory.Delete(root);File.WriteAllText(root,"block directory");
        Assert.ThrowsAny<IOException>(()=>service.EnsureHtml(id));Assert.Equal(0L,Scalar("SELECT count(*) FROM ReportArtifact WHERE Status='ready'"));Assert.Equal("failed",Scalar("SELECT Status FROM ProcessingStep WHERE StepNumber=6"));
    }
    [Fact] public async Task ReportInsertFailureRollsBackStepCompletion()
    {
        var saved=Seed();await new Step4Service(Db).ExecuteAsync(saved.NamingCaseID,saved.CaseRevisionID,"fake",new Client((r,n)=>Reply(r)));
        Write("CREATE TRIGGER reject_report BEFORE INSERT ON NamingReport BEGIN SELECT RAISE(ABORT,'test write failure'); END");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new Step5Service(Db).ExecuteAsync(saved.NamingCaseID,saved.CaseRevisionID,"fake",new Client((r,n)=>ReportReply(r))));
        Assert.Equal("failed",Scalar("SELECT Status FROM ProcessingStep WHERE StepNumber=5"));Assert.Equal(0L,Scalar("SELECT count(*) FROM NamingReport"));
    }
    [Fact] public async Task ReportRequestWhitelistOmitsBirthSourcesAndUnselectedNames()
    {
        var saved=Seed();await new Step4Service(Db).ExecuteAsync(saved.NamingCaseID,saved.CaseRevisionID,"fake",new Client((r,n)=>Reply(r)));
        Write("UPDATE ProcessingStep SET OutputJson=json_insert(OutputJson,'$.naming.remarks[#]',json('{\"quote\":\"希望\",\"disposition\":\"ignored\",\"reason\":\"private-address-ignored\"}')) WHERE StepNumber=4");
        var client=new Client((r,n)=>ReportReply(r,illegal:n==1));
        await new Step5Service(Db).ExecuteAsync(saved.NamingCaseID,saved.CaseRevisionID,"fake",client);
        foreach(var request in client.Requests) {
            using var input=JsonDocument.Parse(request.GetProperty("input")[0].GetProperty("content").GetString()!);
            var context=input.RootElement.GetProperty("context");
            Assert.Equal("字義自然，寄託安定成長。",context.GetProperty("groups")[0].GetProperty("entries")[0].GetProperty("selectionReason").GetString());
            Assert.Single(context.GetProperty("appliedPreferences").EnumerateArray());
            Assert.Equal("希望文雅",context.GetProperty("appliedPreferences")[0].GetProperty("quote").GetString());
            Assert.DoesNotContain("private-address-ignored",request.GetRawText());
            Assert.Equal(new[]{"groups","appliedPreferences","ruleStatus"},context.EnumerateObject().Select(p=>p.Name));
            foreach(var group in context.GetProperty("groups").EnumerateArray())foreach(var entry in group.GetProperty("entries").EnumerateArray())foreach(string position in new[]{"first","last"})Assert.Equal(new[]{"literalMeaning","namingWish","style"},entry.GetProperty(position).GetProperty("profile").EnumerateObject().Select(p=>p.Name));
            foreach(string key in new[]{"sourceChartRunId","sourceAnalysisRunId","sourceNamingRunId","forbiddenNames","remarksRaw","birthDate","birthCity","birthLocationDetail","kangxiStrokes"})Assert.DoesNotContain(key,request.GetRawText());
        }
    }
    [Fact] public void PureAssemblerMarksOfflineFixtureAndUsesCandidateFactsNotModelLabels()
    {
        var saved=Seed();var context=new Step4Service(Db).Prepare(saved.NamingCaseID,saved.CaseRevisionID);
        var candidates=context.GetProperty("candidates");
        var node=System.Text.Json.Nodes.JsonNode.Parse(PoolTestData.Selection(context).GetRawText())!;node["groups"]![0]!["label"]="999畫模型亂標籤";
        var selection=JsonSerializer.SerializeToElement(node);
        var naming=new ChartRunResult(999,saved.CaseRevisionID,"completed",context,Step2Json.Element(new{naming=selection,candidates,ruleVersion=NamingEngine.Version,dictionaryVersion="curated-zh-TW-v2",coverage=Array.Empty<string>(),warnings=Array.Empty<string>()}),null);
        using var fixture=JsonDocument.Parse(typeof(ReportFlowTests).Assembly.GetManifestResourceStream("Step2Fixture")!);
        var chart=fixture.RootElement.GetProperty("response").GetProperty("structuredContent").GetProperty("data").GetProperty("chart");
        var prose=PoolTestData.Prose(ReportDataAssembler.ProseContext(naming));
        var data=ReportDataAssembler.Build(saved,chart,naming,prose,new Step3Service(Db).Latest(saved.CaseRevisionID)!,"offline-fictional-demo");
        Assert.Equal("offline-fictional-demo",data.GetProperty("sourceKind").GetString());
        var group=data.GetProperty("groups")[0];var c=group.GetProperty("recommendation").GetProperty("candidate");
        Assert.Equal(c.GetProperty("first").GetProperty("kangxiStrokes").GetInt32(),group.GetProperty("configuration").GetProperty("firstStrokes").GetInt32());
        Assert.InRange(group.GetProperty("combinations").GetArrayLength(),16,25);
        string html=ReportHtmlRenderer.Render(data);
        Assert.Contains("離線虛構示範",html);Assert.DoesNotContain("本組怎麼算",html);Assert.DoesNotContain("第一字 999",html);
        Assert.Equal(0L,Scalar("SELECT count(*) FROM ExecutionCall"));
    }
    [Fact] public async Task ReparseRootAndReceiptAreRejected()
    {
        var(_,id)=await CreateReport();string target=Path.Combine(folder,"target"),link=Path.Combine(folder,"link");Directory.CreateDirectory(target);
        if(OperatingSystem.IsWindows()) {
            var info=new System.Diagnostics.ProcessStartInfo("cmd.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            info.ArgumentList.Add("/c");info.ArgumentList.Add("mklink");info.ArgumentList.Add("/J");info.ArgumentList.Add(link);info.ArgumentList.Add(target);
            using var process=System.Diagnostics.Process.Start(info)!;await process.WaitForExitAsync();Assert.Equal(0,process.ExitCode);
        } else Directory.CreateSymbolicLink(link,target);
        try {Assert.Throws<InvalidOperationException>(()=>new ReportArtifactService(Db,link));}
        finally {Directory.Delete(link);}
        var service=new ReportArtifactService(Db,target);var receipt=service.EnsureHtml(id);
        // A ready artifact replaced with a link must fail policy before reading target bytes.
        File.Delete(receipt.FullPath);
        if(OperatingSystem.IsWindows()) {
            Directory.CreateDirectory(Path.Combine(folder,"replacement"));
            var info=new System.Diagnostics.ProcessStartInfo("cmd.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(string a in new[]{"/c","mklink","/J",receipt.FullPath,Path.Combine(folder,"replacement")})info.ArgumentList.Add(a);
            using var process=System.Diagnostics.Process.Start(info)!;await process.WaitForExitAsync();Assert.Equal(0,process.ExitCode);
            try{Assert.Throws<InvalidOperationException>(()=>service.EnsureHtml(id));}finally{Directory.Delete(receipt.FullPath);}
        } else {
            File.WriteAllText(Path.Combine(folder,"replacement"),"replacement");File.CreateSymbolicLink(receipt.FullPath,Path.Combine(folder,"replacement"));
            Assert.Throws<InvalidOperationException>(()=>service.EnsureHtml(id));File.Delete(receipt.FullPath);
        }
    }
    private sealed class IgnoringCancellationClient:IAnalysisClient
    {
        public Task<AiResponse> SendAsync(JsonElement request,CancellationToken token)=>new TaskCompletionSource<AiResponse>(TaskCreationOptions.RunContinuationsAsynchronously).Task;
        public static implicit operator ModelConnection(IgnoringCancellationClient client) => new(OpenAiResponsesAdapter.Instance, client);
    }
    [Theory] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public async Task SharedRunnerStopsEvenIfTransportIgnoresCancellation(int step)
    {
        var saved=Seed();await new Step4Service(Db).ExecuteAsync(saved.NamingCaseID,saved.CaseRevisionID,"fake",new Client((r,n)=>Reply(r)));
        using var stop=new CancellationTokenSource();var client=new IgnoringCancellationClient();
        Task<ChartRunResult> pending=step switch {
            3=>new Step3Service(Db).ExecuteAsync(saved.NamingCaseID,saved.CaseRevisionID,"fake",client,stop.Token),
            4=>new Step4Service(Db).ExecuteAsync(saved.NamingCaseID,saved.CaseRevisionID,"fake",client,stop.Token),
            _=>new Step5Service(Db).ExecuteAsync(saved.NamingCaseID,saved.CaseRevisionID,"fake",client,stop.Token)
        };
        stop.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0L,Scalar("SELECT count(*) FROM ExecutionCall WHERE Status='running'"));
    }
    [Fact] public async Task PrintLayoutUsesGroupChaptersAndMarginBoxWithoutDebugCoverage()
    {
        var(_,id)=await CreateReport();var data=new ReportRepository(Db).Load(id).Data;
        var mutable=System.Text.Json.Nodes.JsonNode.Parse(data.GetRawText())!;
        mutable["coverage"]=new System.Text.Json.Nodes.JsonArray("H1 internal-coverage-sentinel");
        mutable["warnings"]=new System.Text.Json.Nodes.JsonArray("H2 internal-warning-sentinel");
        mutable["sourceNotes"]=new System.Text.Json.Nodes.JsonArray("internal-source-sentinel");
        var changed=JsonSerializer.SerializeToElement(mutable);var html=ReportHtmlRenderer.Render(changed);
        Assert.Equal("parent-report-pools-v12",ReportDataAssembler.TemplateVersion);
        Assert.Contains(".print-page{break-before:page",html);
        Assert.Equal(1,html.Split("class=\"report-content\"").Length-1);
        Assert.Equal(2,html.Split("class=\"proposal-page print-page\"").Length-1);
        Assert.Equal(2,html.Split("class=\"comparison-page print-page\"").Length-1);
        Assert.True(html.IndexOf("寶寶的出生資料",StringComparison.Ordinal)<html.IndexOf("id=\"birth-analysis\"",StringComparison.Ordinal));
        Assert.Equal(1,html.Split("class=\"print-status\"").Length-1);
        Assert.Contains("@bottom-center",html);Assert.Contains("counter(page)",html);Assert.Contains("OnlyFate 試行報告",html);
        Assert.DoesNotContain("position:fixed",html);Assert.DoesNotContain("print-trial",html);
        foreach(string marker in new[]{"internal-coverage-sentinel","internal-warning-sentinel","internal-source-sentinel","naming-trial-v1","limited-zh-TW-v1","parent-report-trial-v2"})Assert.DoesNotContain(marker,html);
        Assert.Contains("internal-coverage-sentinel",changed.GetRawText());
        Assert.Contains("選定前，和家人一起讀讀看",html);
        Assert.DoesNotContain("overflow:hidden",html);Assert.DoesNotContain("max-height",html);
        mutable["versions"]!["templateVersion"]="parent-report-trial-v1";
        Assert.Throws<InvalidOperationException>(()=>ReportHtmlRenderer.Render(JsonSerializer.SerializeToElement(mutable)));
    }
    [Theory][InlineData(4)][InlineData(5)]
    public async Task RendererIncludesEveryPositionProfileAndCountsDynamicPools(int size)
    {
        var(_,id)=await CreateReport();var data=new ReportRepository(Db).Load(id).Data;
        var node=System.Text.Json.Nodes.JsonNode.Parse(data.GetRawText())!;
        for(int gi=0;gi<2;gi++) {
            var group=node["groups"]![gi]!;
            foreach(string key in new[]{"firstCharacters","lastCharacters"}) {
                var pool=group[key]!.AsArray();while(pool.Count>size)pool.RemoveAt(pool.Count-1);
                while(pool.Count<size){var extra=pool[0]!.DeepClone();extra["character"]="測";pool.Add(extra);}
                for(int ci=0;ci<pool.Count;ci++)foreach(string field in new[]{"literalMeaning","namingWish","style","caution"})
                    pool[ci]!["profile"]![field]=$"{gi}-{key}-{ci}-{field} <&> "+new string('長',200);
            }
        }
        string html=ReportHtmlRenderer.Render(JsonSerializer.SerializeToElement(node));
        Assert.Contains($"本組共有 {size*size} 種搭配",html);
        Assert.Equal(2*2*size,html.Split("<article class=\"compare-character ").Length-1);
        foreach(var group in node["groups"]!.AsArray())foreach(string key in new[]{"firstCharacters","lastCharacters"})foreach(var ch in group![key]!.AsArray())foreach(string field in new[]{"literalMeaning","namingWish","style","caution"})
            Assert.Contains(System.Net.WebUtility.HtmlEncode(ch!["profile"]![field]!.ToString()),html);
        Assert.Contains("Noto Serif TC",html);Assert.Contains("grid-template-columns:1fr",html);
        Assert.Contains("可跨列搭配",html);Assert.DoesNotContain("sourceNote",html);foreach(var group in data.GetProperty("groups").EnumerateArray())Assert.DoesNotContain(group.GetProperty("id").GetString()!,html);
        Assert.DoesNotContain("overflow:hidden",html);Assert.DoesNotContain("max-height",html);
    }
    [Fact] public async Task ArtifactCopyPreservesFixedDataAndOriginalRowsWithoutModelCall()
    {
        var(saved,id)=await CreateReport();var repository=new ReportRepository(Db);var original=repository.Load(id);
        long calls=(long)Scalar("SELECT count(*) FROM ExecutionCall");string output=Scalar("SELECT OutputJson FROM ProcessingStep WHERE StepNumber=5").ToString()!;
        string file=Path.Combine(folder,"original.html");File.WriteAllText(file,"original artifact fixture");
        string artifactHash=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)));
        Write($"INSERT INTO ReportArtifact(forNamingReportID,Format,FilePath,ContentSha256,FileSizeBytes,Status,CompletedAtUtc) VALUES({id},'html','original.html','{artifactHash}',25,'ready','2026-11-03T00:00:00Z')");
        var copy=repository.CreateArtifactCopy(id);
        Assert.Equal("original artifact fixture",File.ReadAllText(file));Assert.Equal(1L,Scalar("SELECT count(*) FROM ReportArtifact"));Assert.Equal(id,Scalar("SELECT forNamingReportID FROM ReportArtifact"));Assert.Equal(artifactHash,Scalar("SELECT ContentSha256 FROM ReportArtifact"));
        Assert.NotEqual(id,copy.NamingReportID);Assert.Equal(original.ProcessingRunID,copy.ProcessingRunID);Assert.Equal(original.ContentSha256,copy.ContentSha256);Assert.Equal(original.Data.GetRawText(),copy.Data.GetRawText());
        Assert.Equal(2L,Scalar("SELECT MAX(VersionNumber) FROM NamingReport"));Assert.Equal(2L,Scalar("SELECT count(*) FROM NamingReport"));Assert.Equal(calls,Scalar("SELECT count(*) FROM ExecutionCall"));
        Assert.Equal(output,Scalar("SELECT OutputJson FROM ProcessingStep WHERE StepNumber=5"));Assert.Equal(original.Data.GetRawText(),repository.Load(id).Data.GetRawText());
        Assert.Equal(copy.NamingReportID,repository.LatestForRun(original.ProcessingRunID).NamingReportID);
        Assert.Equal(copy.NamingReportID,new Step5Service(Db).Latest(saved.CaseRevisionID)!.Chart!.Value.GetProperty("reportId").GetInt64());
        Assert.Contains("rebuiltFromReportId",Scalar("SELECT ValidationJson FROM NamingReport ORDER BY NamingReportID DESC LIMIT 1").ToString());
    }
    [Theory][InlineData(1)][InlineData(2)][InlineData(3)][InlineData(4)][InlineData(5)]
    public async Task ArtifactCopyRejectsChangedSources(int change) {var(saved,id)=await CreateReport();Change(saved,change);Assert.ThrowsAny<InvalidOperationException>(()=>new ReportRepository(Db).CreateArtifactCopy(id));Assert.Equal(1L,Scalar("SELECT count(*) FROM NamingReport"));}
    [Theory][InlineData(true)][InlineData(false)]
    public async Task ArtifactCopyRejectsHashOrUnsupportedTemplate(bool hash) {var(_,id)=await CreateReport();Write(hash?"UPDATE NamingReport SET ReportDataJson=json_set(ReportDataJson,'$.summary','changed')":"UPDATE NamingReport SET TemplateVersion='old-v1'");Assert.Throws<InvalidOperationException>(()=>new ReportRepository(Db).CreateArtifactCopy(id));Assert.Equal(1L,Scalar("SELECT count(*) FROM NamingReport"));}
    [Fact] public void LegacyV1ReportReturnsClearExpiredStatus()
    {
        var saved=Seed();Write("INSERT INTO ProcessingRun(forCaseRevisionID,Status) VALUES($revision,'completed'); INSERT INTO ProcessingStep(forProcessingRunID,StepNumber,InputJson,OutputJson,Status) VALUES(last_insert_rowid(),5,'{}','{}','completed');",saved.CaseRevisionID);
        var latest=new Step5Service(Db).Latest(saved.CaseRevisionID)!;Assert.Equal("cancelled",latest.Status);Assert.Contains("舊版",latest.ErrorMessage);
    }
    [Fact] public async Task NormalThreeModelStagesProduceCompleteFixedDataForRendering()
    {
        var saved=Seed();var client3=new Client((request,n)=>{
            var analysis=Step2Json.Element(new{summary="依目前資料採土金方向試行。",direction="以穩重與清朗字義取名。",limitations=new[]{"命理取用仍為研究試行。"},blockingIssues=Array.Empty<string>(),namingElements=new[]{"土","金"},evidence=new[]{new{ruleId="R1",factPath="/dayMaster/char",factValue="庚",reason="引用已記錄日干。"}},remarks=Array.Empty<object>()});
            return new AiResponse(200,Step2Json.Element(new{status="completed",output=new[]{new{type="message",content=new[]{new{type="output_text",text=analysis.GetRawText()}}}}}));
        });
        await new Step3Service(Db).ExecuteAsync(saved.NamingCaseID,saved.CaseRevisionID,"fake",client3);
        var client4=new Client((r,n)=>Reply(r));await new Step4Service(Db).ExecuteAsync(saved.NamingCaseID,saved.CaseRevisionID,"fake",client4);
        var client5=new Client((r,n)=>ReportReply(r));var result=await new Step5Service(Db).ExecuteAsync(saved.NamingCaseID,saved.CaseRevisionID,"fake",client5);
        Assert.Single(client3.Requests);Assert.Single(client4.Requests);Assert.Single(client5.Requests);Assert.Equal(3L,Scalar("SELECT count(*) FROM ExecutionCall"));
        var data=result.Chart!.Value.GetProperty("reportData");Assert.Equal("report-data-v2",data.GetProperty("schemaVersion").GetString());Assert.False(data.GetProperty("formalDeliverable").GetBoolean());Assert.Equal("model",data.GetProperty("sourceKind").GetString());
        var groups=data.GetProperty("groups").EnumerateArray().ToArray();Assert.Equal(2,groups.Length);
        Assert.All(groups,g=>{Assert.Equal(4,g.GetProperty("alternatives").GetArrayLength());Assert.Equal(16,g.GetProperty("combinations").GetArrayLength());Assert.Equal(4,g.GetProperty("firstCharacters").GetArrayLength());Assert.Equal(4,g.GetProperty("lastCharacters").GetArrayLength());Assert.All(g.GetProperty("firstCharacters").EnumerateArray(),c=>Assert.False(string.IsNullOrWhiteSpace(c.GetProperty("profile").GetProperty("literalMeaning").GetString())));});
    }
    public void Dispose(){if(Directory.Exists(folder))Directory.Delete(folder,true);}
}

