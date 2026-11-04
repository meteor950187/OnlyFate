using System.Net.Http;
using System.Text.Json;
using System.Windows;
using OnlyFate.Database;

namespace OnlyFate.Desktop;

public partial class MainWindow
{
    private Step3Service AnalysisService() => new(databasePath);
    private void ResetAnalysis()
    {
        ResetNaming();
        AnalysisStatus.Text = "請先儲存案件並完成排盤。";
        AnalysisDetails.Clear();
    }

    private async void PreviewAnalysis(object sender, RoutedEventArgs e)
    {
        if (!CanUseSavedCase()) return;
        SetCaseBusy(true, "正在準備 context…");
        try
        {
            var context = await Task.Run(() => AnalysisService().Prepare(activeCaseId!.Value, activeRevisionId!.Value));
            ShowRequest(AnalysisContract.Request(ModelId(), context));
            AnalysisStatus.Text = "這是將送出的指令、資料與 schema；尚未呼叫 API。";
        }
        catch (Exception exception) { AnalysisStatus.Text = exception is InvalidOperationException ? exception.Message : "無法準備 context。"; }
        finally { SetCaseBusy(false); }
    }

    private async void AnalyzeCase(object sender, RoutedEventArgs e)
    {
        if (!CanUseSavedCase()) return;
        if(!AllowRealCalls() || !TryCredential(out string key,out string model)) return;
        ResetNaming();
        using var cancellation = new CancellationTokenSource();
        chartCancellation = cancellation;
        CancelChartButton.Content = "取消處理";
        CancelChartButton.Visibility = Visibility.Visible;
        SetCaseBusy(true, "正在呼叫 OpenAI，最多一次修正，總等待期限 120 秒…");
        AnalysisStatus.Text = "判讀中…";
        AnalysisDetails.Clear();
        long caseId = activeCaseId!.Value, revisionId = activeRevisionId!.Value;
        try
        {
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
            var client = new OpenAiResponsesClient(http, key);
            var result = await Task.Run(() => AnalysisService().ExecuteAsync(caseId, revisionId, model, client, cancellation.Token));
            ShowAnalysis(result);
            CaseMessage.Text = "試行判讀已儲存；可重新開啟查看。";
        }
        catch (OperationCanceledException) { AnalysisStatus.Text = "判讀已取消。"; }
        catch (Exception exception) { AnalysisStatus.Text = exception is InvalidOperationException or ArgumentException ? exception.Message : "判讀未完成，請檢查設定。"; }
        finally
        {
            chartCancellation = null;
            CancelChartButton.Content = "取消處理";
            CancelChartButton.Visibility = Visibility.Collapsed;
            SetCaseBusy(false);
        }
    }

    private async Task RestoreAnalysis(long revisionId)
    {
        try
        {
            var result = await Task.Run(() => AnalysisService().Latest(revisionId));
            if (result is not null) ShowAnalysis(result);
        }
        catch { AnalysisStatus.Text = "案件已開啟，但判讀紀錄無法讀取。"; }
    }

    private void ShowAnalysis(ChartRunResult result)
    {
        AnalysisDetails.Clear();
        if (result.Status != "completed" || result.Chart is null)
        { AnalysisStatus.Text = result.ErrorMessage ?? "上次判讀未完成，可重新執行。"; return; }
        var output = result.Chart.Value.GetProperty("analysis");
        AnalysisStatus.Text = result.Chart.Value.TryGetProperty("canTrialProceed",out var trial) && trial.ValueKind==JsonValueKind.True ? "試行判讀完成｜可繼續試行選名；正式交付仍禁止。" : "判讀有待處理阻礙，不能繼續試行選名。";
        AnalysisDetails.Text = output.GetProperty("summary").GetString() + "\n\n取用方向\n" + output.GetProperty("direction").GetString()
            + "\n\n限制與待處理事項\n" + string.Join("\n", output.GetProperty("limitations").EnumerateArray().Concat(output.GetProperty("blockingIssues").EnumerateArray()).Select(x => x.GetString()))
            + "\n\n判讀依據\n" + string.Join("\n", output.GetProperty("evidence").EnumerateArray().Select(x => $"{x.GetProperty("ruleId").GetString()} · {x.GetProperty("factPath").GetString()}：{x.GetProperty("reason").GetString()}"))
            + "\n\n備註處理\n" + string.Join("\n", output.GetProperty("remarks").EnumerateArray().Select(x => $"{x.GetProperty("quote").GetString()}：{x.GetProperty("disposition").GetString()}／{x.GetProperty("reason").GetString()}"));
    }
}
