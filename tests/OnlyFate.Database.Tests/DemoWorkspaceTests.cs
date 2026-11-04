using Microsoft.Data.Sqlite;
using OnlyFate.Database;
using Xunit;
namespace OnlyFate.Database.Tests;
public sealed class DemoWorkspaceTests
{
 [Fact] public void IsolatedDemoReopensWithoutCallsAndRejectsChangedCase()
 {
  string root=Path.Combine(Path.GetTempPath(),"OnlyFate.Demo.Tests",Guid.NewGuid().ToString("N"));
  try {
   var demo=new DemoWorkspace(root);var first=demo.Open();var second=demo.Open();
   Assert.Equal(first.Report.NamingReportID,second.Report.NamingReportID);
   Assert.NotEqual(DatabaseSchema.DefaultDatabasePath,demo.DatabasePath);
   Assert.Equal("宏、志",first.Case.Input.AvoidCharacters);Assert.Equal("same_sound_tone",first.Case.Input.AvoidMatchMode);Assert.False(first.Case.Input.NoAvoidanceCharacters);
   using(var db=new SqliteConnection($"Data Source={demo.DatabasePath};Pooling=False")) {db.Open();using var c=db.CreateCommand();c.CommandText="SELECT count(*) FROM ExecutionCall";Assert.Equal(0L,c.ExecuteScalar());}
   Assert.Equal("offline-fictional-demo",first.Report.Data.GetProperty("sourceKind").GetString());
   Assert.Equal(2,first.Report.Data.GetProperty("groups").GetArrayLength());
   Assert.All(first.Report.Data.GetProperty("groups").EnumerateArray(),g=>{Assert.Equal(16,g.GetProperty("combinations").GetArrayLength());Assert.Equal(4,g.GetProperty("alternatives").GetArrayLength());});
   new CaseRepository(demo.DatabasePath).Save(first.Case.Input with{BirthMinute="15"},first.Case.NamingCaseID,first.Case.CaseRevisionID);
   Assert.ThrowsAny<InvalidOperationException>(()=>new ReportRepository(demo.DatabasePath).Load(first.Report.NamingReportID));
   Assert.ThrowsAny<InvalidOperationException>(()=>demo.Open());
   var replacement=demo.CreateNew();Assert.NotEqual(first.Case.NamingCaseID,replacement.Case.NamingCaseID);Assert.Equal(replacement.Report.NamingReportID,demo.Open().Report.NamingReportID);
   Assert.Equal(2,new CaseRepository(demo.DatabasePath).List().Count);
  } finally {if(Directory.Exists(root)) Directory.Delete(root,true);}
 }
 [Fact] public void DemoRepositoryRejectsUnmarkedUpstreamAndHistoryDoesNotInventUsage()
 {
  string root=Path.Combine(Path.GetTempPath(),"OnlyFate.Demo.Tests",Guid.NewGuid().ToString("N"));
  try {
   var demo=new DemoWorkspace(root);var saved=demo.Open();
   string history=ExecutionHistory.Read(demo.DatabasePath,saved.Case.NamingCaseID);
   Assert.Contains("無模型呼叫／未知",history);Assert.DoesNotContain("tokens 0",history);
   using(var db=new SqliteConnection($"Data Source={demo.DatabasePath};Pooling=False")) {db.Open();using var c=db.CreateCommand();c.CommandText="UPDATE ProcessingRun SET PolicyJson='{}' WHERE ProcessingRunID=(SELECT forProcessingRunID FROM ProcessingStep WHERE StepNumber=3 LIMIT 1)";c.ExecuteNonQuery();}
   using var prose=System.Text.Json.JsonDocument.Parse("{}");
   Assert.Throws<InvalidOperationException>(()=>new ReportRepository(demo.DatabasePath).CreateOfflineDemo(saved.Case.NamingCaseID,saved.Case.CaseRevisionID,prose.RootElement));
  } finally {if(Directory.Exists(root))Directory.Delete(root,true);}
 }
 [Fact] public void RejectsNormalDatabaseLocation() => Assert.Throws<ArgumentException>(()=>new DemoWorkspace(Path.GetDirectoryName(Path.GetDirectoryName(DatabaseSchema.DefaultDatabasePath))!));
}
