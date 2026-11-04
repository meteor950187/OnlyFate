# Step 3 實作計畫與驗證

2026-11-03：實作已討論的呼叫流程。規則尚未完成商用品質驗證，使用明確版本的試行規則，所有結果不可直接進入正式選名。

1. AnalysisContract：固定指令、試行規則、JSON schema、欄位與證據驗證。先測試不合法引用與原文不符會被拒絕。
2. OpenAiResponsesClient：HttpClient 呼叫官方 Responses endpoint；不自動重試、key 僅放 header，測試使用假 HTTP handler。
3. Step3Service：固定案件及成功命盤版本、最多一次修正、SQLite 記錄、取消及版本保護。重用既有交易紀錄工具，schema v1 不變。
4. WPF：模型與 session-only PasswordBox，context 預覽、試行判讀、取消與重新開啟結果。測試不顯示視窗。
5. 更新文件並執行隔離輸出建置、測試。未有指定模型及可用憑證時不宣稱真實 OpenAI 品質驗證。
