using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using OnlyFate.Desktop;
using Xunit;
namespace OnlyFate.Desktop.Tests;
[Collection("WPF resources")]
public sealed class WorkflowWindowTests
{
 [Theory]
 [InlineData("ResetChart")]
 [InlineData("ResetAnalysis")]
 [InlineData("ResetNaming")]
 [InlineData("ResetReport")]
 public void EveryUpstreamResetClearsReportExportAndRequest(string reset)
 {
  Exception? error=null;var thread=new Thread(()=>{try {
   var window=new MainWindow();
   var state=(WorkflowState)typeof(MainWindow).GetField("workflow",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!;
   state.SetReport(123);int generation=state.Generation;
   ((Button)window.FindName("ExportHtmlButton")).IsEnabled=true;
   ((Button)window.FindName("ExportPdfButton")).IsEnabled=true;
   ((Button)window.FindName("RebuildReportButton")).IsEnabled=true;
   ((TextBox)window.FindName("RequestDetails")).Text="old request";
   typeof(MainWindow).GetMethod(reset,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,null);
   Assert.Null(state.ReportId);Assert.False(state.IsCurrent(generation,123));
   Assert.False(((Button)window.FindName("ExportHtmlButton")).IsEnabled);Assert.False(((Button)window.FindName("ExportPdfButton")).IsEnabled);
   Assert.False(((Button)window.FindName("RebuildReportButton")).IsEnabled);
   Assert.Empty(((TextBox)window.FindName("RequestDetails")).Text);
   Assert.Contains("未就緒",((TextBlock)window.FindName("PreviewStatus")).Text);
   window.Close();
  }catch(Exception e){error=e;}});thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(error is not null)ExceptionDispatchInfo.Capture(error).Throw();
 }
}
