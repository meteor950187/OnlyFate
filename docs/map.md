# 文件路由

| 文件 | 角色與權威範圍 | 讀取時機 | 生命週期 | 關聯文件 |
| --- | --- | --- | --- | --- |
| [README](../README.md) | 人類導覽與已驗證操作入口 | 進入專案時 | 持續維護 | 專案狀態、研究入口 |
| [AGENTS](../AGENTS.md)／[CLAUDE](../CLAUDE.md) | 全專案穩定 Agent 規則；兩份鏡像等價 | 每次開始工作 | 穩定 | 開發慣例 |
| [docs README](README.md) | 文件收錄、排除與治理方式 | 新增或整理文件時 | 穩定 | 本路由 |
| [開始驗收](acceptance.md) | 發行版、離線示範、實測成品與驗證界線 | 驗收與重開專案時 | 持續維護 | 桌面 README、測試、專案狀態 |
| [專案狀態](project-status.md) | 開發階段、未決事項與初始化來源 | 確認實作範圍或技術決策時 | 持續維護 | Step 1、開發慣例、研究入口 |
| [Step 1](step-1.md) | 案件輸入、備註原文使用與輸出約定 | 修改表單或備註處理時 | 持續維護 | 專案狀態、Step 3、研究入口 |
| [Step 2](step-2.md) | 出生地／時區、MCP 互動、回傳檢查與執行紀錄契約 | 修改排盤、工具呼叫或紀錄保存時 | 持續維護 | Step 1、DB Schema、OpenFate runtime |
| [Step 3 試行判讀](step-3.md) | 首次 AI 呼叫的 context、API 職責、有限修正、候選方法與檢查邊界 | 修改判讀或準備規則驗證時 | 試行中 | Step 1、Step 2、DB Schema、案例試走 |
| [Context 與呼叫流程](ai-call-flow.md) | 三次模型呼叫的分工、context 與 harness 生命週期導覽 | 理解或盤點 AI 串接時 | 持續維護 | Step 3、Step 4–6 |
| [Step 4–6](step-4-6.md) | 配置組名、報告與交付的實作契約 | 修改選名或報告產物時 | 試行中 | 有限字庫、DB Schema、AI 呼叫流程 |
| [報告與字池改版](report-redesign.md) | 兩組自由配字、逐字說明與視覺改版需求及實作順序 | 回查本輪需求與驗收時 | 過渡中 | Step 4–6、有限字庫、驗收紀錄 |
| [有限字庫](../resources/naming/README.md) | 已收字、來源、涵蓋及試行分類界線 | 修改選字、筆畫或讀音時 | 試行中 | 案例試走、Step 4–6 |
| [完整流程實作計畫](end-to-end-implementation.md) | 本次工作範圍、分段順序及驗收要求 | 回查實作範圍時 | 過渡中 | Step 1–6、專案狀態 |
| [tools README](../tools/README.md)／[OpenFate runtime](../tools/openfate/README.md) | 本機 MCP 的執行依賴、鎖版及還原入口 | 安裝或修改排盤執行依賴時 | 持續維護 | Step 2、桌面專案 README |
| [resources README](../resources/README.md)／[GeoNames](../resources/geography/README.md) | 公開地理資料來源、署名、精度與版本收據 | 更新城市資料或檢查授權時 | 持續維護 | Step 2 |
| [開發慣例](development-conventions.md) | 已採用的桌面技術、資料庫與工具設計習慣 | 修改桌面介面、設計資料或引入工具時 | 持續維護 | Agent 規則、專案狀態 |
| [桌面初始化紀錄](desktop-bootstrap.md) | 本輪骨架建立範圍與實際驗證結果 | 確認初始化完成範圍時 | 過渡中 | 開發慣例、專案狀態 |
| [桌面專案 README](../src/OnlyFate.Desktop/README.md) | 桌面應用程式開發需求與已驗證操作入口 | 還原、建置或啟動桌面程式時 | 持續維護 | 桌面初始化紀錄、開發慣例 |
| [DB Schema](database-schema.md) | 資料表責任、邏輯關聯、寫入契約及建置驗證邊界 | 修改資料結構或案件存取時 | 持續維護 | Step 1、開發慣例、database README |
| [database README](../database/README.md) | SQL 目錄責任與初始化／唯讀查核操作入口 | 建立或檢查本機 DB 時 | 持續維護 | DB Schema、資料庫工具 README |
| [初始 DDL](../database/migrations/001_Initial.sql) | v1 欄位、值域、default、索引與欄位用途的權威定義 | 查閱欄位或設計下一版 migration 時 | 穩定 | DB Schema、database README |
| [邏輯參照查核](../database/checks/logical-references.sql) | 非 NULL 邏輯孤兒參照的唯讀查詢 | 修改邏輯關聯查核時 | 持續維護 | DB Schema、初始 DDL |
| [tests README](../tests/README.md) | 測試範圍、隔離方式與已驗證入口 | 執行或新增資料庫測試時 | 持續維護 | DB Schema、database README |
| [研究入口](research/README.md) | 導覽既有研究證據並區別歷史樣張 | 驗證工具能力或回查案例時 | 持續維護 | Step 1、案例試走 |
| [案例試走](../lin-baby-naming-walkthrough.md) | 該次完整人工試走的依據、判斷與驗證邊界 | 回查命名研究依據時 | 過渡中 | 研究入口、歷史文字樣張 |
| [歷史文字樣張](../lin-baby-report-sample.md) | 原始條件式報告文字試作 | 比較歷次報告內容時 | 過渡中 | 案例試走、精簡 Mockup 文稿 |
| [精簡 Mockup 文稿](../output/pdf/lin-baby-naming-mockup.md) | 已產出 PDF 的精簡內容樣本 | 討論家長閱讀內容與版型時 | 過渡中 | 研究入口、歷史文字樣張 |

[Step 3 實作計畫](step-3-implementation.md)：本輪工作範圍與驗證順序，生命週期為歷史紀錄。

[報告依據與閱讀節奏增補](report-rationale.md)：本輪補強需求、來源接線與驗證範圍。

[報告的螢幕與PDF編排](report-print-layout.md)：共用內容、螢幕閱讀、印刷頁比較任務與驗收標準。
