using System.Text.Json.Serialization;
namespace OnlyFate.Desktop;
// UI-owned session memory. No persistence or logger receives the credential.
public sealed class WorkflowState
{
 public int Generation {get;private set;}
 public long? ReportId {get;private set;}
 [JsonIgnore] public string Credential {get;private set;}="";
 public void SetCredential(string value)=>Credential=value;
 public void ClearCredential()=>Credential="";
 public void SetReport(long id)=>ReportId=id;
 public void Reset(){Generation++;ReportId=null;}
 public bool IsCurrent(int generation,long report)=>Generation==generation&&ReportId==report;
}
