using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using OnlyFate.Database;
namespace OnlyFate.Desktop;
public partial class MainWindow : Window
{
 private bool isClosed;
 private readonly bool isDemo=Environment.GetCommandLineArgs().Contains("--demo");
 private readonly string dataRoot;
 private readonly string databasePath;
 private readonly WorkflowState workflow=new();
 private readonly PreviewInitialization previewInitialization=new();
 private readonly PreviewOperationCoordinator previewOperations=new();
 private bool previewEventsSubscribed;
 private readonly Action clearPreview;
 private string? allowedPreviewUri;
 public MainWindow()
 {
  dataRoot=isDemo?DemoWorkspace.DefaultRoot:Path.GetDirectoryName(Path.GetDirectoryName(DatabaseSchema.DefaultDatabasePath))!;
  databasePath=isDemo?new DemoWorkspace(dataRoot).DatabasePath:DatabaseSchema.DefaultDatabasePath;
  InitializeComponent();
  clearPreview=()=>ReportPreview.CoreWebView2?.NavigateToString("<!doctype html><html><body></body></html>");
  if(isDemo){Title="OnlyFate｜離線示範｜虛構案例";ModeBanner.Text="離線示範｜虛構案例 · H1 土金為條件假設，尚未排除 H2 · 無模型呼叫";OpenDemoButton.Content="建立新示範案例";AnalysisKey.IsEnabled=false;AnalysisModel.IsEnabled=false;AnalysisProvider.IsEnabled=false;}
  AnalysisProvider.ItemsSource=ModelProvider.All;AnalysisProvider.SelectedIndex=0;
  Loaded+=InitializeCases;Closing+=CheckBeforeClosing;Closed+=ClosePreview;
 }
 private async Task EnsurePreview(CancellationToken token)
 {
  try {await previewInitialization.EnsureAsync(InitializePreview,TimeSpan.FromSeconds(30),token);}
  catch(TimeoutException){throw new InvalidOperationException("報告預覽初始化逾時，請稍後重試；仍在執行的初始化不會重複啟動。");}
 }
 private async Task InitializePreview()
 {
  var environment=await CoreWebView2Environment.CreateAsync(userDataFolder:Path.Combine(dataRoot,"WebView2"));
  if(isClosed)throw new OperationCanceledException();
  await ReportPreview.EnsureCoreWebView2Async(environment);
  var core=ReportPreview.CoreWebView2;
  core.Settings.IsScriptEnabled=false;core.Settings.AreDefaultContextMenusEnabled=false;core.Settings.AreDevToolsEnabled=false;
  if(previewEventsSubscribed)return;
  previewEventsSubscribed=true;
  core.NavigationStarting+=(_,e)=>{if(!ReportNavigationPolicy.Allows(e.Uri,allowedPreviewUri))e.Cancel=true;};
  core.NewWindowRequested+=(_,e)=>e.Handled=true;
  core.PermissionRequested+=(_,e)=>e.State=CoreWebView2PermissionState.Deny;
 }
 private async Task NavigateReport(string html,int generation,long report,CancellationToken token)
 {
  await EnsurePreview(token);token.ThrowIfCancellationRequested();
  if(!workflow.IsCurrent(generation,report))throw new OperationCanceledException();
  var done=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
  string uri=$"https://report.onlyfate.local/{report}/{Guid.NewGuid():N}.html";ulong? navigationId=null;
  var core=ReportPreview.CoreWebView2;
  using var content=new MemoryStream(System.Text.Encoding.UTF8.GetBytes(html));
  void Requested(object? sender,CoreWebView2WebResourceRequestedEventArgs e) {
   if(e.Request.Uri==uri)e.Response=core.Environment.CreateWebResourceResponse(content,200,"OK","Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store");
  }
  void Started(object? sender,CoreWebView2NavigationStartingEventArgs e){if(e.Uri==uri)navigationId=e.NavigationId;}
  void Completed(object? sender,CoreWebView2NavigationCompletedEventArgs e){
   if(e.NavigationId!=navigationId)return;
   if(e.IsSuccess)done.TrySetResult(true);else done.TrySetException(new InvalidOperationException("報告頁面載入失敗，請重新開啟案件。"));
  }
  core.AddWebResourceRequestedFilter(uri,CoreWebView2WebResourceContext.Document);
  core.WebResourceRequested+=Requested;core.NavigationStarting+=Started;core.NavigationCompleted+=Completed;
  try {
   allowedPreviewUri=uri;core.Navigate(uri);
   await done.Task.WaitAsync(TimeSpan.FromSeconds(30),token);
   if(!workflow.IsCurrent(generation,report))throw new OperationCanceledException();
   await Task.Run(()=>new ReportRepository(databasePath).Load(report),token);
   token.ThrowIfCancellationRequested();if(!workflow.IsCurrent(generation,report))throw new OperationCanceledException();
   ReportPreview.Visibility=Visibility.Visible;PreviewMessage.Visibility=Visibility.Collapsed;
  } catch(TimeoutException){core.Stop();throw new InvalidOperationException("報告頁面載入逾時，請重試。");} catch(OperationCanceledException){core.Stop();throw;} finally {core.WebResourceRequested-=Requested;core.NavigationStarting-=Started;core.NavigationCompleted-=Completed;core.RemoveWebResourceRequestedFilter(uri,CoreWebView2WebResourceContext.Document);}
 }

 private void ShowPreviewFailure(string title,string detail)
 {
  ReportPreview.Visibility=Visibility.Hidden;PreviewMessage.Visibility=Visibility.Visible;PreviewMessageTitle.Text=title;PreviewMessageDetail.Text=detail;
 }
 private void ClosePreview(object? sender,EventArgs e){isClosed=true;chartCancellation?.Cancel();workflow.ClearCredential();AnalysisKey.Clear();ReportPreview.Dispose();}
 private async void OpenDemo(object sender,RoutedEventArgs e)
 {
  if(isDemo) {
   if(caseOperationInProgress||previewOperations.IsBusy||!ConfirmDiscard())return;
   ResetChart();SetCaseBusy(true,"正在建立新的離線虛構示範；原紀錄保留…");
   try {
    var demo=await Task.Run(()=>new DemoWorkspace(dataRoot).CreateNew());
    SetCurrentCase(demo.Case);await RestoreChart(demo.Case.CaseRevisionID);await RestoreAnalysis(demo.Case.CaseRevisionID);await RestoreWorkflow(demo.Case.CaseRevisionID);
    CaseList.ItemsSource=await Task.Run(()=>caseRepository!.List());CaseMessage.Text="新示範案例已建立；沒有呼叫 OpenFate 或模型服務，原案紀錄保留。";
   }catch(Exception exception){CaseMessage.Text=SafeError(exception);}finally{SetCaseBusy(false);}
   return;
  }
  try {var exe=Environment.ProcessPath??throw new InvalidOperationException();Process.Start(new ProcessStartInfo(exe,"--demo"){UseShellExecute=true});}
  catch {CaseMessage.Text="無法開啟示範；請直接以 OnlyFate.Desktop.exe --demo 啟動。";}
 }
}

