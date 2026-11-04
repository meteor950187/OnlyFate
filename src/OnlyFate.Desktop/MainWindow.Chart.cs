using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using OnlyFate.Database;

namespace OnlyFate.Desktop;

public partial class MainWindow
{
    private CancellationTokenSource? chartCancellation;
    private Step2Service ChartService() => new(databasePath);

    private void ResetChart()
    {
        ResetAnalysis();
        BirthplaceCandidates.ItemsSource = null;
        AcceptCityPoint.IsChecked = false;
        ChartStatus.Text = "尚未排盤";
        ChartPillars.Text = "";
        ChartDetails.Text = "請先儲存案件，再查找出生城市。";
    }

    private void BirthplaceChanged(object sender, SelectionChangedEventArgs e) => AcceptCityPoint.IsChecked = false;

    private bool CanUseSavedCase()
    {
        if (activeCaseId is null || activeRevisionId is null || CaseForm.IsDirty)
        {
            CaseMessage.Text = "請先儲存目前的案件資料，再進行排盤。";
            return false;
        }
        return !caseOperationInProgress;
    }

    private async void FindBirthplace(object sender, RoutedEventArgs e)
    {
        if (!CanUseSavedCase()) return;
        long caseId = activeCaseId!.Value, revisionId = activeRevisionId!.Value;
        BirthplaceCandidates.ItemsSource = null;
        SetCaseBusy(true, "正在查找出生城市…");
        try
        {
            var places = await Task.Run(() => ChartService().FindPlaces(caseId, revisionId));
            BirthplaceCandidates.ItemsSource = places;
            CaseMessage.Text = places.Count == 0 ? "找不到出生城市；請核對國家、城市名稱。此版資料庫未涵蓋所有鄉鎮，不會自動代用鄰近城市。"
                : "請選擇正確城市，並確認是否接受城市代表點的精度。";
        }
        catch (Step2Exception exception) { CaseMessage.Text = exception.Message; }
        catch (CaseConflictException exception) { CaseMessage.Text = exception.Message; }
        catch (Exception exception)
        {
            Trace.TraceError("Birthplace lookup failed: {0}", exception);
            CaseMessage.Text = "出生地查詢未完成，請確認本機資料與案件狀態。";
        }
        finally { SetCaseBusy(false); }
    }

    private async void CalculateChart(object sender, RoutedEventArgs e)
    {
        if (!CanUseSavedCase() || !AllowRealCalls()) return;
        if (BirthplaceCandidates.SelectedItem is not BirthplaceCandidate place || AcceptCityPoint.IsChecked != true)
        {
            CaseMessage.Text = "請先選擇出生城市，並確認採用城市代表點。";
            return;
        }
        long caseId = activeCaseId!.Value, revisionId = activeRevisionId!.Value;
        using CancellationTokenSource cancellation = new();
        chartCancellation = cancellation;
        CancelChartButton.Visibility = Visibility.Visible;
        SetCaseBusy(true, "正在核對時區並呼叫本機 OpenFate；呼叫逾 45 秒會停止…");
        ResetAnalysis();
        ChartStatus.Text = "排盤中…";
        ChartPillars.Text = "";
        ChartDetails.Text = "";
        try
        {
            ChartRunResult result = await Task.Run(() => ChartService().ExecuteAsync(caseId, revisionId, place.GeoNameID, true, cancellation.Token));
            ShowChart(result);
            CaseMessage.Text = "排盤已儲存；尚未進行命理判讀或取名。";
        }
        catch (OperationCanceledException) { ShowChartFailure("已取消排盤", "已保留本次執行紀錄，可重新排盤。"); }
        catch (Step2Exception exception) { ShowChartFailure("排盤未完成", exception.Message); }
        catch (CaseConflictException exception) { ShowChartFailure("案件版本已更新", exception.Message); }
        catch (Exception exception)
        {
            Trace.TraceError("Chart calculation failed: {0}", exception);
            ShowChartFailure("排盤未完成", "請確認資料庫可寫入與本機執行依賴，再重試。");
        }
        finally
        {
            chartCancellation = null;
            CancelChartButton.Visibility = Visibility.Collapsed;
            SetCaseBusy(false);
        }
    }

    private void CancelChart(object sender, RoutedEventArgs e)
    {
        chartCancellation?.Cancel();
        CaseMessage.Text = "正在取消處理，請稍候…";
    }

    private async Task RestoreChart(long revisionId)
    {
        try
        {
            ChartRunResult? latest = await Task.Run(() => ChartService().Latest(revisionId));
            if (latest is not null) ShowChart(latest);
        }
        catch (Exception exception)
        {
            Trace.TraceError("Chart reloading failed: {0}", exception);
            ShowChartFailure("排盤紀錄無法讀取", "案件表單已開啟，但排盤紀錄載入失敗，請檢查資料庫。");
        }
    }

    private void ShowChartFailure(string status, string message)
    {
        ChartStatus.Text = status;
        ChartPillars.Text = "";
        ChartDetails.Text = message;
        CaseMessage.Text = message;
    }

    private void ShowChart(ChartRunResult result)
    {
        if (result.Status != "completed" || result.Chart is null)
        {
            ShowChartFailure(result.Status is "running" or "pending" ? "上次排盤未完成" : "上次排盤未成功",
                result.ErrorMessage ?? "未留下完整結果，請重新排盤；上次紀錄仍保留。");
            return;
        }
        JsonElement chart = result.Chart.Value, pillars = chart.GetProperty("pillars"), time = result.Input.GetProperty("time"), place = result.Input.GetProperty("place");
        ChartStatus.Text = "排盤完成（尚未判讀）";
        ChartPillars.Text = string.Join("　", new[] { ("year", "年"), ("month", "月"), ("day", "日"), ("hour", "時") }
            .Select(p => $"{p.Item2}柱 {pillars.GetProperty(p.Item1).GetProperty("ganZhi").GetString()}"));
        JsonElement solar = chart.GetProperty("calendar").GetProperty("calculationSolar");
        string calculationTime = $"{solar.GetProperty("year").GetInt32():0000}-{solar.GetProperty("month").GetInt32():00}-{solar.GetProperty("day").GetInt32():00} {solar.GetProperty("hour").GetInt32():00}:{solar.GetProperty("minute").GetInt32():00}";
        double offset = time.GetProperty("utcOffsetHours").GetDouble();
        ChartDetails.Text = $"出生紀錄：{time.GetProperty("localCivilTime").GetString()?.Replace('T', ' ')}（原始精度到分鐘）\n"
            + $"出生時區：{time.GetProperty("timeZoneID").GetString()}，當時 UTC{(offset >= 0 ? "+" : "")}{offset.ToString("0.####", CultureInfo.InvariantCulture)}\n"
            + $"出生城市：{place.GetProperty("name").GetString()}（城市代表點）\n"
            + $"排盤用真太陽時：{calculationTime}；23:00 換日\n"
            + "計算秒數以 0 代入，不表示出生紀錄精確到秒。排盤用日期與原始出生日期分開保存。";
    }
}
