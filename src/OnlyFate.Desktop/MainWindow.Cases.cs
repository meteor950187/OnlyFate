using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using OnlyFate.Database;

namespace OnlyFate.Desktop;

public partial class MainWindow
{
    private CaseRepository? caseRepository;
    private long? activeCaseId;
    private long? activeRevisionId;
    private bool caseOperationInProgress;

    private async void InitializeCases(object sender, RoutedEventArgs e)
    {
        Loaded -= InitializeCases;
        SetCaseBusy(true, "正在開啟本機案件資料…");
        try
        {
            caseRepository = await Task.Run(() => new CaseRepository(databasePath));
            if(isDemo) {
                try {
                var demo=await Task.Run(()=>new DemoWorkspace(dataRoot).Open());
                SetCurrentCase(demo.Case);
                await RestoreChart(demo.Case.CaseRevisionID);await RestoreAnalysis(demo.Case.CaseRevisionID);
                await RestoreWorkflow(demo.Case.CaseRevisionID);
                WorkspaceTabs.SelectedIndex=4;
                CaseMessage.Text="離線虛構示範已開啟；無模型呼叫，可預覽與另存 HTML／PDF。";
                } catch(InvalidOperationException exception) {CaseMessage.Text=exception.Message+" 此模式禁止重新呼叫 AI；可按下方「建立新示範案例」。";}
            }
            CaseList.ItemsSource = await Task.Run(() => caseRepository.List());
            if(!isDemo) CaseMessage.Text = "可新增案件，或選取清單中的案件後按「開啟選取案件」。";
        }
        catch (Exception exception)
        {
            caseRepository = null;
            Trace.TraceError("Case storage initialization failed: {0}", exception);
            CaseMessage.Text = "無法開啟本機案件資料，請確認資料夾權限與資料庫狀態，再重新開啟程式。";
        }
        finally { SetCaseBusy(false); }
    }

    private void SetCaseBusy(bool busy, string? message = null)
    {
        caseOperationInProgress = busy;
        Workspace.IsEnabled = !busy && caseRepository is not null;
        if(!busy && activeCaseId is long id) {try{HistoryDetails.Text=ExecutionHistory.Read(databasePath,id);}catch{HistoryDetails.Text="執行紀錄暫時無法讀取。";}}
        if (message is not null) CaseMessage.Text = message;
    }

    private bool ConfirmDiscard() => !CaseForm.IsDirty || MessageBox.Show(this,
        "目前有未儲存的輸入，確定放棄這些變更？", "未儲存的變更", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    private void NewCase(object sender, RoutedEventArgs e)
    {
        if (caseOperationInProgress || !ConfirmDiscard()) return;
        activeCaseId = null; activeRevisionId = null;
        CaseForm.SetInput(new CaseInput());
        ResetChart();
        CaseList.SelectedItem = null;
        CaseHeading.Text = "新案件";
        WorkspaceTabs.SelectedIndex = 0;
        CaseMessage.Text = "請填寫必要資料，再按「儲存案件」。";
    }

    private async void OpenSelectedCase(object sender, RoutedEventArgs e)
    {
        if (caseOperationInProgress || caseRepository is null) return;
        if (CaseList.SelectedItem is not CaseSummary summary)
        {
            CaseMessage.Text = "請先從左側清單選擇案件。";
            return;
        }
        if (!ConfirmDiscard()) return;
        SetCaseBusy(true, "正在開啟案件…");
        try
        {
            StoredCase saved = await Task.Run(() => caseRepository.Load(summary.NamingCaseID));
            SetCurrentCase(saved);
            CaseMessage.Text = "案件已開啟；修改後可另存為下一版。";
            await RestoreChart(saved.CaseRevisionID);
            await RestoreAnalysis(saved.CaseRevisionID);
            await RestoreWorkflow(saved.CaseRevisionID);
        }
        catch (Exception exception)
        {
            Trace.TraceError("Case loading failed: {0}", exception);
            CaseMessage.Text = "無法開啟案件；目前表單未被取代。請確認資料庫與案件狀態。";
        }
        finally { SetCaseBusy(false); }
    }

    private async void SaveCase(object sender, RoutedEventArgs e)
    {
        if (caseOperationInProgress || caseRepository is null) return;
        if (activeCaseId is not null && !CaseForm.IsDirty)
        {
            CaseMessage.Text = "目前沒有尚未儲存的變更。";
            return;
        }
        SetCaseBusy(true, "正在儲存案件…");
        try
        {
            CaseInput input = CaseForm.ReadInput();
            StoredCase saved = await Task.Run(() => caseRepository.Save(input, activeCaseId, activeRevisionId));
            SetCurrentCase(saved);
            CaseMessage.Text = $"案件 #{saved.NamingCaseID} 已儲存（第 {saved.RevisionNumber} 版）。地點與需求的後續整理尚未執行。";
            try
            {
                CaseList.ItemsSource = await Task.Run(() => caseRepository.List());
                CaseList.SelectedItem = CaseList.Items.Cast<CaseSummary>().FirstOrDefault(c => c.NamingCaseID == saved.NamingCaseID);
            }
            catch (Exception exception)
            {
                Trace.TraceError("Case list refresh failed after save: {0}", exception);
                CaseMessage.Text = $"案件 #{saved.NamingCaseID} 已成功儲存，但清單更新失敗；重新開啟程式後可讀取。";
            }
        }
        catch (CaseInputException exception) { CaseMessage.Text = string.Join("　", exception.Errors); }
        catch (CaseConflictException exception) { CaseMessage.Text = exception.Message + "目前輸入仍保留在表單中。"; }
        catch (Exception exception)
        {
            Trace.TraceError("Case saving failed: {0}", exception);
            CaseMessage.Text = "儲存未完成，目前輸入仍保留。請確認資料庫可寫入，再重試。";
        }
        finally { SetCaseBusy(false); }
    }

    private void SetCurrentCase(StoredCase saved)
    {
        CaseForm.SetInput(saved.Input);
        activeCaseId = saved.NamingCaseID; activeRevisionId = saved.CaseRevisionID;
        ResetChart();
        CaseHeading.Text = $"案件 #{saved.NamingCaseID}｜第 {saved.RevisionNumber} 版";
        WorkspaceTabs.SelectedIndex = 0;
    }

    private void CheckBeforeClosing(object? sender, CancelEventArgs e)
    {
        if (caseOperationInProgress || previewOperations.IsBusy)
        {
            e.Cancel = true;
            CaseMessage.Text = "正在處理案件或等待原生列印完成，請稍候再關閉；取消等待不會立即中止原生列印。";
        }
        else e.Cancel = !ConfirmDiscard();
    }
}

