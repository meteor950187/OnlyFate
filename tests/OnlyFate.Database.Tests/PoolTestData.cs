using System.Text.Json;
using OnlyFate.Database;
namespace OnlyFate.Database.Tests;
internal static class PoolTestData {
 internal static JsonElement Context(CaseInput? input=null) {input??=PoolCoreTests.Input;var source=NamingEngine.Generate(input,["土","金"]);return Step2Json.Element(new{caseData=input,analysis=new{namingElements=new[]{"土","金"}},pools=NamingPools.Generate(source.Candidates),candidates=source.Candidates,remarksRaw=input.Remarks});}
 internal static JsonElement Selection(JsonElement context,bool valid=true) {
  var pools=context.GetProperty("pools").EnumerateArray().ToArray();
  IEnumerable<string> Characters(JsonElement p)=>p.GetProperty("firstCharacters").EnumerateArray().Concat(p.GetProperty("lastCharacters").EnumerateArray()).Select(c=>c.ValueKind==JsonValueKind.String?c.GetString()!:c.GetProperty("character").GetString()!);
  bool Compatible(JsonElement x,JsonElement y)=>x.GetProperty("poolId").GetString()!=y.GetProperty("poolId").GetString()&&Characters(x).Intersect(Characters(y)).Count()<=2;
  var a=pools.First(x=>pools.Any(y=>Compatible(x,y)));var b=pools.First(y=>Compatible(a,y));
  return Step2Json.Element(new{groups=new[]{a,b}.Select((p,i)=>new{poolId=p.GetProperty("poolId").GetString(),label=i==0?"從容堅毅":"溫和明朗",direction=i==0?"重視安定與承擔的祝願。":"以清新開闊的字義寄託成長。",recommendation=new{candidateId=valid?p.GetProperty("pairIds")[0].GetString()!:"invented",reason="字義自然，寄託安定成長。"},alternatives=p.GetProperty("pairIds").EnumerateArray().Skip(1).Take(4).Select(x=>x.GetString()!).Select(id=>new{candidateId=id,reason="讀音清楚，搭配有自己的特色。"})}),remarks=context.GetProperty("remarksRaw").GetString() is {Length:>0} raw?new[]{new{quote=raw,disposition="applied",reason="採用字義與風格偏好。"}}:Array.Empty<object>()});
 }
 internal static JsonElement Prose(JsonElement context) =>Step2Json.Element(new{groups=context.GetProperty("groups").EnumerateArray().Select(g=>new{poolId=g.GetProperty("poolId").GetString(),entries=g.GetProperty("entries").EnumerateArray().Select((e,i)=>new{candidateId=e.GetProperty("candidateId").GetString(),wish=i%2==0?"從容向前，珍惜身邊的人。":"心懷開闊，認真做好喜愛的事。",reason=i%2==0?"以字義寄託沉穩而有力量的祝願。":"搭配自然，帶出溫和明朗的氣息。"})}),summary="兩組各有風格，請依讀音與字義偏好選擇。"});
}
