# OnlyFate.Desktop

WPF 桌面入口，已接通案件輸入、出生城市／排盤、AI 試行判讀、取名配置、WebView2 報告與 HTML／PDF 匯出。操作與驗收範圍見 [開始驗收](../../docs/acceptance.md)。

## 開發與執行需求

- Windows；開發使用 .NET 10 SDK，global.json 允許較新 feature band，本次 SDK 10.0.403。
- 執行需要 .NET 10 Windows Desktop Runtime、WebView2 Evergreen Runtime。目前為 framework-dependent 輸出，未製作安裝包。
- 排盤需要 PATH 中的 Node.js 20 以上；[OpenFate runtime](../../tools/openfate/README.md) 隨建置複製，初次還原需要網路。
- 模型服務（OpenAI 或 Claude）、模型 ID 與 API key 可在「AI 試行判讀」選擇及輸入，或由 `OPENAI_MODEL`／`OPENAI_API_KEY`、`ANTHROPIC_MODEL`／`ANTHROPIC_API_KEY` 環境變數提供；切換服務會清除本次輸入的 key。key 僅留本次程序記憶體，可清除；不寫入 SQLite。

## 建置與執行

Repository 根目錄依序執行：

```powershell
npm ci --prefix tools/openfate --ignore-scripts --no-audit --no-fund
dotnet restore OnlyFate.slnx --locked-mode --artifacts-path tmp/final-build
dotnet build OnlyFate.slnx --no-restore --artifacts-path tmp/final-build
dotnet test OnlyFate.slnx --no-restore --artifacts-path tmp/final-build
dotnet publish src/OnlyFate.Desktop/OnlyFate.Desktop.csproj -c Release --artifacts-path tmp/release-build -o output/app --no-self-contained -p:RestoreLockedMode=true
```

上述命令不開視窗。開啟 `output/app/OnlyFate.Desktop.exe`；保留整個資料夾內的相依檔、Assets、OpenFate 與 Credits。可加 `--demo` 直接開啟離線虛構案例，或由主畫面右下角進入。

## 本機資料與流程

正常資料位於 `%LOCALAPPDATA%\OnlyFate`：`Data/OnlyFate.db`、`Reports/`、`WebView2/`；示範資料獨立於同根下 `Demo/`。不要手動覆寫內部報告檔，程式會核對 SHA-256。另存功能拒絕整個內部資料根目錄及 reparse point 路徑。

新增案件→儲存→查找並確認出生城市代表點→排盤→AI 試行判讀→取名配置→生成報告→預覽／匯出。每個 AI 階段可先查看實際 request；執行紀錄頁顯示階段、模型／工具、狀態與 usage 摘要；完整 request／response 保存在 SQLite 的 ExecutionCall，不在此摘要框展開。重開不自動付費呼叫；上游重做或案件改版會讓舊下游結果失效，舊紀錄仍保留。

WebView2 僅接受當前報告唯一的 `report.onlyfate.local` 內部 URI，回應已核對的 HTML；不是公開網站，不映射整個資料夾，不載入遠端資源。PDF 以同份 HTML 列印。

新版取名配置為兩組完整字池，每組一個主推與四個替代示例；報告逐一解釋各池用字。HTML／PDF 分別保存產物狀態。檔案遺失或損壞時，可按「重建報告檔案（不呼叫 AI）」由已保存資料建立新副本；原資料與紀錄保留。若案件、上游來源或資料本身失效，不能用重建繞過來源驗證。

目前為有限字庫與試行規則。實際 GUI／PDF 驗收及 API 驗證界線見 [驗收紀錄](../../docs/acceptance.md)，自動化測試見 [測試入口](../../tests/README.md)。
