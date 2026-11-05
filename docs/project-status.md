# 專案狀態

更新日期：2026-11-03。

## 已完成

Windows 本機端到端試行版已接通：Step 1 案件儲存／重開、Step 2 本機 OpenFate MCP 排盤、Step 3 AI 判讀、Step 4 兩組完整配字方案、Step 5 短文與固定報告、Step 6 PAGE／HTML／PDF。各步可查看實際 request 與執行紀錄；案件和上游改版會使舊下游失效。

- WPF＋WebView2、SQLite v1 九張資料表；沒有新增 Web 服務或修改已套用 migration。
- 獨立離線示範、不可變報告資料／產物 hash、內部路徑保護、有限修正與取消。
- 本輪兩組字池與逐字報告已完成新版發行、實際 HTML／PDF 視覺驗收及全流程審查；前版兩頁 PDF 屬歷史資料。當前成品、測試與驗證界線見 [開始驗收](acceptance.md)。
- 新版每組兩個 4–5 字池、1 主推與 4 個示例；字池所有交叉配對由程式檢查。固定報告資料與不可變產物副本支援無 AI 重建。
- Git 目前為 `feature/local-report-workflow`，尚無初始 commit／遠端；既有未追蹤研究檔保留。

## 已確認方向

產品目標仍是非專業商家可交付的正式命名報告；本輪完成的是供驗收的技術流程，不能以流程跑通代替命理品質認證。Windows／SQLite 取代先前 PostgreSQL；案件與報告保存在本機，OpenAI API 仍需網路。

備註保留原文，不建立獨立 AI 整理階段。正常三次模型呼叫，各自組成 context；詳見 [呼叫流程](ai-call-flow.md)。桌面與資料慣例見 [開發慣例](development-conventions.md)，契約見 [Step 1](step-1.md)、[Step 2](step-2.md)、[Step 3](step-3.md)、[Step 4–6](step-4-6.md)。

## 尚未完成的品質與發行條件

- 真實模型 API 端到端驗證：OpenAI 與 Claude adapter 都只有假 transport 測試及明示離線示範；Claude API 額度尚未發放，真實 request 是否被接受、品質、成本與延遲都未驗證。
- 命理取用方法及字五行品質驗證：林姓案例土金／金水分歧未解，所有正式交付旗標維持 false。
- 可用字庫擴充及來源／授權確認：目前僅研究用有限字庫，實際姓氏覆蓋以林姓為主；未知姓氏、必用字或同音查核不能猜。
- 更完整城市／精確地址定位與重複時刻介面，不能由四個 MCP 測試案例推論所有年份地點都涵蓋。
- 正式安裝包、Node.js 隨附／更新、備份還原與案件刪除。本輪提供需既有 runtime 的本機發行資料夾。
- 正式散佈前仍需處理 true-solar-time 套件未附 LICENSE 的授權通知，見 [runtime 說明](../tools/openfate/README.md)。

## 初始化來源

2026-11-02 透過官方 Obsidian CLI，唯讀查閱 meteorVault 的下列筆記；未修改 Vault。專案文件保存經採用的約定，來源筆記日後變更不會自動改寫專案規則。

| 筆記路徑 | 本輪採用範圍 |
| --- | --- |
| AI Agent 協作/AI Agent 協作專案初始化標準.md | Agent 鏡像入口、單一權威來源、最小目錄、文件路由與驗證原則 |
| AI Agent 協作/我的DB 設計習慣.md | 資料命名、邏輯關聯、nullable、狀態、時區及描述慣例；依現行 SQLite 適配 |
| AI Agent 協作/我的後台設計習慣.md | 有管理端且採 React 時的預設偏好 |
| AI Agent 協作/Ant Design Pro 預設設計摘要.md | 條件式 UI 參考，這一輪不建立 UI |
| AI Agent 協作/npm 全域安裝原則.md | 全域工具與專案依賴分界、非 npm 專案邊界 |
| AI Agent 協作/Playwright E2E 與 Coding Agent 瀏覽器策略.md | Agent 瀏覽器操作與正式 E2E 的分界 |
| 開發知識/eLite 專案知識索引.md | 判讀 reference 與 confirmed 範例的邊界，定位前後端相關筆記 |
| 開發知識/eLite 復盤/Blazor 後台 UI 架構.md | Web 後台偏好與流程設計參考；Windows 方向確認後不再視為本專案 UI 技術預設 |
| 開發知識/ASP.NET Core/AppSetting 與 Configuration.md | C#／ASP.NET Core 範例、feature Options、DI 與設定驗證原則 |
| 開發知識/ADO.NET/eLite DBAccess 設計與限制.md | 舊式同步 static helper 與隱含交易的適用限制 |
| 開發知識/ADO.NET/Async DB Helper Template.md | async、參數化、明確交易、取消支援；SQL Server 範例不直接搬到 SQLite |
| 開發知識/ASP.NET Core/eLite Backend Runtime Access Map.md | 釐清既有專案登入／Redis session 不是新專案的預設架構 |

## 本輪完成條件

- Agent 入口逐位元組一致，Markdown 本機連結均可解析。
- Map 只保存 metadata 與閱讀路由，不重述規格。
- 沒有未確認框架的空殼、虛構啟動指令或專案依賴。
- 既有研究檔案內容與路徑保持不變，Git 正確排除暫存與密鑰檔案。
