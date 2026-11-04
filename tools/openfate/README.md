# OpenFate 本機 MCP runtime

供 C# 桌面 Step 2 透過 stdio 啟動；不是 OpenAI API，也不需要啟動 HTTP server。實際互動與資料保存見 [Step 2](../../docs/step-2.md)。

## 安裝與還原

需要 PATH 中的 **Node.js 20 以上**及 npm。此次實測 Node.js 24.15.0／npm 11.12.1；目前程式未另做 Node 主版本預檢，環境不合可能回報 MCP 啟動失敗。

在 repository 根目錄執行：

```powershell
npm ci --prefix tools/openfate --ignore-scripts --no-audit --no-fund
```

package.json 固定 `@openfate/bazi-mcp` 0.3.1；傳遞依賴實際版本與 integrity 由 package-lock.json 管理。此 npm 範圍是產品 runtime 依賴，不是為測試引入前端工具。node_modules 不納入 Git，不安裝到 npm global，也不在排盤中自動下載／更新。

桌面建置會把此範圍的 node_modules（含授權檔）複製到輸出的 `OpenFate/node_modules`。Node.js 執行檔尚未隨附，沒有安裝包；在沒有 Node.js 的電腦不能直接當成免安裝產品。開發測試可回溯 repository 的 tools/openfate 路徑。

## 來源及授權

- [OpenFate MCP](https://github.com/openfate-ai/openfate-mcp)：MIT；只採用本機公開套件的工具能力，不推論其包含取名或報告功能。
- `@openfate/bazi-engine` 2.0.0 的 LICENSE 隨 node_modules 保留；`@openfate/true-solar-time` 4.0.2 的 package.json／README 宣告 MIT，但 npm 包未附 LICENSE，正式散佈前須補齊上游授權通知。完整依賴版本及 license metadata 見鎖檔。沒有採用 OpenFate 線上付費報告或抓取網站文案。
- Windows stdio 的程序管理由 [官方 C# MCP SDK](https://github.com/modelcontextprotocol/csharp-sdk) 處理；本輪測試不開啟 console 視窗、不操作桌面。

修改 runtime 版本後必須重新驗證政策、參數、回傳格式及時區一致性，不能只修改文件中的版本號。
