using System.IO;
using Microsoft.Data.Sqlite;
using OnlyFate.Database;
using OnlyFate.Desktop;
using Xunit;
namespace OnlyFate.Desktop.Tests;
public sealed class ReportArtifactInspectionTests:IDisposable
{
 private readonly string root=Path.Combine(Path.GetTempPath(),"OnlyFate.UIArtifacts",Guid.NewGuid().ToString("N"));
 [Theory][InlineData("html",false)][InlineData("html",true)][InlineData("pdf",false)][InlineData("pdf",true)]
 public async Task OneDamagedFormatKeepsOtherFormatAndSupportsImmutableRecovery(string format,bool corrupt)
 {
  var demo=new DemoWorkspace(root);var result=demo.CreateNew();var service=new ReportArtifactService(demo.DatabasePath,demo.ReportsPath);long id=result.Report.NamingReportID;
  var html=service.EnsureHtml(id);var pdf=await service.GeneratePdfAsync(id,(_,_)=>Task.FromResult(System.Text.Encoding.ASCII.GetBytes("%PDF-fixture")));
  string path=format=="html"?html.FullPath:pdf.FullPath;if(corrupt)File.AppendAllText(path,"damage");else File.Delete(path);
  var state=ReportArtifactInspection.Read(demo.DatabasePath,service,id);
  Assert.Equal(format!="html",state.HtmlReady);Assert.Equal(format!="pdf",state.PdfReady);
  Assert.Contains("異常",format=="html"?state.HtmlStatus:state.PdfStatus);
  var repository=new ReportRepository(demo.DatabasePath);var copy=repository.CreateArtifactCopy(id);
  Assert.Equal(result.Report.ContentSha256,copy.ContentSha256);Assert.NotEqual(id,copy.NamingReportID);
  Assert.NotNull(service.EnsureHtml(copy.NamingReportID));Assert.Equal(result.Report.Data.GetRawText(),repository.Load(id).Data.GetRawText());
  using var db=new SqliteConnection($"Data Source={demo.DatabasePath};Pooling=False");db.Open();using var command=db.CreateCommand();command.CommandText="SELECT count(*) FROM ExecutionCall";Assert.Equal(0L,command.ExecuteScalar());
 }
 [Fact] public void TamperedFixedDataCannotBeRecoveredOrInspectedAsFileOnlyFailure()
 {
  var demo=new DemoWorkspace(root);var result=demo.CreateNew();
  using(var db=new SqliteConnection($"Data Source={demo.DatabasePath};Pooling=False")){db.Open();using var c=db.CreateCommand();c.CommandText="UPDATE NamingReport SET ContentSha256='0000000000000000000000000000000000000000000000000000000000000000'";c.ExecuteNonQuery();}
  Assert.Throws<InvalidOperationException>(()=>ReportArtifactInspection.Read(demo.DatabasePath,new(demo.DatabasePath,demo.ReportsPath),result.Report.NamingReportID));
  Assert.Throws<InvalidOperationException>(()=>new ReportRepository(demo.DatabasePath).CreateArtifactCopy(result.Report.NamingReportID));
 }
 public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}

