using System.Text.Json;
namespace OnlyFate.Database;

/// <summary>Copies the actual upstream interpretation, never generates a retrospective justification.</summary>
public static class ReportNamingBasis
{
 public static JsonElement Build(StoredCase saved,JsonElement chart,ChartRunResult naming,ChartRunResult analysis)
 {
  if(analysis.Status!="completed"||analysis.Chart is null||analysis.CaseRevisionID!=saved.CaseRevisionID
   ||analysis.ProcessingRunID!=naming.Input.GetProperty("sourceAnalysisRunId").GetInt64()
   ||analysis.Input.GetProperty("sourceChartRunId").GetInt64()!=naming.Input.GetProperty("sourceChartRunId").GetInt64())
   throw new InvalidOperationException("命名依據與選名使用的判讀來源不一致。");
  var a=analysis.Chart.Value.GetProperty("analysis");
  var facts=Step2Json.Element(new{pillars=chart.GetProperty("pillars"),dayMaster=chart.GetProperty("dayMaster"),calendar=chart.GetProperty("calendar"),metadata=chart.GetProperty("metadata")});
  if(AnalysisContract.Validate(a,facts,saved.Input.Remarks).Count!=0||!AnalysisContract.CanTrialProceed(a))
   throw new InvalidOperationException("判讀缺少可核對的命名依據，請重新判讀。");
  var elements=a.GetProperty("namingElements").EnumerateArray().Select(x=>x.GetString()!).ToHashSet();
  if(!elements.SetEquals(naming.Input.GetProperty("analysis").GetProperty("namingElements").EnumerateArray().Select(x=>x.GetString()!)))
   throw new InvalidOperationException("判讀與選名的五行方向不一致。");
  return Step2Json.Element(new{sourceAnalysisRunId=analysis.ProcessingRunID,summary=a.GetProperty("summary"),direction=a.GetProperty("direction"),namingElements=a.GetProperty("namingElements"),evidence=a.GetProperty("evidence"),limitations=a.GetProperty("limitations")});
 }
}
