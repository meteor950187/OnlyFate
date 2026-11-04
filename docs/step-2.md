# Step 2：出生地、時區與 OpenFate 排盤

狀態：已接入桌面與 SQLite，經背景 MCP 實測。此步只有程式與計算工具，**不呼叫 LLM**；不做喜用神、姓名判讀或取名。依已保存且通過格式檢查的出生欄位排盤；備註按 [Step 1](step-1.md) 約定保留原文供後續使用，不設前置 AI 整理或需求確認。

## 處理流程

| 順序 | 處理與停止條件 | 留下什麼 |
| --- | --- | --- |
| 1. 固定輸入版本 | 必須先儲存案件；表單有未儲存修改時先儲存 | NamingCaseID、CaseRevisionID、原始出生欄位快照 |
| 2. 確認出生城市 | 用國家＋城市查本機 GeoNames 候選，由商家選取並確認城市代表點精度；不自動選第一筆或代用鄰近城市 | GeoNameID、座標、IANA 時區、行政區、資料來源版本及精度確認 |
| 3. 解析出生時刻 | NodaTime 的 TZDB 依「出生當年」解析標準時差與 DST，不用今天的時差 | 原始當地時間、標準／夏令／合計時差、UTC、TZDB 版本 |
| 4. 呼叫 MCP | C# MCP client 開啟本機 Node.js stdio 程序，先讀政策，再傳資料排盤；無 HTTP 服務或模型中轉 | 兩次工具的參數、原始回傳、版本、耗時及狀態 |
| 5. 驗證及保存 | 核對必要欄位、原始時間、政策及 UTC；失敗即停止，不生成替代命盤 | 命盤 JSON、檢查結果與本次執行狀態 |

查不到地點、國家無法辨識或不接受城市精度，就先確認地點，不送 MCP。城市資料涵蓋人口較大的城市及部分行政中心，並非完整全球鄉鎮／地址庫。[GeoNames 資料說明](https://download.geonames.org/export/dump/readme.txt)

**目前只做到城市代表點定位**，選填的醫院／地址仍保留原文，沒有被解析成精確座標。介面會明示並要求確認。真太陽時使用經度，接近時辰或換日邊界時，城市與實際出生點的差距可能影響結果；此版不提供精確地址定位，也不把城市座標冒稱醫院座標。

## 時區與排盤約定

- IANA 時區由選定的 GeoNames 城市取得，例如 `Asia/Taipei`，不要求商家填 UTC offset。
- NodaTime `MapLocal` 有 0 個對應時刻表示 DST 跳時空檔、有 2 個表示重複時刻；兩者均停止並要求核對。此版尚無重複時刻的補充表單，不能自動選早或晚的時刻。[NodaTime 定義](https://nodatime.org/3.3.x/api/NodaTime.TimeZones.ZoneLocalMapping.html)
- 傳入 `timezone` 是當時的**標準時差**，`dstOffset` 另傳；合計是實際 UTC offset。例如紐約夏季為 `-5 + 1 = -4`，不重複加 DST。
- 原始輸入只精確到分鐘；計算以 `second: 0` 代入，快照標記原始秒數未知，不能宣稱精確到秒。
- 目前明確採 `enableTrueSolarTime: true`、`dayBoundaryMode: "ZI_HOUR_23"`（23:00–23:59 採翌日日柱），與這次實測的 MCP 政策一致。這是可追溯的排盤設定，不表示命名取用方法已驗證；後續若改政策，須另產新紀錄。
- 原始生日／農曆生日與排盤用真太陽時分開。真太陽時跨日，不改寫 CaseRevision 的出生日期及 CalendarDataJson。

## MCP 實際互動

固定使用 `@openfate/bazi-mcp@0.3.1`，C# 使用 `ModelContextProtocol.Core`；必要執行環境與安裝入口見 [OpenFate runtime](../tools/openfate/README.md)。啟動時檢查 MCP 回報版本，與固定版本不符則停止。[OpenFate 官方原始碼](https://github.com/openfate-ai/openfate-mcp)

每次排盤開一個本機 MCP 程序，依序：

1. `get_openfate_bazi_policy`，參數 `{}`；保存政策，確認 `DAYUN_SECOND_V2`。
2. `calculate_bazi_chart`，以下為林姓虛構案例的實際參數：

```json
{
  "year": 2026, "month": 6, "day": 15,
  "hour": 12, "minute": 0, "second": 0,
  "gender": "male",
  "longitude": 121.52639,
  "timezone": 8,
  "timezoneId": "Asia/Taipei",
  "dstOffset": 0,
  "calendarType": "solar",
  "enableTrueSolarTime": true,
  "dayBoundaryMode": "ZI_HOUR_23"
}
```

經度取 GeoNames 台北城市代表點 `1668341`；不是醫院座標。緯度留在本機來源快照，此 MCP 工具不需要緯度。姓氏、避諱字、父母姓名、自由備註、農曆及閏月旗標均不傳入。

0.3.1 的 `calculate_bazi_chart` 未公開 `daYunTimingVersion` 參數，內部固定 V2，不能把未支援的參數硬塞進去；實際政策與回傳版本均有檢查。呼叫結束後釋放 MCP client／子程序，不在執行時透過 npx 下載套件。

## SQLite 如何保存

沿用 schema v1，無 migration 變更。每次嘗試各建一筆 run 與 Step 2；不覆寫舊嘗試，也不在等待 MCP 時持有 DB transaction。

| 資料表／欄位 | 內容 |
| --- | --- |
| ProcessingRun.forCaseRevisionID | 當次原始案件版本 |
| ProcessingRun.PolicyJson | `step2-v1` 政策、真太陽時／換日設定、秒數與地點精度約定 |
| ProcessingStep.InputJson | 原始表單、地點與來源版本、時間解析收據、實際 MCP 排盤參數 |
| ExecutionCall | `CallKind=tool`、OpenFate、工具名稱／版本、RequestJson、完整 ResponseJson、耗時與成功／失敗／取消 |
| ProcessingStep.OutputJson | 通過檢查的 `data.chart`，包含原樣保存的四柱、藏干及其他工具資料 |
| ProcessingStep.ValidationJson | 檢查通過與否、錯誤代碼及訊息 |
| ProcessingRun 的錯誤欄位 | 本次失敗／取消原因 |

此版**不回填或改寫 CaseRevision**。BirthTimeZoneID、LocationDataJson 等既有欄位仍保留原狀，排盤使用的地點及時區以本次 InputJson 為準；不把 Step 1 的待整理狀態自動設為 ready。

Step 2 成功時：`ProcessingStep.Status=completed`，`ProcessingRun.Status=waiting`，表示等待後續階段，並非六步皆完成。失敗或取消時兩者分別設 `failed`／`cancelled`。連線尚未建立就失敗時沒有工具呼叫列，但 run／step 有失敗原因；收到工具錯誤則保存原始回傳。

重新開啟案件時，只載入**同一 CaseRevisionID** 最新一次排盤結果，不重新呼叫工具；不宣稱舊結果已經過未來新政策重新驗證。修改表單並儲存新版後清空畫面結果。若另一個視窗在排盤中更新案件，舊嘗試保留紀錄但取消採用，不能貼到新版上。

## 驗證與失敗邊界

- 檢查四柱干支合法且自洽、藏干項目具有合法天干與五行欄位、日主與日干一致、民用出生時刻回傳一致，以及有效的排盤日期時間。
- 檢查真太陽時、換日政策、經度、標準時差及 DST 實際生效；工具解析的 UTC／合計時差必須與 NodaTime 相符，不一致即停止。
- 此版以大運 timing 的 `CALCULATED / DAYUN_SECOND_V2` 作為時間解析完成的條件；不把 `UNAVAILABLE` 的 legacy fallback 當完整成功。大運週期、神煞、五行強弱及喜用神**未做本地重算或語意驗證**；原始資料保存不代表這些內容已獲產品採用。
- 單次作業設 45 秒取消期限；可按「取消排盤」，收尾會釋放程序並保存狀態。失敗後手動重試另開新 run，不無限重試。
- 程式被強制終止時，可能留下 running 紀錄；重新開啟會提示上次未完成，不能視為成功。本輪未實作自動續跑或修復中斷 run。
- 全部測試使用虛構資料與暫存 DB，未操作桌面。涵蓋林姓真實 MCP 呼叫、紐約 DST、台灣 1979 DST、近午夜真太陽時跨日、缺漏／矛盾回傳、取消、版本競爭與重新讀取。數量與入口統一見 [測試 README](../tests/README.md)。

實作入口：[Step2Service](../src/OnlyFate.Database/Step2Service.cs)、[BirthTimeResolver](../src/OnlyFate.Database/BirthTimeResolver.cs)、[ChartRunRepository](../src/OnlyFate.Database/ChartRunRepository.cs)、[桌面排盤介面](../src/OnlyFate.Desktop/MainWindow.Chart.cs)。
