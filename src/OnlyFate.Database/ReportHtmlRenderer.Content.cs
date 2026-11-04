using System.Text;
using System.Text.Json;
namespace OnlyFate.Database;

public static partial class ReportHtmlRenderer
{
 private static void BirthAnalysisPage(StringBuilder b,JsonElement data)
 {
  var basis=data.GetProperty("namingBasis");
  string elements=string.Join("、",basis.GetProperty("namingElements").EnumerateArray().Select(x=>x.GetString()));
  b.Append("<section class=\"analysis-page print-page\" id=\"birth-analysis\"><div class=\"eyebrow\">認識寶寶的命盤</div><h2>八字如何連到取名</h2><div class=\"pillar-strip\">");
  foreach(var(key,label)in new[]{("year","年柱"),("month","月柱"),("day","日柱"),("hour","時柱")})b.Append("<div><span>").Append(label).Append("</span><strong>").Append(E(S(data.GetProperty("pillars").GetProperty(key),"ganZhi"))).Append("</strong></div>");
  b.Append("</div>");
  // These short explanations are editorial paraphrases of the existing case research.
  // Only show one when this analysis actually cites the corresponding fact and value.
  var notes=new[]{
   (Facts:new[]{(Path:"/pillars/day/ganZhi",Value:"庚申")},Title:"先認識命盤的主角：庚金",Text:"八字以出生那一天的天干作為判讀中心，稱為「日主」。寶寶的日柱是庚申，日主就是庚金；其中的「申」也帶有金，可以理解為已有同類支援。這是本次評估取名方向的起點。"),
   (Facts:new[]{(Path:"/pillars/month/branch",Value:"午"),(Path:"/pillars/day/ganZhi",Value:"庚申")},Title:"再看季節：夏季的火較旺",Text:"午月正值夏季，命理上視為火旺的時節。火對金有制約作用，因此這次會考量庚金是否需要更多支持；判斷時，也會一起看前面提到的原有支援。"),
   (Facts:new[]{(Path:"/pillars/hour/stem",Value:"壬"),(Path:"/pillars/month/branch",Value:"午")},Title:"命盤中的水，也影響取名方向",Text:"時柱的「壬」屬水，命理上會考量水對夏季燥熱的調和。命盤已有水，仍須看它能發揮多少作用；這個條件也會影響最後採用的取名方向，因此本次仍保留確認空間。")};
  foreach(var note in notes){
   bool cited=note.Facts.All(fact=>basis.GetProperty("evidence").EnumerateArray().Any(x=>S(x,"factPath")==fact.Path&&S(x,"factValue")==fact.Value));
   if(!cited)continue;
   b.Append("<article class=\"analysis-note\"><h3>").Append(note.Title).Append("</h3><p>").Append(note.Text).Append("</p></article>");
  }
  b.Append("<div class=\"analysis-conclusion\"><h3>本次選字方向：").Append(E(elements)).Append("</h3><p>").Append(E(S(basis,"direction"))).Append("</p>");
  if(basis.GetProperty("namingElements").EnumerateArray().Select(x=>x.GetString()).ToHashSet().SetEquals(new[]{"土","金"})
   &&S(basis,"direction").Contains("土生金",StringComparison.Ordinal)&&S(basis,"direction").Contains("金助金",StringComparison.Ordinal))
   b.Append("<p>這裡的意思是：用土作為金的支持，再以金提供同類的幫助。</p>");
  b.Append("</div>");
  foreach(var item in basis.GetProperty("limitations").EnumerateArray())b.Append("<p class=\"analysis-limit\">").Append(E(item.GetString())).Append("</p>");
  b.Append("</section>");
 }
 private static void ProposalPage(StringBuilder b,JsonElement g,int index)
 {
  var rec=g.GetProperty("recommendation");var candidate=rec.GetProperty("candidate");
  string PoolElements(string key)=>string.Join("、",g.GetProperty(key).EnumerateArray().Select(x=>S(x,"trialElement")).Distinct());
  b.Append("<section class=\"proposal-page print-page\" id=\"scheme-").Append(index).Append("\" data-proposal=\"").Append(index).Append("\"><div class=\"eyebrow\">方案 ").Append(index).Append(" · 認識這組名字</div><h2>").Append(E(S(g,"label"))).Append("</h2><p class=\"proposal-direction\">").Append(E(S(g,"direction"))).Append("</p>");
  b.Append("<div class=\"compare-primary\"><div><div class=\"example-meta\">本組主推</div><div class=\"example-name\">").Append(E(S(candidate,"name"))).Append("</div></div><div>");
  foreach(string part in new[]{"first","last"}){
   var ch=candidate.GetProperty(part);
   b.Append("<p><strong>「").Append(E(S(ch,"character"))).Append("」</strong>寄託").Append(E(S(ch.GetProperty("profile"),"namingWish"))).Append("。</p>");
  }
  b.Append("<p class=\"primary-summary\">").Append(E(S(rec,"reason"))).Append("</p></div></div>");
  b.Append("<h3>這組怎麼選字</h3><p class=\"proposal-basis\">以").Append(E(PoolElements("firstCharacters"))).Append("類字在前、").Append(E(PoolElements("lastCharacters"))).Append("類字在後，延續本次八字的取名方向。</p><div class=\"proposal-pools\">");
  foreach(var(key,label,part)in new[]{("firstCharacters","名字第一字","first"),("lastCharacters","名字第二字","last")}){
   var pool=g.GetProperty(key).EnumerateArray().ToArray();
   string glyphs=string.Join("、",pool.Select(x=>S(x,"character")));
   var others=pool.Where(x=>S(x,"character")!=S(candidate.GetProperty(part),"character")).ToArray();
   b.Append("<article><h3>").Append(label).Append(" <span class=\"pool-element\">· ").Append(E(PoolElements(key))).Append("類</span></h3><p class=\"pool-letterline\">").Append(E(glyphs)).Append("</p><p>");
   var examples=others.Where((_,i)=>i==0||i==others.Length-1).ToArray();
   for(int i=0;i<examples.Length;i++){
    var ch=examples[i];
    if(i>0)b.Append("；");
    b.Append("「").Append(E(S(ch,"character"))).Append("」寄託").Append(E(S(ch.GetProperty("profile"),"namingWish")));
   }
   b.Append("。</p></article>");
  }
  b.Append("</div><h3>也可以這樣搭配</h3><div class=\"comparison-examples\"><div class=\"compare-alternatives\">");
  foreach(var e in g.GetProperty("alternatives").EnumerateArray()){
   var alternative=e.GetProperty("candidate");
   b.Append("<article class=\"compare-example\"><div class=\"example-name\">").Append(E(S(alternative,"name"))).Append("</div><p class=\"pair-tone\">");
   foreach(string part in new[]{"first","last"}){
    var ch=alternative.GetProperty(part);
    if(part=="last")b.Append(" × ");
    b.Append(E(S(ch,"character"))).Append("・").Append(E(S(ch.GetProperty("profile"),"style")));
   }
   b.Append("</p><p>").Append(E(S(e,"reason"))).Append("</p></article>");
  }
  b.Append("</div></div><p class=\"next-page-hint\"><span class=\"print-only\">想換個字？下一頁可看完整字義，自由搭配。</span><a class=\"screen-only\" href=\"#characters-").Append(index).Append("\">看完整字義，挑選自己的搭配 ↓</a></p></section>");
 }

 private static void ReportSections(StringBuilder b,JsonElement data)
 {
  var groups=data.GetProperty("groups").EnumerateArray().ToArray();var c=data.GetProperty("caseData");
  b.Append("<div class=\"report-content\"><section class=\"print-page print-cover\"><p class=\"print-status\">");
  if(S(data,"sourceKind")=="offline-fictional-demo")b.Append("離線虛構示範｜");
  b.Append("試行提案・取名方向尚待確認</p><div class=\"eyebrow\">一份名字，一份心意</div><h1>").Append(E(S(c,"surname"))).Append("姓寶寶・命名提案</h1>");
  b.Append("<div class=\"reading-guide\"><h3>這份報告怎麼看</h3><div class=\"reading-steps\"><div><strong>① 看取名方向</strong><span class=\"print-only\">第 2 頁 · 八字分析</span><a class=\"screen-only\" href=\"#birth-analysis\">八字分析</a></div><div><strong>② 比較兩組風格</strong><span class=\"print-only\">第 3、5 頁 · 主推姓名</span><span class=\"screen-only\"><a href=\"#scheme-1\">方案一</a> ／ <a href=\"#scheme-2\">方案二</a></span></div><div><strong>③ 挑喜歡的字</strong><span class=\"print-only\">第 4、6 頁 · 同頁搭配</span><span class=\"screen-only\"><a href=\"#characters-1\">方案一挑字</a> ／ <a href=\"#characters-2\">方案二挑字</a></span></div></div></div>");
  b.Append("<div class=\"case-intro\"><h3>寶寶的出生資料</h3><p>姓氏：").Append(E(S(c,"surname"))).Append(" · 國曆 ").Append(E(S(c,"birthDate"))).Append(' ').Append(E(S(c,"birthHour"))).Append(':').Append(E(S(c,"birthMinute"))).Append(" · ").Append(E(S(c,"birthCity"))).Append("</p></div>");
  var basis=data.GetProperty("namingBasis");
  string elements=string.Join("、",basis.GetProperty("namingElements").EnumerateArray().Select(x=>x.GetString()));
  b.Append("<div class=\"cover-direction\"><h2>寶寶的取名方向</h2><p>本次以<strong>").Append(E(elements)).Append("</strong>類用字為方向，再搭配字義、讀音與筆畫配置，提供兩組風格。</p></div><div class=\"cover-schemes\">");
  for(int i=0;i<groups.Length;i++)b.Append("<article><div class=\"eyebrow\">方案 ").Append(i+1).Append("</div><h3>").Append(E(S(groups[i],"label"))).Append("</h3><p class=\"print-only\">介紹與主推 · 第 ").Append(3+i*2).Append(" 頁<br>逐字說明與搭配 · 第 ").Append(4+i*2).Append(" 頁</p><p class=\"screen-only\"><a href=\"#scheme-").Append(i+1).Append("\">看介紹與主推</a> · <a href=\"#characters-").Append(i+1).Append("\">逐字說明與搭配</a></p></article>");
  b.Append("</div></section>");
  BirthAnalysisPage(b,data);
  for(int i=0;i<groups.Length;i++){
   var g=groups[i];var rec=g.GetProperty("recommendation");var candidate=rec.GetProperty("candidate");
   ProposalPage(b,g,i+1);
   var first=g.GetProperty("firstCharacters").EnumerateArray().ToArray();var last=g.GetProperty("lastCharacters").EnumerateArray().ToArray();
   b.Append("<section class=\"comparison-page print-page\" id=\"characters-").Append(i+1).Append("\" data-group=\"").Append(i+1).Append("\"><header class=\"comparison-heading\"><div><div class=\"eyebrow\">方案 ").Append(i+1).Append(" · 配字比較</div><h2>").Append(E(S(g,"label"))).Append("</h2></div><aside class=\"comparison-pick\"><span>本組主推</span><strong>").Append(E(S(candidate,"name"))).Append("</strong></aside></header><p class=\"comparison-guide print-only\">左、右各挑一字，接在「").Append(E(S(c,"surname"))).Append("」後面，就是完整姓名。本組共有 ").Append(first.Length*last.Length).Append(" 種搭配，不限於前頁示例。</p><p class=\"comparison-guide screen-only\">從第一字、第二字各挑一字，接在「").Append(E(S(c,"surname"))).Append("」後面，就是完整姓名。本組共有 ").Append(first.Length*last.Length).Append(" 種搭配，不限於上方示例。</p><div class=\"comparison-columns\"><div>① 名字第一字</div><div>② 名字第二字</div></div>");
   for(int r=0;r<Math.Max(first.Length,last.Length);r++){
    b.Append("<div class=\"comparison-row\">");
    Character(r<first.Length?first[r]:null,"first",candidate.GetProperty("first"));
    Character(r<last.Length?last[r]:null,"last",candidate.GetProperty("last"));
    b.Append("</div>");
   }
   b.Append("<p class=\"compare-note print-only\">左、右各挑一字，可跨列搭配。選定前，和家人一起讀讀看。</p><p class=\"compare-note screen-only\">請在本組內，從兩種位置各選一字自由搭配。選定前，和家人一起讀讀看。</p></section>");
  }
  b.Append("</div>");
  void Character(JsonElement? ch,string position,JsonElement recommended){
   if(ch is null){b.Append("<div></div>");return;}var x=ch.Value;
   b.Append("<article class=\"compare-character ").Append(position).Append("\"><div class=\"character-position screen-only\">").Append(position=="first"?"① 名字第一字":"② 名字第二字").Append("</div><div class=\"compare-heading\"><strong class=\"compare-glyph\">").Append(E(S(x,"character"))).Append("</strong><span class=\"compare-reading\">").Append(Readings(x)).Append("</span><span class=\"compare-style\">").Append(E(S(x.GetProperty("profile"),"style"))).Append("</span>");
   if(S(x,"character")==S(recommended,"character"))b.Append("<span class=\"pick-tag\">主推用字</span>");
   b.Append("</div>");
   foreach(var(field,label)in new[]{("literalMeaning","本義"),("namingWish","放進名字"),("caution","留意")})b.Append("<div class=\"compare-fact\"><strong>").Append(label).Append("</strong><span>").Append(E(S(x.GetProperty("profile"),field))).Append("</span></div>");
   b.Append("</article>");
  }
 }
}
