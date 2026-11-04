using System.Text.Json;
using Microsoft.Data.Sqlite;
using OnlyFate.Database;
using Xunit;

namespace OnlyFate.Database.Tests;

public sealed class NamingFlowTests : IDisposable
{
    private readonly string folder=Path.Combine(Path.GetTempPath(),"OnlyFate.Naming.Tests",Guid.NewGuid().ToString("N"));
    private string Db=>Path.Combine(folder,"test.db");
    private static CaseInput Input=>new(){Surname="林",Gender="male",BirthDate="2026-06-15",BirthHour="12",BirthMinute="00",BirthCountry="台灣",BirthCity="台北市",Remarks="希望文雅",NoAvoidanceCharacters=true,NoRequiredCharacter=true};
    private StoredCase Seed(bool blocked=false,bool legacy=false,CaseInput? custom=null)
    {
        var saved=new CaseRepository(Db).Save(custom??Input);
        using var fixture=JsonDocument.Parse(typeof(NamingFlowTests).Assembly.GetManifestResourceStream("Step2Fixture")!);
        var chart=fixture.RootElement.GetProperty("response").GetProperty("structuredContent").GetProperty("data").GetProperty("chart");
        Write("INSERT INTO ProcessingRun(forCaseRevisionID,Status) VALUES($revision,'waiting'); INSERT INTO ProcessingStep(forProcessingRunID,StepNumber,InputJson,OutputJson,Status) VALUES(last_insert_rowid(),2,'{}',$output,'completed');",saved.CaseRevisionID,chart.GetRawText());
        var analysis=Step2Json.Element(new {canProceed=false,canTrialProceed=true,ruleStatus="experimental",analysis=new{blockingIssues=blocked?new[]{"寒暖方向矛盾"}:Array.Empty<string>(),namingElements=new[]{"金","土"}}});
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
    }
    private static AiResponse Reply(JsonElement request,bool valid=true)
    {
        using var input=JsonDocument.Parse(request.GetProperty("input")[0].GetProperty("content").GetString()!);
        var output=PoolTestData.Selection(input.RootElement.GetProperty("context"),valid);
        return new(200,Step2Json.Element(new{id="fake-test",status="completed",output=new[]{new{type="message",content=new[]{new{type="output_text",text=output.GetRawText()}}}}}));
    }
    [Fact] public async Task RepairsIllegalIdOnceAndReopensWithPinnedVersions()
    {
        var saved=Seed();var client=new Client((request,n)=>Reply(request,n==2));var service=new Step4Service(Db);
        var result=await service.ExecuteAsync(saved.NamingCaseID,saved.CaseRevisionID,"fake-model",client);
        Assert.Equal(2,client.Requests.Count);Assert.Contains("previousOutput",client.Requests[1].GetRawText());
        Assert.False(result.Chart!.Value.GetProperty("canProceed").GetBoolean());Assert.Equal("experimental",result.Chart.Value.GetProperty("ruleStatus").GetString());
        Assert.Equal(result.ProcessingRunID,new Step4Service(Db).Latest(saved.CaseRevisionID)!.ProcessingRunID);
        Assert.Equal(NamingContract.Version,Scalar("SELECT PromptVersion FROM ExecutionCall LIMIT 1"));
        Assert.Equal(NamingContract.Version,Scalar("SELECT RuleSetVersion FROM ProcessingRun ORDER BY ProcessingRunID DESC LIMIT 1"));
        Assert.Equal(2L,Scalar("SELECT count(*) FROM ExecutionCall WHERE ResponseJson IS NOT NULL"));
        foreach(string field in new[]{"birthDate","birthHour","birthMinute","birthCountry","birthCity","birthLocationDetail","remarks"})
            Assert.False(result.Input.GetProperty("caseData").TryGetProperty(field,out _));
    }
    [Fact] public async Task IllegalIdTwiceHasNoSuccessfulOutput()
    {
        var saved=Seed();var client=new Client((request,n)=>Reply(request,false));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new Step4Service(Db).ExecuteAsync(saved.NamingCaseID,saved.CaseRevisionID,"fake",client));
        Assert.Equal(2,client.Requests.Count);Assert.Equal(0L,Scalar("SELECT count(*) FROM ProcessingStep WHERE StepNumber=4 AND OutputJson IS NOT NULL"));
    }
    [Fact] public async Task ActualRequestOmitsBirthDataFromValidCalendarEvidenceAndAnalysisText()
    {
        var saved=Seed();
        using var fixture=JsonDocument.Parse(typeof(NamingFlowTests).Assembly.GetManifestResourceStream("Step2Fixture")!);
        var chart=fixture.RootElement.GetProperty("response").GetProperty("structuredContent").GetProperty("data").GetProperty("chart");
        var facts=Step2Json.Element(new{calendar=chart.GetProperty("calendar")});
        var civilSolar=facts.GetProperty("calendar").GetProperty("civilSolar");
        const string copiedBirthText="2026-06-15 12:00 台北市醫院";
        var analysis=Step2Json.Element(new{
            summary=copiedBirthText,direction=copiedBirthText,limitations=new[]{copiedBirthText},
            evidence=new[]{new{ruleId="R1",factPath="/calendar/civilSolar",factValue=civilSolar.GetRawText(),reason=copiedBirthText}},
            remarks=Array.Empty<object>(),namingElements=new[]{"金","土"},blockingIssues=Array.Empty<string>()
        });
        Assert.Empty(AnalysisContract.Validate(analysis,facts,Input.Remarks));
        Write("UPDATE ProcessingStep SET OutputJson=$output WHERE StepNumber=3",output:Step2Json.Element(new{
            canProceed=false,canTrialProceed=true,ruleStatus="experimental",analysis
        }).GetRawText());
        var client=new Client((request,n)=>Reply(request,n==2));
        await new Step4Service(Db).ExecuteAsync(saved.NamingCaseID,saved.CaseRevisionID,"fake",client);
        Assert.Equal(2,client.Requests.Count);
        foreach(var request in client.Requests)
        {
            using var body=JsonDocument.Parse(request.GetProperty("input")[0].GetProperty("content").GetString()!);
            var context=body.RootElement.GetProperty("context");
            Assert.Equal(new[]{"namingElements"},context.GetProperty("analysis").EnumerateObject().Select(p=>p.Name));
            Assert.Equal(new[]{"金","土"},context.GetProperty("analysis").GetProperty("namingElements").EnumerateArray().Select(v=>v.GetString()));
            string actualRequest=request.GetRawText();
            foreach(string forbidden in new[]{"civilSolar","calendar","factValue","factPath","evidence","summary","limitations","year","month","day","hour","minute","second","2026-06-15","birthDate","birthLocationDetail"})
                Assert.DoesNotContain(forbidden,actualRequest);
            Assert.DoesNotContain(copiedBirthText,body.RootElement.GetRawText());
        }
    }
    [Theory] [InlineData(true,false)] [InlineData(false,true)]
    public void BlockedAndLegacyAnalysisCannotBypassGate(bool blocked,bool legacy)
    {
        var saved=Seed(blocked,legacy);Assert.Throws<InvalidOperationException>(()=>new Step4Service(Db).Prepare(saved.NamingCaseID,saved.CaseRevisionID));
        Assert.Equal(0L,Scalar("SELECT count(*) FROM ExecutionCall"));
    }
    [Theory] [InlineData(2)] [InlineData(3)] [InlineData(1)]
    public async Task UpstreamChangeDuringCallPreservesRawCallButRejectsResult(int change)
    {
        var saved=Seed();var client=new Client((request,n)=>{
            if(change==1)new CaseRepository(Db).Save(Input with{BirthMinute="15"},saved.NamingCaseID,saved.CaseRevisionID);
            else Write($"INSERT INTO ProcessingRun(forCaseRevisionID,Status) VALUES($revision,'running'); INSERT INTO ProcessingStep(forProcessingRunID,StepNumber,InputJson,Status) VALUES(last_insert_rowid(),{change},'{{}}','running');",saved.CaseRevisionID);
            return Reply(request);
        });
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new Step4Service(Db).ExecuteAsync(saved.NamingCaseID,saved.CaseRevisionID,"fake",client));
        Assert.Equal(1L,Scalar("SELECT count(*) FROM ExecutionCall WHERE ResponseJson IS NOT NULL"));
        Assert.Equal(0L,Scalar("SELECT count(*) FROM ProcessingStep WHERE StepNumber=4 AND Status='completed'"));
    }
    [Theory] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task LatestInvalidatesCompletedDownstream(int change)
    {
        var saved=Seed();var service=new Step4Service(Db);await service.ExecuteAsync(saved.NamingCaseID,saved.CaseRevisionID,"fake",new Client((r,n)=>Reply(r)));
        if(change==1)new CaseRepository(Db).Save(Input with{BirthMinute="15"},saved.NamingCaseID,saved.CaseRevisionID);
        else Write($"INSERT INTO ProcessingRun(forCaseRevisionID,Status) VALUES($revision,'running'); INSERT INTO ProcessingStep(forProcessingRunID,StepNumber,InputJson,Status) VALUES(last_insert_rowid(),{change},'{{}}','running');",saved.CaseRevisionID);
        Assert.Equal("cancelled",service.Latest(saved.CaseRevisionID)!.Status);Assert.Null(service.Latest(saved.CaseRevisionID)!.Chart);
    }
    [Theory] [InlineData(429)] [InlineData(401)] [InlineData(200)]
    public async Task HttpFailuresAndRefusalsDoNotRetry(int status)
    {
        var saved=Seed();var client=new Client((r,n)=>new(status,Step2Json.Element(new{status="completed",output=new[]{new{type="message",content=new[]{new{type="refusal",refusal="test"}}}}})));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new Step4Service(Db).ExecuteAsync(saved.NamingCaseID,saved.CaseRevisionID,"fake",client));
        Assert.Single(client.Requests);Assert.Equal(0L,Scalar("SELECT count(*) FROM ExecutionCall WHERE Status='running'"));
    }
    [Fact] public async Task CancellationClosesRunAndCall()
    {
        var saved=Seed();using var stop=new CancellationTokenSource();var client=new Client((r,n)=>{stop.Cancel();throw new OperationCanceledException(stop.Token);});
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new Step4Service(Db).ExecuteAsync(saved.NamingCaseID,saved.CaseRevisionID,"fake",client,stop.Token));
        Assert.Equal("cancelled",Scalar("SELECT Status FROM ProcessingStep WHERE StepNumber=4"));Assert.Equal(0L,Scalar("SELECT count(*) FROM ExecutionCall WHERE Status='running'"));
    }
    [Fact] public async Task IncompatibleRequiredCharacterStopsBeforeModelCall()
    {
        var saved=Seed(custom:Input with{NoRequiredCharacter=false,RequiredCharacter="宥",RequiredPosition="first"});var client=new Client((r,n)=>Reply(r));
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>new Step4Service(Db).ExecuteAsync(saved.NamingCaseID,saved.CaseRevisionID,"fake",client));
        Assert.Contains("找不到兩組",error.Message);Assert.Empty(client.Requests);Assert.Equal(0L,Scalar("SELECT count(*) FROM ExecutionCall"));
    }
    [Fact] public void LegacyV1NamingIsClearlyExpiredInsteadOfMissingPropertyCrash()
    {
        var saved=Seed();Write("INSERT INTO ProcessingRun(forCaseRevisionID,Status) VALUES($revision,'completed'); INSERT INTO ProcessingStep(forProcessingRunID,StepNumber,InputJson,OutputJson,Status) VALUES(last_insert_rowid(),4,'{}','{}','completed');",saved.CaseRevisionID);
        var latest=new Step4Service(Db).Latest(saved.CaseRevisionID)!;Assert.Equal("cancelled",latest.Status);Assert.Contains("舊版",latest.ErrorMessage);
    }
    public void Dispose(){if(Directory.Exists(folder))Directory.Delete(folder,true);}
}
