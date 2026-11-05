# Context 與 AI 呼叫流程

這裡說明應用程式實際如何與 AI 互動。各步的欄位及錯誤條件以 [Step 3](step-3.md) 與 [後續流程](step-4-6.md) 為準。

## 正常一個案件會呼叫幾次？

**三次模型呼叫：判讀、選名、短文撰寫。** Step 1 沒有 AI 備註整理；Step 2 是程式呼叫 OpenFate MCP；Step 6 是渲染與 PDF 列印。

| 階段 | 程式準備的資料 | AI 負責 | 程式接手 |
|---|---|---|---|
| Step 3 判讀 | 本版案件條件、已驗證命盤、固定試行規則、備註原文 | 有依據的判讀摘要、選字方向、備註取捨與阻擋理由 | 檢查 schema、命盤引用及規則 ID、備註引用、方向與阻擋狀態 |
| Step 4 選名 | 案件限制、Step 3 的 namingElements、完整合法字池、去重的逐字資料、輕量姓名候選、備註原文 | 選兩組，每組一個主推與四個替代示例，說明不同方向 | 重新核對字庫與所有交叉配對；兩組不能只是同一套字換序，示例不得重複 |
| Step 5 短文 | 兩組共十個示例的字義、選名理由與已採用偏好 | 為每個示例寫祝願與簡短特色 | 驗證組別、ID、長度與內容限制；逐字說明、姓名、生日、筆畫、命盤由固定資料放入報告 |

模型不能直接寫 SQLite、改案件欄位或執行 MCP。流程控制、資料讀寫、重試與停止是 C# 應用程式的責任；這一層才是本專案的 agent harness。

## Context 具體是什麼？

每次由各階段 `Contract.Request` 準備一份與廠商無關的 `ModelRequest`：

1. `Instructions`：該階段任務、固定規則與資料優先順序。
2. `UserContent`：一則 user 資料訊息，內含 `context`；內容由 SQLite 的固定案件版本及前步有效結果組成。
3. `Schema`：嚴格 JSON schema，與 `SchemaName`。
4. `Model` 與輸出 token 上限。

再由所選服務的 adapter 轉成實際 request（見下節）。

**第一次就是 Step 3。** 沒有預先存在的應用程式 AI 對話，也不會送入你和 Codex 的開發聊天。Step 4、5 使用各自整理的必要資料；不使用 `previous_response_id`，也不把前面整段 request/response 一路累積。

服務內部的 `Prepare` 可以包含驗證用資料；真正送往模型服務的內容，以各階段 `Contract.Request` 的白名單為準。介面的「查看 request」顯示這份實際請求，不包含 API key。Step 4 不傳出生日期、地址或完整判讀引用；逐字資料只傳一次，不對每個候選重複送研究連結。Step 5 不傳整段備註、命盤或筆畫。使用者自由文字可能自行含個資，不宣稱白名單能自動匿名化所有文字。

## 模型服務 adapter

程式碼在 [ModelAdapters.cs](../src/OnlyFate.Database/ModelAdapters.cs) 與 [ModelTransports.cs](../src/OnlyFate.Database/ModelTransports.cs)。`ModelCallRunner` 只讀 adapter 整理好的結果（完成／拒答／未完成、輸出文字、上游 ID、實際模型、usage），不解析任何廠商的 JSON。

| | OpenAI | Claude（Anthropic） |
|---|---|---|
| Adapter／transport | `OpenAiResponsesAdapter`／`OpenAiResponsesClient` | `ClaudeMessagesAdapter`／`ClaudeMessagesClient` |
| Endpoint | `POST https://api.openai.com/v1/responses`，`Authorization: Bearer` | `POST https://api.anthropic.com/v1/messages`，`x-api-key` 與 `anthropic-version: 2026-10-21` |
| 指令與資料 | `instructions`＋`input[0]` | `system`＋`messages[0]` |
| 結構化輸出 | `text.format`（`json_schema`、`strict=true`、`name`），`store=false` | `output_config.format`（`json_schema`）；沒有 `store` 參數 |
| 輸出上限 | 契約值（4000／6000） | 至少 16000：新模型預設會思考，思考 token 計入 `max_tokens` |
| 完成判斷 | `status=completed` | `stop_reason=end_turn`；`refusal` 為拒答，`max_tokens` 等其他值為未完成 |
| `ExecutionCall.Provider／Operation` | `OpenAI`／`responses.create` | `Anthropic`／`messages.create` |
| 環境變數 | `OPENAI_MODEL`／`OPENAI_API_KEY` | `ANTHROPIC_MODEL`／`ANTHROPIC_API_KEY` |

Claude structured outputs 只支援陣列 `minItems` 0 或 1，不支援 `maxItems`。adapter 送出前移除這兩項，改寫成 `description`（例如「必須恰好 2 項」）提示模型；兩組方案、四個替代、五個示例、至多兩種五行等數量，原本就由各階段 C# 驗證強制檢查，不因此放寬。

兩家都只送一則 user 訊息、不提供 tools、不自動重試。Claude 沒有啟用 server-side fallback：拒答依本專案規則停止並保存紀錄，不換模型重答。模型 ID 由使用者輸入，程式沒有預設模型。

## 一次呼叫如何完成？

`檢查前置條件 → 固定 context → 保存呼叫開始紀錄 → 呼叫 API → 保存回應與 usage → 驗證 → 成功保存結果`

- 只有格式、引用或候選 ID 等可修正錯誤，才允許 **一次** 修正呼叫：同一份事實，加上前次輸出與錯誤清單。
- HTTP 失敗、拒答、截斷、逾時或取消不自動重送。
- 真正的命理分歧或沒有符合條件的字，不靠重新抽答案消除。
- 等待 API 時不鎖 SQLite 交易；成功寫回前，在交易中重新核對案件及前步版本。
- API key 只用於 HTTP header，不放進 context、紀錄或報告。缺 usage 就保留未知，不補造用量。

因此正常是三次，三個階段都發生一次可修正錯誤時最多六次；使用者手動重做是另一次執行並保留舊紀錄。

## 檢查的界線

程式能確認資料、引用、算術與明確限制是否一致，不能因為 JSON 合法就證明命理方法正確。目前判讀規則及字五行分類仍為試行；可走完整驗收流程，但報告必須標示試行，不能宣稱已達正式商用的命理品質。
