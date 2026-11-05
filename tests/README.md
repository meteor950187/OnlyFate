# 自動化測試

測試使用 `%TEMP%\OnlyFate.Database.Tests\<隨機識別碼>` 與 `%TEMP%\OnlyFate.Desktop.Tests\<隨機識別碼>`，不寫入正常案件 DB。先依 [桌面 README](../src/OnlyFate.Desktop/README.md) 還原 NuGet 與 OpenFate runtime，再執行：

```powershell
dotnet test OnlyFate.slnx --no-restore --artifacts-path tmp/pools-ui-build
```

2026-11-05 全套 **298 項通過，0 失敗、0 略過：Database 223、Desktop 75**。上述輸出路徑先以 `dotnet restore OnlyFate.slnx --locked-mode --artifacts-path tmp/pools-ui-build` 還原。

- 案件／SQLite：schema、交易、農曆、NULL 與明確沒有、版本追加、競爭與過期來源拒絕。
- 排盤：4 次真正本機 C#→Node MCP 計算（林姓、紐約夏令、台灣 1979 夏令、午夜跨日），另驗證 DST 空檔／重複、城市精度、缺漏／矛盾回傳與取消。
- AI：假 transport 驗證三階段 context 白名單、schema、引用、一次修正、逾時／取消／拒答、usage、key 不落盤。OpenAI 與 Claude adapter 另驗證 request 形狀、Claude schema 限制轉換、stop_reason 對應、header 與 Provider 紀錄。不呼叫真實模型 API。
- 選名：五格／三才、避諱與已知同音、必用字、字庫未知值停止；兩組 4–5 字完整字池、所有交叉配對、重複字／跨組交集、竄改事實及不足兩組拒絕。固定必用字與完整自由字池衝突時不放寬限制。
- 報告：正常三階段假 transport 與離線示範共用新版契約；固定事實、逐字 profile、HTML 編碼、來源／hash／版本、受控路徑、格式獨立狀態及無 AI 不可變副本重建。
- Desktop：STA 控制項與未顯示視窗、流程狀態及 37 項匯出路徑保護測試；新增初始化重試、原生列印取消後仍持鎖、切案不導航及各格式匯出獨立。建立 WPF 控制項的三類測試共用 collection，避免共用 XAML package 資源被多個 STA 同時載入；不呼叫 Window.Show、不初始化 WebView2、不操作滑鼠鍵盤。

報告導覽另有11項網址限制測試：允許當前報告與fragment，拒絕其他報告、query、外站及非預期協定。v12 HTML以背景Edge驗證四種寬度、完整字義、長文及停用JavaScript時的錨點導覽；列印前後六頁逐字及像素比對一致。真正WebView2預覽與PDF匯出的既有驗收紀錄見[開始驗收](../docs/acceptance.md)，本輪未操作桌面。這不等於真實模型品質或命理方法已驗證。

`Fixtures/openfate-lin.json` 位於 `OnlyFate.Database.Tests/`，為虛構案例透過本機 MCP 0.3.1 計算的原始回傳，不是真實客戶資料。

本輪命名依據增補：驗證報告引用相同 Step 3 run、拒絕方向及命盤引用值被竄改；原有 schema/hash/來源失效測試全部保留。
