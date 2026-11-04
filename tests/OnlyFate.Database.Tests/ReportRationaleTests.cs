using System.Text.Json;
using OnlyFate.Database;
using Xunit;
namespace OnlyFate.Database.Tests;

public sealed class ReportRationaleTests
{
 [Theory][InlineData("source")][InlineData("elements")][InlineData("evidence")]
 public void BasisRejectsWrongSourceOrChangedDirectionOrFact(string mutation)
 {
  string root=Path.Combine(Path.GetTempPath(),"OnlyFate.Rationale",Guid.NewGuid().ToString("N"));
  try {
   var demo=new DemoWorkspace(root);var result=demo.CreateNew();long rev=result.Case.CaseRevisionID;
   var naming=new Step4Service(demo.DatabasePath).Latest(rev)!;
   var analysis=new Step3Service(demo.DatabasePath).Latest(rev)!;
   using var fixture=JsonDocument.Parse(typeof(ReportRationaleTests).Assembly.GetManifestResourceStream("Step2Fixture")!);
   var chart=fixture.RootElement.GetProperty("response").GetProperty("structuredContent").GetProperty("data").GetProperty("chart");
   if(mutation=="source")analysis=analysis with{ProcessingRunID=analysis.ProcessingRunID+100};
   else {
    var node=System.Text.Json.Nodes.JsonNode.Parse(analysis.Chart!.Value.GetRawText())!;
    if(mutation=="elements")node["analysis"]!["namingElements"]=new System.Text.Json.Nodes.JsonArray("水");
    else node["analysis"]!["evidence"]![0]!["factValue"]="子";
    analysis=analysis with{Chart=JsonSerializer.SerializeToElement(node)};
   }
   Assert.Throws<InvalidOperationException>(()=>ReportNamingBasis.Build(result.Case,chart,naming,analysis));
  }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
 }
 [Fact] public void DemoPreservesBirthReasonAndExplainsConfigurationAndEachCharacter()
 {
  string root=Path.Combine(Path.GetTempPath(),"OnlyFate.Rationale",Guid.NewGuid().ToString("N"));
  try {
   var demo=new DemoWorkspace(root);var result=demo.CreateNew();var data=result.Report.Data;
   var basis=data.GetProperty("namingBasis");
   Assert.Equal(data.GetProperty("source").GetProperty("sourceAnalysisRunId").GetInt64(),basis.GetProperty("sourceAnalysisRunId").GetInt64());
   Assert.True(basis.GetProperty("evidence").GetArrayLength()>=3);
   Assert.Contains("尚未",string.Join("",basis.GetProperty("limitations").EnumerateArray().Select(x=>x.GetString())));
   string html=ReportHtmlRenderer.Render(data);
   foreach(string text in new[]{"寶寶的取名方向","土、金","這組怎麼選字","本義","放進名字"})
    Assert.Contains(System.Net.WebUtility.HtmlEncode(text),html);
   foreach(string text in new[]{"用來檢查什麼","本組怎麼算","8 + 9 = 17","入選條件","不能只數五行個數","相鄰不相剋","三才不是直接補八字","為什麼採用這組配置"})
    Assert.DoesNotContain(System.Net.WebUtility.HtmlEncode(text),html);
   Assert.DoesNotContain("/pillars/",html);
   Assert.False(data.GetProperty("formalDeliverable").GetBoolean());
   string print=html[html.IndexOf("<div class=\"report-content\">",StringComparison.Ordinal)..];
   int analysisStart=print.IndexOf("<section class=\"analysis-page print-page\"",StringComparison.Ordinal);
   Assert.True(analysisStart>0);
   string analysisPage=print[analysisStart..print.IndexOf("</section>",analysisStart,StringComparison.Ordinal)];
   foreach(string text in new[]{"八字如何連到取名","日主","午月","庚申","壬","土、金"})Assert.Contains(text,analysisPage);
   Assert.True(analysisStart<print.IndexOf("class=\"proposal-page print-page\"",StringComparison.Ordinal));
   Assert.Equal(2,print.Split("class=\"proposal-page print-page\"").Length-1);
   Assert.True(print.IndexOf("這份報告怎麼看",StringComparison.Ordinal)<print.IndexOf("寶寶的取名方向",StringComparison.Ordinal));
   Assert.Contains("庚申",print);Assert.Contains("丙午",print);
   Assert.Equal(2,print.Split("class=\"comparison-page print-page\"").Length-1);
   int groupIndex=0;
   foreach(var g in data.GetProperty("groups").EnumerateArray()) {
    int start=print.IndexOf("data-group=\""+(++groupIndex)+"\"",StringComparison.Ordinal);
    int end=print.IndexOf("</section>",start,StringComparison.Ordinal);
    string page=print[start..end];
    foreach(string pos in new[]{"firstCharacters","lastCharacters"})foreach(var ch in g.GetProperty(pos).EnumerateArray())
     foreach(string field in new[]{"literalMeaning","namingWish","style","caution"})Assert.Contains(System.Net.WebUtility.HtmlEncode(ch.GetProperty("profile").GetProperty(field).GetString()!),page);
    Assert.Contains(g.GetProperty("recommendation").GetProperty("candidate").GetProperty("name").GetString()!,page);
    Assert.DoesNotContain(g.GetProperty("direction").GetString()!,page);
    Assert.DoesNotContain(g.GetProperty("recommendation").GetProperty("reason").GetString()!,page);
   }
   // Cross-fact explanations must not be applied when one of their cited conditions is absent.
   foreach(var(path,title)in new[]{("/pillars/day/ganZhi","再看季節：夏季的火較旺"),("/pillars/month/branch","命盤中的水，也影響取名方向")}){
    var modified=System.Text.Json.Nodes.JsonNode.Parse(data.GetRawText())!;
    var evidence=modified["namingBasis"]!["evidence"]!.AsArray();
    for(int i=evidence.Count-1;i>=0;i--)if(evidence[i]!["factPath"]!.GetValue<string>()==path)evidence.RemoveAt(i);
    string adjusted=ReportHtmlRenderer.Render(JsonSerializer.SerializeToElement(modified));
    Assert.DoesNotContain(title,adjusted);
   }
  }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
 }
}
