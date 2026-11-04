# SQLite 結構與操作

此目錄保存資料定義、migration 與唯讀邏輯參照查核。SQL 中的欄位註解是用途、值域、default 與時間基準的權威來源；設計取捨與關係見 [Schema 規劃](../docs/database-schema.md)。

初始化與查核由 [C# 資料庫工具](../src/OnlyFate.Database/README.md) 執行，不需要安裝 sqlite3 CLI、Python 或雲端服務。桌面 UI 已使用同一 assembly 的 CaseRepository 存取案件；本輪沿用 schema v1，沒有改寫 migration。

## 已驗證操作

在 Repository 根目錄執行：

```powershell
dotnet restore OnlyFate.slnx --locked-mode
dotnet build src/OnlyFate.Database/OnlyFate.Database.csproj --no-restore
dotnet run --project src/OnlyFate.Database/OnlyFate.Database.csproj --no-build -- init
dotnet run --project src/OnlyFate.Database/OnlyFate.Database.csproj --no-build -- verify
```

預設 DB 位於 `%LOCALAPPDATA%\OnlyFate\Data\OnlyFate.db`。指令輸出實際位置、schema／SQLite 版本、資料表清單及邏輯參照錯誤。

工具也接受第二個參數指定 DB 路徑。`init` 只初始化空白 DB 或驗證既有 v1，不覆寫陌生 DB；`verify` 使用唯讀連線，檔案不存在時回報錯誤，不建立新檔。發現孤兒參照、版本／checksum 不符或完整性錯誤時，退出碼為非零。

目前只有 schema v1；後續變更需新增 migration 及升級邏輯，不編輯已套用腳本。不要直接重跑 SQL 腳本取代版本工具，也不要以刪除 DB 解決升級問題。正式備份／還原流程尚未實作。
