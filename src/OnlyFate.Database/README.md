# OnlyFate.Database

SQLite schema 初始化、唯讀查核與 Step 1–6 共用服務。桌面直接使用同一 assembly；排盤透過 MCP 啟動本機 Node.js，模型以 HttpClient 呼叫 Responses API。

Schema v1 從 assembly 內嵌 SQL 載入，Microsoft.Data.Sqlite 使用參數化 SQL、明確交易與每次操作獨立連線，沒有 ORM 或 stored procedure。

- `CaseRepository`：表單檢查、國曆／農曆、不可變案件版本。
- `Step2Service`：城市、歷史時區、MCP 與回傳檢查。
- `Step3Service`／`Step4Service`／`Step5Service`：每階段獨立 context、契約與結果；共用 `ModelCallRunner` 處理最多一次修正。
- `NamingEngine`：有限字庫、五格三才、禁用字／讀音／配對計算。
- `ReportRepository`／`ReportArtifactService`：固定報告資料、來源檢查、HTML／PDF 收據與 hash。
- `DemoWorkspace`：獨立 DB 的虛構示範，不建立假的模型呼叫紀錄。

權威入口：[資料庫操作](../../database/README.md)、[Schema](../../docs/database-schema.md)、[AI 呼叫](../../docs/ai-call-flow.md)、[Step 4–6](../../docs/step-4-6.md)、[測試](../../tests/README.md)。
