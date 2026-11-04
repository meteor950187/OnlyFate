using OnlyFate.Desktop;
using Xunit;
namespace OnlyFate.Desktop.Tests;
public sealed class WorkflowStateTests
{
 [Fact] public void ResetInvalidatesReportAndOldOperation() {var state=new WorkflowState();int version=state.Generation;state.SetReport(12);state.Reset();Assert.Null(state.ReportId);Assert.False(state.IsCurrent(version,12));}
 [Fact] public void CredentialsAreExplicitlyClearedAndNeverSerialized() {var state=new WorkflowState();state.SetCredential("secret-example");Assert.Equal("secret-example",state.Credential);Assert.DoesNotContain("secret-example",System.Text.Json.JsonSerializer.Serialize(state));state.ClearCredential();Assert.Equal("",state.Credential);}
}
