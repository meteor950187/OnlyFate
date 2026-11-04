using System.Net;
using System.Text;
using System.Text.Json;
namespace OnlyFate.Database;

/// <summary>Pure presentation of the immutable, validated report data. No remote resources.</summary>
public static partial class ReportHtmlRenderer
{
    private static string E(object? value) => WebUtility.HtmlEncode(value?.ToString() ?? "");
    private static string S(JsonElement value, string key) => value.GetProperty(key).ToString();
    public static string Render(JsonElement data)
    {
        if (S(data.GetProperty("versions"), "templateVersion") != ReportDataAssembler.TemplateVersion || S(data,"schemaVersion")!="report-data-v2")
            throw new InvalidOperationException("不支援的固定報告版型版本。");
        var b = new StringBuilder("""
<!doctype html><html lang="zh-Hant-TW"><head><meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'"><meta name="viewport" content="width=device-width, initial-scale=1"><title>寶寶命名報告（試行）</title><style>
@page{size:A4;margin:17mm 17mm 20mm;@bottom-center{content:"OnlyFate 試行報告 · " counter(page) " / " counter(pages);font-family:"Noto Sans TC","Microsoft JhengHei",sans-serif;font-size:9pt;color:#475e56}}
*{box-sizing:border-box}body{margin:0;background:#e9e9e2;color:#173F36;font-family:"Noto Sans TC","Microsoft JhengHei",sans-serif;font-size:11pt;line-height:1.8}main{max-width:1040px;margin:32px auto;background:#FCFAF6;padding:30px 42px}h1,h2,h3,.name,.glyph{font-family:"Noto Serif TC","PMingLiU",serif;font-weight:600}h1{font-size:26pt;line-height:1.4;margin:10px 0}h2{font-size:24pt;line-height:1.5;margin:8px 0 20px}h3{font-size:16pt;margin:0 0 10px}p{margin:8px 0;overflow-wrap:anywhere}h1,h2,h3{break-after:avoid}section{margin-bottom:32px}.eyebrow{font-size:10pt;letter-spacing:.16em;color:#735a35;font-weight:600}
@media print{body{background:white;font-size:11pt;line-height:1.65}main{max-width:none;margin:0;padding:0;background:white}section{margin-bottom:20px}p{orphans:3;widows:3}}
""");
        b.Append(ScreenLayoutCss).Append(PrintLayoutCss).Append("</style></head><body><main>");
        ReportSections(b,data);
        return b.Append("</main></body></html>").ToString();
    }
    private static string Readings(JsonElement ch)=>E(string.Join("／",ch.GetProperty("readings").EnumerateArray().Select(x=>x.GetString())));
}
