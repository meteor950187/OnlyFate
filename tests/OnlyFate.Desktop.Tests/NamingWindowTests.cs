using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using OnlyFate.Database;
using OnlyFate.Desktop;
using Xunit;
namespace OnlyFate.Desktop.Tests;
[Collection("WPF resources")]
public sealed class NamingWindowTests
{
 [Fact] public void ActualDemoShowsTwoGroupsAndBothPoolsWithoutOpeningWindow()
 {
  string root=Path.Combine(Path.GetTempPath(),"OnlyFate.NamingUI",Guid.NewGuid().ToString("N"));
  try {
   var demo=new DemoWorkspace(root);var result=demo.CreateNew();var naming=new Step4Service(demo.DatabasePath).Latest(result.Case.CaseRevisionID)!;
   OnSta(()=>{
    var window=new MainWindow();
    typeof(MainWindow).GetMethod("ShowNaming",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[naming]);
    var text=((TextBox)window.FindName("NamingDetails")).Text;
    Assert.Contains("方案 1",text);Assert.Contains("方案 2",text);Assert.Contains("16 種",text);
    Assert.Equal(2,text.Split("名字第一個字").Length-1);Assert.Equal(2,text.Split("名字第二個字").Length-1);
    Assert.Equal(2,text.Split("主推：").Length-1);Assert.Equal(8,text.Split("替代：").Length-1);
    window.Close();
   });
  }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
 }
 [Fact] public async Task DamagedHtmlHidesPreviewButKeepsRecoveryWhileValidPdfStaysIndependent()
 {
  string root=Path.Combine(Path.GetTempPath(),"OnlyFate.RecoveryUI",Guid.NewGuid().ToString("N"));
  try {
   var demo=new DemoWorkspace(root);var result=demo.CreateNew();var service=new ReportArtifactService(demo.DatabasePath,demo.ReportsPath);
   var html=service.EnsureHtml(result.Report.NamingReportID);
   var pdf=await service.GeneratePdfAsync(result.Report.NamingReportID,(_,_)=>Task.FromResult(System.Text.Encoding.ASCII.GetBytes("%PDF-fixture")));
   File.Delete(html.FullPath);
   Assert.Equal(pdf,await service.GeneratePdfAsync(result.Report.NamingReportID,(_,_)=>throw new Exception("Existing PDF must not print again")));
   OnSta(()=>{
    var window=new MainWindow();var flags=BindingFlags.Instance|BindingFlags.NonPublic;
    typeof(MainWindow).GetField("databasePath",flags)!.SetValue(window,demo.DatabasePath);
    typeof(MainWindow).GetField("dataRoot",flags)!.SetValue(window,root);
    var state=(WorkflowState)typeof(MainWindow).GetField("workflow",flags)!.GetValue(window)!;state.SetReport(result.Report.NamingReportID);
    typeof(MainWindow).GetMethod("UpdateArtifactStatus",flags)!.Invoke(window,[result.Report.NamingReportID]);
    Assert.Equal(result.Report.NamingReportID,state.ReportId);Assert.True(((Button)window.FindName("RebuildReportButton")).IsEnabled);
    Assert.False(((Button)window.FindName("ExportHtmlButton")).IsEnabled);
    Assert.True(((Button)window.FindName("ExportPdfButton")).IsEnabled);
    Assert.Equal(Visibility.Visible,((FrameworkElement)window.FindName("PreviewMessage")).Visibility);
    Assert.Equal(Visibility.Hidden,((FrameworkElement)window.FindName("ReportPreview")).Visibility);window.Close();
   });
  }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
 }
 [Theory][InlineData("ResetReport")][InlineData("ResetChart")][InlineData("ResetNaming")]
 public void CaseResetDuringCancelledNativePrintHidesAndInvalidatesWithoutNavigating(string reset)
 {
  OnSta(()=>{
   var window=new MainWindow();var flags=BindingFlags.Instance|BindingFlags.NonPublic;
   var coordinator=(PreviewOperationCoordinator)typeof(MainWindow).GetField("previewOperations",flags)!.GetValue(window)!;
   var state=(WorkflowState)typeof(MainWindow).GetField("workflow",flags)!.GetValue(window)!;state.SetReport(123);
   int navigations=0;typeof(MainWindow).GetField("clearPreview",flags)!.SetValue(window,(Action)(()=>navigations++));
   var native=new TaskCompletionSource<int>();using var stop=new CancellationTokenSource();
   var running=coordinator.RunAsync(()=>native.Task,stop.Token);stop.Cancel();
   typeof(MainWindow).GetMethod(reset,flags)!.Invoke(window,null);
   Assert.Null(state.ReportId);Assert.Equal(0,navigations);Assert.True(coordinator.IsBusy);
   Assert.Equal(Visibility.Hidden,((FrameworkElement)window.FindName("ReportPreview")).Visibility);
   Assert.False(((Button)window.FindName("ExportHtmlButton")).IsEnabled);
   native.SetResult(1);Assert.ThrowsAny<OperationCanceledException>(()=>running.GetAwaiter().GetResult());
   typeof(MainWindow).GetMethod("ResetReport",flags)!.Invoke(window,null);
   Assert.Equal(1,navigations);Assert.False(coordinator.IsBusy);window.Close();
  });
 }
 private static void OnSta(Action action){Exception? error=null;var thread=new Thread(()=>{try{action();}catch(Exception e){error=e;}});thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(error is not null)ExceptionDispatchInfo.Capture(error).Throw();}
}
