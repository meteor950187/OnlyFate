using Microsoft.Data.Sqlite;
namespace OnlyFate.Database;
public static class ExecutionHistory
{
 // Deliberately excludes requests, responses and arbitrary provider error text.
 public static string Read(string databasePath,long caseId)
 {
  using var db=ReportSql.Open(databasePath);
  using var c=ReportSql.Command(db,null,"""
   SELECT s.StepNumber,c.CallKind,c.ModelName,c.ToolName,coalesce(c.Status,s.Status),c.InputTokens,c.OutputTokens,c.DurationMilliseconds,c.ErrorCode
   FROM ProcessingRun r JOIN CaseRevision v ON v.CaseRevisionID=r.forCaseRevisionID
   JOIN ProcessingStep s ON s.forProcessingRunID=r.ProcessingRunID
   LEFT JOIN ExecutionCall c ON c.forProcessingStepID=s.ProcessingStepID
   WHERE v.forNamingCaseID=$case ORDER BY s.ProcessingStepID DESC,c.ExecutionCallID DESC LIMIT 30
   """,("$case",caseId));
  using var reader=c.ExecuteReader();List<string> lines=[];
  string Value(int i)=>reader.IsDBNull(i)?"未知":reader.GetValue(i).ToString()!;
  while(reader.Read())lines.Add($"Step {Value(0)}｜{(reader.IsDBNull(1)?"無模型呼叫／未知":Value(1)+" · "+(reader.IsDBNull(2)?Value(3):Value(2)))}｜{Value(4)}｜tokens {Value(5)} / {Value(6)}｜耗時 {Value(7)} ms｜錯誤代碼 {(reader.IsDBNull(8)?"無":Value(8))}");
  return lines.Count==0?"無模型呼叫／未知":string.Join("\n",lines);
 }
}
