# OnlyFate

供沒有命理經驗的商家使用的新生兒取名報告生成系統。第一版採 Windows 本機應用程式與 SQLite，提供本機報告預覽 PAGE 與 PDF，降低客製化服務的製作成本。OpenAI API 仍需連網。

目前已接通 Step 1–6：案件輸入／儲存、OpenFate 排盤、AI 試行判讀、受限字庫選名、報告短文、本機 PAGE 與 PDF 匯出。提供獨立離線示範，可先驗收操作與成品。命理方法與字庫仍屬研究試行，尚不能宣稱正式商用品質；實際 OpenAI API 尚待提供憑證驗證。

## 從這裡開始

- [文件路由](docs/map.md)：依工作範圍選擇要讀的文件。
- [開始驗收](docs/acceptance.md)：啟動發行版、離線示範與本輪驗證結果。
- [已確認的 PDF](output/pdf/onlyfate-naming-report-v11.pdf)／[新版 HTML 預覽](output/pdf/onlyfate-naming-report-v12.html)：螢幕預覽與六頁 PDF 共用內容、標題層級及主推標示；螢幕版提供段落跳轉與自適應字卡，PDF 維持原有配置。
- [AI context 與呼叫流程](docs/ai-call-flow.md)：三次模型呼叫、資料分工與修正上限。
- [專案狀態與邊界](docs/project-status.md)：已確認方向、未決事項與本輪初始化範圍。
- [Step 1：案件輸入與整理](docs/step-1.md)：目前已確認的輸入與處理約定。
- [Step 2：出生地、時區與排盤](docs/step-2.md)：MCP 參數、計算政策與 SQLite 保存方式。
- [Step 3：AI 試行判讀](docs/step-3.md) 與 [Step 4–6](docs/step-4-6.md)：各階段契約與檢查邊界。
- [桌面專案操作](src/OnlyFate.Desktop/README.md)：開發需求與已驗證的還原、建置、啟動指令。
- [DB Schema 規劃](docs/database-schema.md) 與 [資料庫操作](database/README.md)：資料表、寫入契約與建置／查核入口。
- [開發慣例](docs/development-conventions.md)：已確認的桌面技術、SQLite 與工具使用慣例。
- [研究資料入口](docs/research/README.md)：既有試算、證據與報告樣張。
- [報告改版規格](docs/report-redesign.md)：兩組完整字池、逐字說明與新版視覺驗收標準。
- [歷史 PDF Mockup](output/pdf/lin-baby-naming-mockup.pdf)：早期內容評估，並非目前成品設計標準。

## 目前目錄

| 位置 | 用途 |
| --- | --- |
| docs/ | 產品約定、開發慣例、研究入口與文件治理 |
| src/ | Windows 桌面專案與資料庫工具，見 [原始碼入口](src/README.md) |
| database/ | SQL migration、欄位註解與唯讀邏輯參照查核 |
| tests/ | 不顯示視窗的核心及 WPF 測試，見 [測試入口](tests/README.md) |
| tools/ | 專案使用的執行工具，見 [OpenFate runtime](tools/openfate/README.md) |
| resources/ | 隨程式使用的公開參考資料，見 [GeoNames 說明](resources/geography/README.md) |
| 根目錄既有 lin-baby-*、calendar-conversion-* | 保留原路徑的研究紀錄與計算證據 |
| output/ | 發行程式、最新報告與歷史 Mockup，見 [產物入口](output/README.md) |
| tmp/ | 可重建的研究暫存、產圖腳本與驗證紀錄，不納入 Git |

## 已驗證操作

在專案根目錄執行 `git status --short --branch` 可檢視工作樹。啟動桌面程式、資料庫操作及測試各以其 README 為準；桌面初始化實測見 [初始化紀錄](docs/desktop-bootstrap.md)。目前尚無安裝包。
