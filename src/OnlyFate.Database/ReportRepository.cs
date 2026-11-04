using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
namespace OnlyFate.Database;

public sealed record StoredReport(long NamingReportID,long ProcessingRunID,JsonElement Data,string ContentSha256);

internal static class ReportSql
{
    internal static SqliteConnection Open(string path) {var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadWrite,Pooling=false}.ToString());try{db.Open();return db;}catch{db.Dispose();throw;}}
    internal static SqliteCommand Command(SqliteConnection db,SqliteTransaction? tx,string sql,params (string,object?)[] values) {var c=db.CreateCommand();c.Transaction=tx;c.CommandText=sql;foreach(var(k,v) in values)c.Parameters.AddWithValue(k,v??DBNull.Value);return c;}
    internal static object? Scalar(SqliteConnection db,SqliteTransaction? tx,string sql,params (string,object?)[] values) {using var c=Command(db,tx,sql,values);return c.ExecuteScalar();}
    internal static void Exec(SqliteConnection db,SqliteTransaction? tx,string sql,params (string,object?)[] values){using var c=Command(db,tx,sql,values);c.ExecuteNonQuery();}
    internal static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
    internal static string Hash(string value)=>Hash(Encoding.UTF8.GetBytes(value));
    internal static void AssertStep(SqliteConnection db,SqliteTransaction tx,long revision,int step,long expected) {
        var id=Scalar(db,tx,"SELECT CASE WHEN s.Status='completed' THEN r.ProcessingRunID END FROM ProcessingRun r JOIN ProcessingStep s ON s.forProcessingRunID=r.ProcessingRunID WHERE r.forCaseRevisionID=$rev AND s.StepNumber=$step ORDER BY s.ProcessingStepID DESC LIMIT 1",("$rev",revision),("$step",step));
        if(id is not long found||found!=expected)throw new CaseConflictException();
    }
    internal static void AssertSources(SqliteConnection db,SqliteTransaction tx,StoredReport report,bool includeReportStep=true) {
        var source=report.Data.GetProperty("source");long revision=source.GetProperty("revisionId").GetInt64();
        var current=Scalar(db,tx,"SELECT cr.CaseRevisionID FROM CaseRevision cr JOIN NamingCase nc ON nc.NamingCaseID=cr.forNamingCaseID WHERE nc.NamingCaseID=$case AND nc.State=0 ORDER BY cr.RevisionNumber DESC,cr.CaseRevisionID DESC LIMIT 1",("$case",source.GetProperty("caseId").GetInt64()));
        if(current is not long id||id!=revision)throw new CaseConflictException();
        AssertStep(db,tx,revision,2,source.GetProperty("sourceChartRunId").GetInt64());
        AssertStep(db,tx,revision,3,source.GetProperty("sourceAnalysisRunId").GetInt64());
        AssertStep(db,tx,revision,4,source.GetProperty("sourceNamingRunId").GetInt64());
        if(includeReportStep)AssertStep(db,tx,revision,5,report.ProcessingRunID);
    }
}

public sealed class ReportRepository(string databasePath)
{
    internal StoredReport Complete(long run,long step,long caseId,long revision,JsonElement context,JsonElement data)
    {
        foreach(string field in new[]{"sourceChartRunId","sourceAnalysisRunId","sourceNamingRunId"})
            if(data.GetProperty("source").GetProperty(field).GetInt64()!=context.GetProperty(field).GetInt64())throw new CaseConflictException();
        long id=0;string json=data.GetRawText(),hash=ReportSql.Hash(json);
        new ChartRunRepository(databasePath).End(run,step,"completed",null,null,null,caseId,revision,
            context.GetProperty("sourceChartRunId").GetInt64(),context.GetProperty("sourceAnalysisRunId").GetInt64(),context.GetProperty("sourceNamingRunId").GetInt64(),
            (db,tx)=>{
                if(data.GetProperty("sourceKind").GetString()=="offline-fictional-demo") AssertDemo(db,tx,revision,context);
                id=(long)ReportSql.Scalar(db,tx,"INSERT INTO NamingReport(forProcessingRunID,ReportDataJson,TemplateVersion,ContentSha256,ValidationJson,Status,ValidatedAtUtc) VALUES($run,$data,$template,$hash,$validation,'ready',strftime('%Y-%m-%dT%H:%M:%fZ','now')); SELECT last_insert_rowid();",("$run",run),("$data",json),("$template",ReportDataAssembler.TemplateVersion),("$hash",hash),("$validation",Step2Json.Element(new{renderable=true,formalDeliverable=false,ruleStatus="experimental",contentCheck="結構、ID、長度及保守詞彙檢查；未證明任意語義或命理品質。"}).GetRawText()))!;
                return Step2Json.Element(new{reportId=id,formalDeliverable=false,ruleStatus="experimental",reportData=data});
            });
        return new(id,run,data,hash);
    }
    private static void AssertDemo(SqliteConnection db,SqliteTransaction tx,long revision,JsonElement context)
    {
        foreach(var pair in new[]{(2,"sourceChartRunId"),(3,"sourceAnalysisRunId"),(4,"sourceNamingRunId")}) {
            long run=context.GetProperty(pair.Item2).GetInt64();
            ReportSql.AssertStep(db,tx,revision,pair.Item1,run);
            string? policy=ReportSql.Scalar(db,tx,"SELECT PolicyJson FROM ProcessingRun WHERE ProcessingRunID=$run",("$run",run)) as string;
            using var policyDoc=JsonDocument.Parse(policy??"{}");
            if(!policyDoc.RootElement.TryGetProperty("sourceKind",out var kind) || kind.GetString()!="offline-fictional-demo")
                throw new InvalidOperationException("離線示範僅接受整條標記的虛構 fixture 來源。");
        }
    }
    public StoredReport CreateOfflineDemo(long caseId,long revisionId,JsonElement prose)
    {
        var context=new Step5Service(databasePath).Prepare(caseId,revisionId);
        using(var db=ReportSql.Open(databasePath)) using(var tx=db.BeginTransaction(deferred:false)) {AssertDemo(db,tx,revisionId,context);tx.Commit();}
        var naming=new Step4Service(databasePath).Latest(revisionId)!;
        var chart=new ChartRunRepository(databasePath).Latest(revisionId)!;
        var analysis=new Step3Service(databasePath).Latest(revisionId)!;
        var data=ReportDataAssembler.Build(new CaseRepository(databasePath).Load(caseId),chart.Chart!.Value,naming,prose,analysis,"offline-fictional-demo");
        var node=System.Text.Json.Nodes.JsonNode.Parse(context.GetRawText())!;
        node["policy"]!["sourceKind"]="offline-fictional-demo";
        context=JsonSerializer.SerializeToElement(node);
        var runs=new ChartRunRepository(databasePath);var started=runs.Start(caseId,revisionId,context,5);
        try{return Complete(started.Run,started.Step,caseId,revisionId,context,data);}
        catch {runs.End(started.Run,started.Step,"failed",null,"DEMO_FAILED","示範來源已變動，請重新開啟原案。");throw;}
    }
    public StoredReport Load(long reportId)
    {
        using var db=ReportSql.Open(databasePath);
        using var tx=db.BeginTransaction(deferred:false);
        var report=ReadReport(db,tx,reportId);
        ReportSql.AssertSources(db,tx,report);tx.Commit();return report;
    }
    private static StoredReport ReadReport(SqliteConnection db,SqliteTransaction tx,long reportId)
    {
        using var c=ReportSql.Command(db,tx,"SELECT forProcessingRunID,ReportDataJson,ContentSha256,TemplateVersion FROM NamingReport WHERE NamingReportID=$id AND Status='ready'",("$id",reportId));
        using var reader=c.ExecuteReader();
        if(!reader.Read())throw new InvalidOperationException("報告尚未就緒。");
        string json=reader.GetString(1),hash=reader.GetString(2);
        if(ReportSql.Hash(json)!=hash)throw new InvalidOperationException("報告資料雜湊不符。");
        using var doc=JsonDocument.Parse(json);var data=doc.RootElement;
        if(reader.GetString(3)!=ReportDataAssembler.TemplateVersion||!data.TryGetProperty("schemaVersion",out var schema)||schema.GetString()!="report-data-v2"
            ||!data.TryGetProperty("versions",out var versions)||!versions.TryGetProperty("templateVersion",out var template)||template.GetString()!=ReportDataAssembler.TemplateVersion)
            throw new InvalidOperationException("舊版報告已過期，請重新選名及撰寫報告；示範請建立新案例。");
        return new(reportId,reader.GetInt64(0),data.Clone(),hash);
    }
    public StoredReport LatestForRun(long runId)
    {
        using var db=ReportSql.Open(databasePath);using var tx=db.BeginTransaction(deferred:false);
        var id=ReportSql.Scalar(db,tx,"SELECT NamingReportID FROM NamingReport WHERE forProcessingRunID=$run ORDER BY VersionNumber DESC,NamingReportID DESC LIMIT 1",("$run",runId));
        if(id is not long reportId)throw new InvalidOperationException("報告尚未就緒。");
        var report=ReadReport(db,tx,reportId);ReportSql.AssertSources(db,tx,report);tx.Commit();return report;
    }
    public StoredReport CreateArtifactCopy(long reportId)
    {
        using var db=ReportSql.Open(databasePath);using var tx=db.BeginTransaction(deferred:false);
        var original=ReadReport(db,tx,reportId);ReportSql.AssertSources(db,tx,original);
        string validation=Step2Json.Element(new{rebuiltFromReportId=reportId,renderable=true,formalDeliverable=false,ruleStatus="experimental"}).GetRawText();
        long id=(long)ReportSql.Scalar(db,tx,"INSERT INTO NamingReport(forProcessingRunID,VersionNumber,ReportDataJson,TemplateVersion,ContentSha256,ValidationJson,Status,ValidatedAtUtc) SELECT forProcessingRunID,(SELECT COALESCE(MAX(VersionNumber),0)+1 FROM NamingReport WHERE forProcessingRunID=$run),ReportDataJson,TemplateVersion,ContentSha256,$validation,'ready',strftime('%Y-%m-%dT%H:%M:%fZ','now') FROM NamingReport WHERE NamingReportID=$id; SELECT last_insert_rowid();",("$run",original.ProcessingRunID),("$validation",validation),("$id",reportId))!;
        var copy=ReadReport(db,tx,id);ReportSql.AssertSources(db,tx,copy);tx.Commit();return copy;
    }
}
