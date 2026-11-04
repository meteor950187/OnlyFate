using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using OnlyFate.Database;
namespace OnlyFate.Desktop;
public partial class MainWindow
{
 private ReportArtifactService Artifacts()=>new(databasePath,Path.Combine(dataRoot,"Reports"));
 private string ModelId()=>AnalysisModel.Text.Trim().Length>0?AnalysisModel.Text.Trim():Environment.GetEnvironmentVariable("OPENAI_MODEL")??"";
 private bool AllowRealCalls(){if(!isDemo)return true;CaseMessage.Text="離線虛構示範禁止呼叫 OpenAI 或 OpenFate；請使用已保存的原案查看報告。";return false;}
 private bool TryCredential(out string key,out string model)
 {
  if(AnalysisKey.Password.Trim().Length>0){workflow.SetCredential(AnalysisKey.Password.Trim());AnalysisKey.Clear();}
  key=workflow.Credential.Length>0?workflow.Credential:Environment.GetEnvironmentVariable("OPENAI_API_KEY")??"";model=ModelId();
  if(key.Length>0&&model.Length>0)return true;
  CaseMessage.Text="請在判讀頁輸入共用模型 ID 與 API key，或設定 OPENAI_MODEL／OPENAI_API_KEY。";return false;
 }
 private void ClearCredential(object sender,RoutedEventArgs e){workflow.ClearCredential();AnalysisKey.Clear();CaseMessage.Text="已清除本次輸入的 API key；環境變數仍由系統管理。";}
 private void ResetNaming(){NamingDetails.Clear();NamingStatus.Text="請先完成可繼續試行的判讀。";ResetReport();}
 private void ResetReport()
 {
  workflow.Reset();allowedPreviewUri=null;RebuildReportButton.IsEnabled=false;ExportHtmlButton.IsEnabled=false;ExportPdfButton.IsEnabled=false;RequestDetails.Clear();HistoryDetails.Clear();
  ReportStatus.Text="請先完成目前版本的取名配置。";PreviewStatus.Text="HTML：未就緒｜PDF：未就緒";
  // Hide/invalidate immediately; never navigate underneath an unfinished native print.
  previewOperations.TryRunWhenIdle(clearPreview);
  ShowPreviewFailure("尚未產生目前版本報告","舊結果仍保留於紀錄；請完成上游步驟後重新產生報告。");
 }
 private void ShowRequest(JsonElement request){RequestDetails.Text=JsonSerializer.Serialize(request,new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping});WorkspaceTabs.SelectedIndex=5;}
 private async Task PreviewRequest(int step)
 {
  if(!CanUseSavedCase())return;SetCaseBusy(true,"正在準備實際 request，尚未呼叫 API…");
  try {long id=activeCaseId!.Value,revision=activeRevisionId!.Value;string model=ModelId();var request=await Task.Run(()=>step==4?NamingContract.Request(model,new Step4Service(databasePath).Prepare(id,revision)):ReportContract.Request(model,new Step5Service(databasePath).Prepare(id,revision)));ShowRequest(request);}
  catch(Exception e){CaseMessage.Text=SafeError(e);}finally{SetCaseBusy(false);}
 }
 private async void PreviewNaming(object sender,RoutedEventArgs e)=>await PreviewRequest(4);
 private async void PreviewReport(object sender,RoutedEventArgs e)=>await PreviewRequest(5);
 private async void NameCase(object sender,RoutedEventArgs e)=>await RunModelStep(4);
 private async void GenerateReport(object sender,RoutedEventArgs e)=>await RunModelStep(5);
 private async Task RunModelStep(int step)
 {
  if(!CanUseSavedCase()||!AllowRealCalls()||!TryCredential(out string key,out string model))return;
  if(step==4)ResetNaming();else ResetReport();
  long id=activeCaseId!.Value,revision=activeRevisionId!.Value;
  using var cancellation=new CancellationTokenSource();chartCancellation=cancellation;CancelChartButton.Visibility=Visibility.Visible;CancelChartButton.Content="取消處理";
  SetCaseBusy(true,step==4?"正在試行選名，可取消…":"正在生成試行報告，可取消…");
  try {
   using var http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=Timeout.InfiniteTimeSpan};var client=new OpenAiResponsesClient(http,key);
   var result=await Task.Run(()=>step==4?new Step4Service(databasePath).ExecuteAsync(id,revision,model,client,cancellation.Token):new Step5Service(databasePath).ExecuteAsync(id,revision,model,client,cancellation.Token));
   cancellation.Token.ThrowIfCancellationRequested();
   if(step==4)ShowNaming(result);else await ShowReport(result,cancellation.Token);
   CaseMessage.Text="試行結果已儲存；正式交付資格仍未開放。";
  }catch(OperationCanceledException){CaseMessage.Text="處理已取消，可重新執行。";}
  catch(Exception e){CaseMessage.Text=SafeError(e);}
  finally{chartCancellation=null;CancelChartButton.Visibility=Visibility.Collapsed;SetCaseBusy(false);}
 }
 private static string SafeError(Exception e)=>e is InvalidOperationException or ArgumentException?e.Message:"處理未完成，請確認本機資料與設定後重試。";
 private void ShowNaming(ChartRunResult result)
 {
  NamingDetails.Clear();if(result.Status!="completed"||result.Chart is null){NamingStatus.Text=result.ErrorMessage??"選名尚未完成。";return;}
  var output=result.Chart.Value;NamingStatus.Text="試行選名完成｜可生成試行報告；正式資格未開放。";
  var candidates=result.Input.GetProperty("candidates").EnumerateArray().ToDictionary(x=>x.GetProperty("id").GetString()!,x=>x.GetProperty("name").GetString());
  var naming=output.GetProperty("naming");
  var pools=result.Input.GetProperty("pools").EnumerateArray().ToDictionary(x=>x.GetProperty("poolId").GetString()!);
  var lines=new System.Text.StringBuilder();int number=0;
  foreach(var group in naming.GetProperty("groups").EnumerateArray()) {
   var pool=pools[group.GetProperty("poolId").GetString()!];
   lines.AppendLine($"方案 {++number}｜{group.GetProperty("label").GetString()}");
   lines.AppendLine(group.GetProperty("direction").GetString());
   foreach(var (key,title) in new[]{("firstCharacters","名字第一個字"),("lastCharacters","名字第二個字")})
    lines.AppendLine(title+"："+string.Join("、",pool.GetProperty(key).EnumerateArray().Select(x=>x.GetProperty("character").GetString())));
   lines.AppendLine($"本組 {pool.GetProperty("pairIds").GetArrayLength()} 種搭配皆已檢查；兩池各選一字，依位置組名。");
   var rec=group.GetProperty("recommendation");lines.AppendLine("主推："+candidates[rec.GetProperty("candidateId").GetString()!]+" — "+rec.GetProperty("reason").GetString());
   foreach(var alt in group.GetProperty("alternatives").EnumerateArray())lines.AppendLine("替代："+candidates[alt.GetProperty("candidateId").GetString()!]+" — "+alt.GetProperty("reason").GetString());
   lines.AppendLine();
  }
  lines.AppendLine("五個姓名是示例，並非所有可用組合；跨組搭配未經檢查，不能任意混用。");
  NamingDetails.Text=lines.ToString();
 }
 private async Task RestoreWorkflow(long revision)
 {
  try{var naming=await Task.Run(()=>new Step4Service(databasePath).Latest(revision));if(naming is not null)ShowNaming(naming);var report=await Task.Run(()=>new Step5Service(databasePath).Latest(revision));if(report is not null)await ShowReport(report,CancellationToken.None);}
  catch(Exception e){ReportStatus.Text=SafeError(e);}
 }
 private async Task ShowReport(ChartRunResult result,CancellationToken token)
 {
  if(result.Status!="completed"||result.Chart is null){ReportStatus.Text=result.ErrorMessage??"報告尚未完成。";return;}
  WorkspaceTabs.SelectedIndex=4;
  await ShowStoredReport(result.Chart.Value.GetProperty("reportId").GetInt64(),token);
 }
 private async Task ShowStoredReport(long report,CancellationToken token)
 {
  // Validate the immutable data before preserving a recovery entry point.
  try {await Task.Run(()=>new ReportRepository(databasePath).Load(report),token);}
  catch {ResetReport();throw;}
  workflow.SetReport(report);int generation=workflow.Generation;
  RebuildReportButton.IsEnabled=true;ExportHtmlButton.IsEnabled=false;ExportPdfButton.IsEnabled=false;
  ShowPreviewFailure("正在準備報告預覽","請稍候…");
  try {
   var receipt=await Task.Run(()=>Artifacts().EnsureHtml(report),token);string html=await File.ReadAllTextAsync(receipt.FullPath,token);
   await previewOperations.RunAsync(async()=>{await NavigateReport(html,generation,report,token);return true;},token);
   UpdateArtifactStatus(report);ReportStatus.Text="試行報告已就緒；命理與字五行規則尚未驗證。";
  }catch(Exception e) {
   try {UpdateArtifactStatus(report);}catch {ResetReport();throw;}
   allowedPreviewUri=null;
   ShowPreviewFailure("預覽尚未完成",SafeError(e)+" 可重建報告檔案，不呼叫 AI，舊產物保留。");
   ReportStatus.Text="固定報告資料仍有效；可重建檔案或重新開啟案件重試預覽。";
   throw;
  }
 }
 private void UpdateArtifactStatus(long report)
 {
  var state=ReportArtifactInspection.Read(databasePath,Artifacts(),report);
  PreviewStatus.Text=$"HTML：{state.HtmlStatus}｜PDF：{state.PdfStatus}";
  ExportHtmlButton.IsEnabled=state.HtmlReady;ExportPdfButton.IsEnabled=state.PdfReady || state.HtmlReady;
  if(!state.HtmlReady){allowedPreviewUri=null;ShowPreviewFailure("HTML 尚未就緒",state.HtmlStatus+"；可由固定資料重建報告檔案，不呼叫 AI。");}
  RebuildReportButton.IsEnabled=true;
 }
 private async void RebuildReport(object sender,RoutedEventArgs args)
 {
  if(!CanUseSavedCase()||workflow.ReportId is not long report)return;
  using var cancellation=new CancellationTokenSource();chartCancellation=cancellation;
  CancelChartButton.Visibility=Visibility.Visible;CancelChartButton.Content="取消處理";
  SetCaseBusy(true,"正在由固定資料建立報告副本；不呼叫 AI，舊產物保留…");
  try {
   var copy=await Task.Run(()=>new ReportRepository(databasePath).CreateArtifactCopy(report),cancellation.Token);
   await ShowStoredReport(copy.NamingReportID,cancellation.Token);
   CaseMessage.Text="報告副本與 HTML 已重建；需要時再產生 PDF，沒有呼叫 AI。";
  }catch(OperationCanceledException){CaseMessage.Text="已取消等待；已建立的副本仍保留，可重新開啟案件。";}
  catch(Exception e){try{if(workflow.ReportId is long current)UpdateArtifactStatus(current);}catch{ResetReport();}CaseMessage.Text=SafeError(e);}
  finally{chartCancellation=null;CancelChartButton.Visibility=Visibility.Collapsed;SetCaseBusy(false);}
 }
 private async void ExportHtml(object sender,RoutedEventArgs e)=>await ExportArtifact(false);
 private async void ExportPdf(object sender,RoutedEventArgs e)=>await ExportArtifact(true);
 private async Task ExportArtifact(bool pdf)
 {
  if(!CanUseSavedCase()||workflow.ReportId is not long report)return;
  int generation=workflow.Generation;using var cancellation=new CancellationTokenSource();chartCancellation=cancellation;CancelChartButton.Visibility=Visibility.Visible;CancelChartButton.Content="取消處理";SetCaseBusy(true,pdf?"正在建立 PDF，可取消…":"正在驗證 HTML…");
  try {
   var service=Artifacts();ReportArtifactReceipt receipt;
   if(pdf)receipt=await Task.Run(()=>service.GeneratePdfAsync(report,(html,token)=>Dispatcher.InvokeAsync(()=>previewOperations.RunAsync(async()=>{
    await NavigateReport(html,generation,report,token);token.ThrowIfCancellationRequested();
    await Task.Run(()=>new ReportRepository(databasePath).Load(report),token);
    if(!workflow.IsCurrent(generation,report))throw new OperationCanceledException();
    var settings=ReportPreview.CoreWebView2.Environment.CreatePrintSettings();settings.ShouldPrintBackgrounds=true;settings.ShouldPrintHeaderAndFooter=false;settings.PageWidth=8.2677165;settings.PageHeight=11.6929134;
    using var stream=await ReportPreview.CoreWebView2.PrintToPdfStreamAsync(settings);using var buffer=new MemoryStream();await stream.CopyToAsync(buffer,token);token.ThrowIfCancellationRequested();return buffer.ToArray();
   },token)).Task.Unwrap(),cancellation.Token));
   else receipt=await Task.Run(()=>service.GetReady(report,"html")??throw new InvalidOperationException("HTML 尚未就緒。"),cancellation.Token);
   cancellation.Token.ThrowIfCancellationRequested();if(!workflow.IsCurrent(generation,report))throw new OperationCanceledException();
   UpdateArtifactStatus(report);
   var dialog=new SaveFileDialog{Title=pdf?"另存報告 PDF":"另存報告 HTML",FileName=$"OnlyFate-report-{report}.{receipt.Format}",Filter=pdf?"PDF 文件|*.pdf":"HTML 文件|*.html"};
   if(dialog.ShowDialog(this)!=true){CaseMessage.Text="已取消另存；內部產物仍保留。";return;}
   // Revalidate immediately before copying the immutable artifact.
   var verified=await Task.Run(()=>service.GetReady(report,receipt.Format)??throw new InvalidOperationException("產物未就緒。"));
   if(!workflow.IsCurrent(generation,report))throw new OperationCanceledException();
   string destination=ExportDestinationPolicy.Validate(dialog.FileName);
   await Task.Run(()=>File.Copy(verified.FullPath,destination,true));CaseMessage.Text="報告已另存。";
  }catch(OperationCanceledException){CaseMessage.Text=previewOperations.IsBusy?"已取消輸出等待；原生列印仍在完成中，下一次預覽或列印會等候它結束。":"輸出已取消。";}catch(Exception e){try{UpdateArtifactStatus(report);}catch{ResetReport();}CaseMessage.Text=SafeError(e);}
  finally{chartCancellation=null;CancelChartButton.Visibility=Visibility.Collapsed;SetCaseBusy(false);}
 }
}



