# 開發慣例

本頁保存專案已採用的技術與慣例。來源與尚未定案的技術選擇見 [專案狀態](project-status.md)；本輪骨架範圍與驗證見 [桌面初始化紀錄](desktop-bootstrap.md)。

## 桌面技術方向

Windows 本機應用程式、SQLite 與 C#／WPF＋WebView2 均已由使用者確認，取代先前以網站與 PostgreSQL 為前提的方向。

| 範圍 | 方向 | 狀態 |
| --- | --- | --- |
| 執行形式 | Windows 本機應用程式；第一版以單機案件處理為範圍 | 已確認，桌面骨架可啟動 |
| 案件儲存 | 本機 SQLite；不加入多機同步 | Schema v1 與案件新增／儲存／重新開啟已實作；刪除與備份／還原未實作 |
| 操作介面 | C#／WPF | 案件清單、Step 1 表單與 Step 2 城市確認／排盤介面已實作 |
| 報告預覽與 PDF | HTML／CSS 報告搭配 WebView2，本機預覽與 PDF 輸出 | 本機空白預覽已驗證；正式報告與 PDF 輸出尚未實作 |
| 模型呼叫 | OpenAI 或 Claude API，以 adapter 切換；必要資料依步驟送往服務 | HttpClient 直接呼叫 Responses API／Messages API；兩者皆以假 transport 測試，模型由設定指定 |
| 開發環境 | .NET 10、Windows Desktop Runtime；SDK 選擇由根目錄 global.json 管理 | 依本機現有正式環境採用；WebView2 SDK 版本及相依鎖定由 csproj／packages.lock.json 管理 |
| 資料庫依賴 | Microsoft.Data.Sqlite，不採 ORM | 資料庫工具與桌面案件存取共用；套件版本由其 csproj／鎖檔管理 |
| 排盤依賴 | 本機 OpenFate MCP、C# MCP SDK、NodaTime 與離線 GeoNames | 版本、還原及互動見 [Step 2](step-2.md)；正式安裝包尚未實作 |

### 桌面介面決策與取捨

已採 C#／WPF＋WebView2，理由是目前只需支援 Windows，並可沿用 Obsidian 中的 .NET 程式設計經驗；報告則維持 HTML／CSS 頁面形式。WPF 是本專案經使用者確認的選擇，不是 Obsidian 筆記原先指定的框架。

| 方案 | 適用之處 | 本案取捨 |
| --- | --- | --- |
| C#／WPF＋WebView2 | WPF 提供控制項、資料繫結與版型；WebView2 嵌入 HTML 報告並提供 PDF 列印 API | 已採用；需處理 XAML 介面、WebView2 Runtime 與安裝打包，PDF 分頁與字型仍須實測 |
| C#／WinForms＋WebView2 | 視覺化設計器適合傳統表單工具 | 未採用；本案選擇 WPF 的資料繫結與版型能力 |
| Electron＋React | 可沿用 Web UI 技術與 Node.js 生態 | 未採用；目前以已確認的 .NET 桌面方向實作 |

官方能力依據：[WPF](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/)、[WinForms](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/overview/)、[WebView2](https://learn.microsoft.com/en-us/microsoft-edge/webview2/)、[WebView2 列印與 PDF](https://learn.microsoft.com/en-us/microsoft-edge/webview2/how-to/print)、[Electron process model](https://www.electronjs.org/docs/latest/tutorial/process-model)。

目前不把 React／Ant Design Pro 當作介面預設，也不預設需要 ASP.NET Core Web API。Obsidian 的後台 UI 筆記保留為流程與資訊設計參考，eLite 的 Blazor workspace、靜態設定橋接與自訂 Redis session 不直接沿用。保留 DI、分功能設定驗證、參數化查詢、明確交易與取消支援等原則；async I/O 按實際 provider 能力處理，Microsoft.Data.Sqlite 的限制見 [Schema 規劃](database-schema.md)。

## SQLite 資料設計

| 項目 | 專案慣例 |
| --- | --- |
| Table、column | 無底線 PascalCase；命名中的 ID 保留大寫 |
| PK | `{TableName}ID` |
| 邏輯參照 | `for{Entity}ID`；不建立 physical FK |
| 索引 | 一般 `IX_`、唯一 `UX_`；按實際查詢與唯一性範圍設計，不因邏輯參照自動建索引 |
| NULL | 除 PK 及另行確認的必要例外外允許 NULL；畫面必填不自動等同所有儲存階段都必須 NOT NULL |
| Default | 只在具有明確業務意義時設定，不用假出生時間、假姓名或空字串代替未知資料 |
| 描述 | 在資料結構規格或 migration 註解中記錄 Table、column 的用途、值域、狀態及時間基準；不照搬其他 DB 的描述語法 |
| State | 僅二元啟停：`0=正常／啟用`、`1=停用` |
| Status | 多階段生命週期，與 State 分開 |
| 時間 | 依用途採 UTC 或 Asia/Taipei；同欄位與同流程一致，記錄基準。出生當地時間及其時區不可當成系統事件時間混存 |
| 業務處理 | 由應用程式處理，不沿用原筆記的 stored procedure 命名與建置習慣 |

### SQLite 適用差異

- 保留上述 PascalCase 命名習慣；DDL、查詢與資料映射使用一致名稱，不以 PostgreSQL 的大小寫處理規則解釋 SQLite。
- SQLite 的型別與日期時間儲存方式不同於 PostgreSQL；實作 schema 時明確訂定型別及時間格式，不因欄位名稱像日期就假設 DB 會自動強制其格式。[官方說明](https://www.sqlite.org/datatype3.html)
- DEFAULT 依 SQLite 欄位定義處理，不保留原筆記 SQL Server 的 `DF_{Table}_{Column}` 物件命名要求。[官方說明](https://www.sqlite.org/lang_createtable.html)
- 有條件的唯一性可使用 partial unique index；只有實際需求具有條件時才採用，不自行決定停用資料可重複。[官方說明](https://www.sqlite.org/partialindex.html)
- 不建立 physical FK，因此實際設計關聯時須明確處理參照存在性及更新／刪除一致性；本輪不預先定義各資料表策略。
- DB 檔案位置與版本管理以 [Schema 規劃](database-schema.md) 為準。正式備份／還原尚未實作；執行中的備份需使用 SQLite 支援的一致性備份方法，不把直接複製使用中的主檔當成完整備份流程。[備份說明](https://www.sqlite.org/backup.html)

Schema v1 的正式定義、與此處慣例的適用方式及驗證邊界見 [Schema 規劃](database-schema.md)。未來只有在多機共用、多人協作等需求成立時才重新評估集中式資料庫；目前不預建 SQLite／PostgreSQL 雙支援。

## UI 與工具

- 桌面介面優先使用成熟且授權可用的框架、版型與元件；先完成表單、狀態回饋與報告閱讀，不為外觀加入多套 UI 框架。
- 報告預覽 PAGE 在本機顯示，不要求公開網址；是否另提供 HTML 匯出尚未定案。PAGE 與 PDF 應以同一份報告資料為來源，分頁與輸出仍需驗證。
- 只有跨專案共用、不須隨專案鎖版的獨立 CLI 才考慮 npm global；runtime／build／test dependency 不放 global。
- 未採 npm 的範圍，不為測試擅自建立 package.json、lockfile 或 node_modules。
- Coding agent 的瀏覽器操作與正式可重現 E2E 分開處理；使用全域 playwright-cli 不等於已建立專案 E2E suite。
- 上述瀏覽器驗證只適用實際可操作的 Web 範圍；WPF／WinForms 原生介面的驗證方法依選定技術另定，不將 Playwright 當作原生桌面控制項測試方案。
