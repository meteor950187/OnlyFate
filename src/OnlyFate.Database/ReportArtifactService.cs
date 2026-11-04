using System.Collections.Concurrent;
using System.Text;
namespace OnlyFate.Database;

public sealed record ReportArtifactReceipt(long ReportArtifactID,long NamingReportID,string Format,string RelativePath,string FullPath,string ContentSha256,long FileSizeBytes);

public sealed class ReportArtifactService
{
    private readonly string databasePath;
    private readonly string root;
    private static readonly ConcurrentDictionary<string,SemaphoreSlim> Gates=new(StringComparer.OrdinalIgnoreCase);
    public ReportArtifactService(string databasePath,string artifactsRoot)
    {
        this.databasePath=Path.GetFullPath(databasePath);root=Path.GetFullPath(artifactsRoot);
        CheckPath(root);Directory.CreateDirectory(root);CheckPath(root);
    }
    public ReportArtifactReceipt EnsureHtml(long reportId)=>EnsureAsync(reportId,"html",(_,_)=>Task.FromResult(Array.Empty<byte>()),default).GetAwaiter().GetResult();
    // WPF supplies the same HTML to WebView2, awaits PrintToPdfStreamAsync, then returns its bytes.
    public Task<ReportArtifactReceipt> GeneratePdfAsync(long reportId,Func<string,CancellationToken,Task<byte[]>> printHtml,CancellationToken token=default)
        =>EnsureAsync(reportId,"pdf",printHtml,token);
    public ReportArtifactReceipt? GetReady(long reportId,string format)
    {
        if(format is not ("html" or "pdf"))throw new ArgumentException("未知格式。");
        var report=new ReportRepository(databasePath).Load(reportId);
        return ReadReady(report,format);
    }
    private async Task<ReportArtifactReceipt> EnsureAsync(long reportId,string format,Func<string,CancellationToken,Task<byte[]>> producer,CancellationToken token)
    {
        var gate=Gates.GetOrAdd(databasePath+"|"+root+"|"+reportId+"|"+format,_=>new(1,1));await gate.WaitAsync(token).ConfigureAwait(false);
        try {
            var report=new ReportRepository(databasePath).Load(reportId);
            var existing=ReadReady(report,format);if(existing is not null)return existing;
            var source=report.Data.GetProperty("source");long caseId=source.GetProperty("caseId").GetInt64(),revision=source.GetProperty("revisionId").GetInt64();
            var context=Step2Json.Element(new{policy=new{ruleVersion=ReportDataAssembler.TemplateVersion},reportId,format,source});
            var runs=new ChartRunRepository(databasePath);var(run,step)=runs.Start(caseId,revision,context,6);
            string? created=null;
            try {
                token.ThrowIfCancellationRequested();
                using(var db=ReportSql.Open(databasePath))using(var tx=db.BeginTransaction(deferred:false)) {
                    ReportSql.AssertSources(db,tx,report);
                    ReportSql.Exec(db,tx,"INSERT INTO ReportArtifact(forNamingReportID,Format,Status) VALUES($id,$format,'pending') ON CONFLICT(forNamingReportID,Format) WHERE forNamingReportID IS NOT NULL AND Format IS NOT NULL DO UPDATE SET Status='pending',ErrorMessage=NULL WHERE ReportArtifact.Status<>'ready'",("$id",reportId),("$format",format));tx.Commit();
                }
                string html=format=="html"?ReportHtmlRenderer.Render(report.Data):File.ReadAllText(EnsureHtml(reportId).FullPath);
                byte[] bytes=format=="html"?Encoding.UTF8.GetBytes(html):await producer(html,token).WaitAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if(bytes is null||bytes.Length==0)throw new InvalidOperationException("產物為空。");
                if(format=="pdf"&&(bytes.Length<5||Encoding.ASCII.GetString(bytes,0,5)!="%PDF-"))throw new InvalidOperationException("未收到PDF格式位元組。");
                string relative=$"report-{reportId}-{Guid.NewGuid():N}.{format}";
                string full=Resolve(relative);created=full;
                using(var stream=new FileStream(full,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {await stream.WriteAsync(bytes,token).ConfigureAwait(false);stream.Flush(true);}
                CheckPath(full);string hash=ReportSql.Hash(bytes);
                token.ThrowIfCancellationRequested();
                runs.End(run,step,"completed",null,null,null,caseId,revision,
                    source.GetProperty("sourceChartRunId").GetInt64(),source.GetProperty("sourceAnalysisRunId").GetInt64(),source.GetProperty("sourceNamingRunId").GetInt64(),
                    (db,tx)=>{
                        ReportSql.AssertSources(db,tx,report);
                        using var command=ReportSql.Command(db,tx,"UPDATE ReportArtifact SET FilePath=$path,ContentSha256=$hash,FileSizeBytes=$size,Status='ready',ErrorMessage=NULL,CompletedAtUtc=strftime('%Y-%m-%dT%H:%M:%fZ','now') WHERE forNamingReportID=$id AND Format=$format AND Status<>'ready'",("$path",relative),("$hash",hash),("$size",bytes.LongLength),("$id",reportId),("$format",format));
                        if(command.ExecuteNonQuery()!=1)throw new InvalidOperationException("產物已由其他程序完成，請重新開啟。");
                        return Step2Json.Element(new{reportId,format,artifactReady=true,htmlReady=Ready(db,tx,reportId,"html"),pdfReady=Ready(db,tx,reportId,"pdf"),formalDeliverable=false});
                    });
                created=null;
                return ReadReady(report,format)!;
            } catch(Exception e) {
                if(created is not null)try{CheckPath(created);File.Delete(created);}catch(IOException){}catch(UnauthorizedAccessException){}catch(InvalidOperationException){}
                using(var db=ReportSql.Open(databasePath))ReportSql.Exec(db,null,"UPDATE ReportArtifact SET Status='failed',ErrorMessage=$message WHERE forNamingReportID=$id AND Format=$format AND Status<>'ready'",("$message",e is OperationCanceledException?"產生已取消。":"產物未完成；請重新產生。"),("$id",reportId),("$format",format));
                runs.End(run,step,e is OperationCanceledException or CaseConflictException?"cancelled":"failed",null,"ARTIFACT_FAILED",e is OperationCanceledException?"已取消產物產生。":"產物未完成。");throw;
            }
        } finally {gate.Release();}
    }
    private static bool Ready(Microsoft.Data.Sqlite.SqliteConnection db,Microsoft.Data.Sqlite.SqliteTransaction tx,long id,string format)=>ReportSql.Scalar(db,tx,"SELECT Status FROM ReportArtifact WHERE forNamingReportID=$id AND Format=$format",("$id",id),("$format",format)) as string=="ready";
    private ReportArtifactReceipt? ReadReady(StoredReport report,string format)
    {
        long reportId=report.NamingReportID;
        using var db=ReportSql.Open(databasePath);using var tx=db.BeginTransaction(deferred:false);
        ReportSql.AssertSources(db,tx,report);
        long artifactId,size;string relative,hash;
        using(var c=ReportSql.Command(db,tx,"SELECT ReportArtifactID,FilePath,ContentSha256,FileSizeBytes FROM ReportArtifact WHERE forNamingReportID=$id AND Format=$format AND Status='ready'",("$id",reportId),("$format",format)))
        using(var reader=c.ExecuteReader()) {
            if(!reader.Read()){tx.Commit();return null;}
            artifactId=reader.GetInt64(0);relative=reader.GetString(1);hash=reader.GetString(2);size=reader.GetInt64(3);
        }
        string full=Resolve(relative);
        if(!File.Exists(full))throw new InvalidOperationException("已完成產物遺失；不可覆寫不可變產物，請建立新報告。");
        byte[] bytes=File.ReadAllBytes(full);
        if(bytes.LongLength!=size||ReportSql.Hash(bytes)!=hash)throw new InvalidOperationException("已完成產物遭變更；不可覆寫，請建立新報告。");
        tx.Commit();return new(artifactId,reportId,format,relative,full,hash,size);
    }
    private string Resolve(string relative)
    {
        // Only our flat ID/GUID file names are accepted, even if a database row is tampered with.
        if(string.IsNullOrEmpty(relative)||Path.IsPathRooted(relative)||relative!=Path.GetFileName(relative)||relative.Contains(':')||relative.Contains('/')||relative.Contains('\\')||!System.Text.RegularExpressions.Regex.IsMatch(relative,@"^report-[0-9]+-[0-9a-f]{32}\.(html|pdf)$"))throw new InvalidOperationException("不安全產物路徑。");
        var full=Path.GetFullPath(Path.Combine(root,relative));
        if(!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("產物路徑超出根目錄。");
        CheckPath(full);return full;
    }
    private static void CheckPath(string path)
    {
        for(string? p=Path.GetFullPath(path);p is not null;p=Path.GetDirectoryName(p)) {
            try {if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("產物路徑禁止reparse point／連結。");}
            catch(FileNotFoundException){} catch(DirectoryNotFoundException){}
        }
    }
}
