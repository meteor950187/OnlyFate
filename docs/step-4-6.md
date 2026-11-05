# Step 4–6：兩組配字與報告

本輪改版依據見 [報告與字池改版](report-redesign.md)。命理方法、外部民俗五行分類仍是試行，正式資格固定false；程式通過不能改稱命理品質已驗證。

## Step 4：完整字池，再選兩組

使用 [字庫v2](../resources/naming/README.md)、案件限制與Step3的namingElements，先計算完整合法姓名，再搜尋每組兩個位置各4–5字的字池。每個位置筆畫相同，組內8–10字不重複，每一個交叉配對都必須已通過原規則。

- 單字姓＋雙字名；五格公式、吉數清單與相鄰三才不相剋不變，天格不另過濾。
- 避諱同字／國語同音同調、指定字及位置、全名重字／同音、元素範圍都作用於每一個配對。
- 四字池相乘有16種，五字池相乘有25種；不能只驗證主推與四個示例。
- 至少存在兩組、跨組字交集最多2字；否則在呼叫模型前停止，說明字庫或限制不足。必用字可能無法與自由配字規格同時成立，不會自行取消必用條件。
- 搜尋使用當前有限字庫的完整合法配對，不用任意截取前幾名冒充完整字池。

AI只選兩個poolId，每組提供label（24字內）、direction（100字內）、一個recommendation及四個alternatives；每個示例只引用candidateId與80字內理由。兩組共十個示例不重複，理由不能編數字或人生保證；備註引用仍需對回原文。

程式重新從字庫與限制生成合法候選，核對傳入字池的逐字資料和全部配對。兩組可使用不同配置，不能只改標題或交換同套字。報告標示只可在同組內跨兩字池搭配；跨組未經驗證不能混用。

`Prepare`保留完整來源快照；`NamingContract.ModelContext`才是模型用投影：逐字語義只傳一次，候選只傳ID／姓名／兩個字，字池傳字列表／完整pairIds／筆畫。研究連結、sourceNote、elementRationale不反覆送給模型；不傳出生日期、地址或整份Step3證據。

規則版本`naming-pools-trial-v2`、輸出`naming-output-v2`。正常一次呼叫，格式／候選／池不符最多一次repair，兩次共用120秒；HTTP、拒答與取消不自動重送。詳見 [AI呼叫流程](ai-call-flow.md)。

## Step 5：十個示例的短文與固定逐字說明

`ReportContract`收到兩組各五個已選示例的字義、字庫語義profile、選名理由與applied備註；不傳完整備註、生日、地址、命盤或數理。AI為每個示例寫wish／reason（各80字內），summary（120字內）；全部組別與ID須對應，不能遺漏或增造。

字的讀音、本義、寓意、風格、注意事項來自研究字庫的固定profile，不讓AI重寫字典事實。字義與命名祝願分開，逐字來源留於報告資料與系統紀錄。保守內容檢查不等於任意語義真實性的證明。

`ReportDataAssembler`輸出`report-data-v2`、`parent-report-pools-v12`版型。螢幕與PDF共用內容，呈現規格見[報告編排](report-print-layout.md)。每個groups項目含：

- label／direction。
- firstCharacters／lastCharacters完整逐字資料。
- recommendation及四個alternatives，各含固定candidate＋短文。
- combinations完整合法配對及configuration實算筆畫／五格／三才。

頂層保存來源run IDs、版本、出生摘要、四柱、coverage／warnings及試行身份。同一交易內核對案件和Step2–4來源、完成Step5與NamingReport；UTF-8 SHA-256固定報告內容。Step5上限6000 output tokens，正常一次、至多一次repair。

舊v1選名／報告不當成新版已完成；重新開啟顯示過期，需要重選、重寫。舊資料與原始產物保留，不回寫migration001或歷史內容。

## Step 6：同資料的螢幕與列印版型

HTML採響應式卡片，PDF採A4章節分頁；兩者同一固定資料，版型可以依screen／print調整。閱讀順序是兩組主推總覽、各組配字與替代示例、逐字說明；頁數依內容，不能為了兩頁壓縮可讀性。

所有動態文字HTML編碼，自包含CSS、無外部載入或JavaScript；WebView2只接受當前報告URI。PDF由同一份經hash核對的HTML列印，HTML與PDF各有獨立ready收據。

產物存受控root，固定ID／GUID檔名，拒絕路徑逃逸與reparse point。寫入後檢查大小和SHA-256，交易內重查案件及Step2–5來源；成功HTML不代表PDF成功，失敗／取消可重新產生尚未完成的格式。

已完成檔案不覆寫。若內部檔遺失／遭變更，`CreateArtifactCopy`驗證固定報告及來源後，在同一Step5 run追加NamingReport版本，內容位元組不變、無新模型呼叫，原記錄仍保留；新副本可產生新HTML／PDF。`Step5Service.Latest`取該run最新報告版本，不覆寫歷史ProcessingStep結果。報告資料本身hash不符或來源已過期時不能用此方式恢復。

程式儲存層只檢查PDF標頭與非空；完整頁面需實際WebView列印及逐頁視覺驗收，最新結果以 [驗收紀錄](acceptance.md) 為準。

## 實作入口

[NamingEngine](../src/OnlyFate.Database/NamingEngine.cs)、[NamingPools](../src/OnlyFate.Database/NamingPools.cs)、[NamingContract](../src/OnlyFate.Database/NamingContract.cs)、[Step4Service](../src/OnlyFate.Database/Step4Service.cs)、[ReportContract](../src/OnlyFate.Database/ReportContract.cs)、[Step5Service](../src/OnlyFate.Database/Step5Service.cs)、[ReportRepository](../src/OnlyFate.Database/ReportRepository.cs)、[Renderer](../src/OnlyFate.Database/ReportHtmlRenderer.cs)、[Artifacts](../src/OnlyFate.Database/ReportArtifactService.cs)。

API格式依據：[OpenAI Structured Outputs](https://developers.openai.com/api/docs/guides/structured-outputs?api-mode=responses)、[Claude Structured Outputs](https://platform.claude.com/docs/en/build-with-claude/structured-outputs)。本機內容與列印依據：[WebView2本機內容](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/working-with-local-content)、[列印API](https://learn.microsoft.com/en-us/microsoft-edge/webview2/how-to/print)。

報告現在保存 `namingBasis`：取自選名所引用的同一個 Step 3 run，組裝前核對案件版本、命盤來源、引用事實及選字五行一致。白話摘要、方向、依據與適用限制沿用該判讀，不由 Step 5 事後編造。家長報告先說白話取名方向，再逐組提供配字及逐字說明；模型呼叫數與 Step 5 context 不變。版型已升版，歷史產物保留。

家長呈現以 [報告閱讀定位](report-rationale.md) 最新修正為準；詳細evidence/計算資料仍保存，但不把檢查流程、算式與開發用限制逐條印在家長報告。

PDF印刷分頁依 [配字比較頁](report-print-layout.md)：第1頁閱讀與取名方向、第2頁推薦前八字分析；每組先一頁說明主推及選字理由，再一頁把完整字池及字義放在一起；完整ReportData供screen與print兩種編排，不新增模型呼叫。
