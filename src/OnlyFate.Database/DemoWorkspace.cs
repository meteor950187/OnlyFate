using System.Text.Json;
using System.Text.Json.Nodes;
namespace OnlyFate.Database;

public sealed record DemoCase(StoredCase Case,StoredReport Report);
public sealed class DemoWorkspace
{
 public string Root {get;}
 public string DatabasePath=>Path.Combine(Root,"Data","OnlyFate.db");
 public string ReportsPath=>Path.Combine(Root,"Reports");
 public string WebViewPath=>Path.Combine(Root,"WebView2");
 public static string DefaultRoot=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OnlyFate","Demo");
 public DemoWorkspace(string root)
 {
  Root=Path.GetFullPath(root);
  string normal=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(DatabaseSchema.DefaultDatabasePath)!,".."));
  if(Root.Equals(normal,StringComparison.OrdinalIgnoreCase) || normal.StartsWith(Root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)
   || Path.GetFullPath(DatabasePath).Equals(Path.GetFullPath(DatabaseSchema.DefaultDatabasePath),StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("示範目錄必須與一般資料隔離。");
 }
 private static JsonElement Mark(JsonElement input) {var node=JsonNode.Parse(input.GetRawText())!;node["policy"]!["sourceKind"]="offline-fictional-demo";return JsonSerializer.SerializeToElement(node);}
 public DemoCase Open()
 {
  var cases=new CaseRepository(DatabasePath);var list=cases.List();
  if(list.Count>0) {
   var saved=cases.Load(list.OrderByDescending(x=>x.NamingCaseID).First().NamingCaseID);
   var latest=new Step5Service(DatabasePath).Latest(saved.CaseRevisionID);
   if(latest?.Status!="completed"||latest.Chart is null) throw new InvalidOperationException("示範案件已變更或未完整建立；可按「建立新示範案例」繼續驗收，原紀錄保留。過期報告不會自動恢復。");
   return new(saved,new ReportRepository(DatabasePath).Load(latest.Chart.Value.GetProperty("reportId").GetInt64()));
  }
  return CreateNew();
 }
 public DemoCase CreateNew()
 {
  var cases=new CaseRepository(DatabasePath);
  var input=new CaseInput {Surname="林",Gender="male",BirthDate="2026-06-15",BirthHour="12",BirthMinute="00",BirthCountry="台灣",BirthCity="台北市",Remarks="大方俐落、有志氣",AvoidCharacters="宏、志",AvoidMatchMode="same_sound_tone",NoAvoidanceCharacters=false,NoRequiredCharacter=true};
  var item=cases.Save(input);var runs=new ChartRunRepository(DatabasePath);
  using var fixture=JsonDocument.Parse(typeof(DemoWorkspace).Assembly.GetManifestResourceStream("Samples.openfate-lin.json")!);
  var chart=fixture.RootElement.GetProperty("response").GetProperty("structuredContent").GetProperty("data").GetProperty("chart");
  var chartInput=Step2Json.Element(new{policy=new{sourceKind="offline-fictional-demo"},time=new{localCivilTime="2026-06-15T12:00:00",timeZoneID="Asia/Taipei",utcOffsetHours=8},place=new{name="台北市"}});
  var start=runs.Start(item.NamingCaseID,item.CaseRevisionID,chartInput);
  runs.End(start.Run,start.Step,"completed",chart,null,null,item.NamingCaseID,item.CaseRevisionID);
  var context=Mark(new Step3Service(DatabasePath).Prepare(item.NamingCaseID,item.CaseRevisionID));
  var analysis=Step2Json.Element(new{
   summary="寶寶出生於夏季午月，命盤的火勢較明顯；本次從支持庚金的方向，選擇土、金類用字。",
   direction="夏季火旺，這次提案考量庚金需要的支持，以土生金、金助金的思路選字，並搭配寬厚、堅定與珍惜的寓意。",
   namingElements=new[]{"土","金"},limitations=new[]{"這是試行樣本，土金方向尚未定案；是否採用其他取名方向，仍需進一步確認。"},blockingIssues=Array.Empty<string>(),
   evidence=new[]{
    new{ruleId="R2",factPath="/pillars/month/branch",factValue="午",reason="午月代表夏季，扶抑思路會先評估金在當令火勢下是否需要支持；不能只數五行個數。"},
    new{ruleId="R2",factPath="/pillars/day/ganZhi",factValue="庚申",reason="庚金坐申，表示已有同類根氣；這是不能直接斷為很弱、也不能直接宣稱必須補土金的原因。"},
    new{ruleId="R3",factPath="/pillars/hour/stem",factValue="壬",reason="時干已有壬水，仍須考量寒暖燥濕。這是本次土金方向保留條件、未直接排除金水思路的原因。"}},remarks=Array.Empty<object>()});
  var analysisErrors=AnalysisContract.Validate(analysis,context.GetProperty("facts"),input.Remarks);
  if(analysisErrors.Count>0)throw new InvalidOperationException(string.Join("；",analysisErrors));
  start=runs.Start(item.NamingCaseID,item.CaseRevisionID,context,3);
  runs.End(start.Run,start.Step,"completed",Step2Json.Element(new{canProceed=false,canTrialProceed=true,ruleStatus="experimental",analysis}),null,null,item.NamingCaseID,item.CaseRevisionID,context.GetProperty("sourceChartRunId").GetInt64());
  context=Mark(new Step4Service(DatabasePath).Prepare(item.NamingCaseID,item.CaseRevisionID));
  var pools=context.GetProperty("pools").EnumerateArray().Where(p=>p.GetProperty("firstCharacters").GetArrayLength()==4&&p.GetProperty("lastCharacters").GetArrayLength()==4).ToArray();
  string[] firstSet=["宥","禹","威","垣"];
  var firstPool=pools.Single(p=>p.GetProperty("firstCharacters").EnumerateArray().Select(c=>c.GetProperty("character").GetString()!).ToHashSet().SetEquals(firstSet));
  var secondPool=pools.Single(p=>p.GetProperty("firstCharacters").EnumerateArray().Select(c=>c.GetProperty("character").GetString()!).ToHashSet().SetEquals(new[]{"鈺","新","鉉","鈴"})&&p.GetProperty("lastCharacters").EnumerateArray().Select(c=>c.GetProperty("character").GetString()!).ToHashSet().SetEquals(new[]{"城","恩","容","益"}));
  string[][] exampleNames=[["林宥錚","林宥錫","林禹錫","林威錦","林垣錕"],["林鈺城","林鈺恩","林新恩","林鉉容","林鈴益"]];
  var candidateIds=context.GetProperty("candidates").EnumerateArray().ToDictionary(c=>c.GetProperty("name").GetString()!,c=>c.GetProperty("id").GetString()!);
  string[] selectionReasons=["寬厚待人，保有清楚的立場。","包容與珍惜相伴，讀來從容。","務實治事，也珍惜每份心意。","自我要求與豐富色彩相映。","守護所愛，也願意持續磨練。","珍視內在，給家人安定支持。","溫潤珍貴，也記得感謝。","懷抱新意，同時珍惜情誼。","願意承擔，也懂得寬容。","清朗靈動，讓付出帶來助益。"];
  var naming=Step2Json.Element(new{groups=new[]{firstPool,secondPool}.Select((p,i)=>new{poolId=p.GetProperty("poolId").GetString(),label=i==0?"寬厚有志":"溫潤開朗",direction=i==0?"從包容與堅毅出發，寄託踏實承擔的祝願。":"以珍惜與新意搭配，帶出清朗親和的氣息。",recommendation=new{candidateId=candidateIds[exampleNames[i][0]],reason=selectionReasons[i*5]},alternatives=exampleNames[i].Skip(1).Select((n,j)=>new{candidateId=candidateIds[n],reason=selectionReasons[i*5+j+1]})}),remarks=new[]{new{quote=input.Remarks,disposition="applied",reason="以簡潔字義寄託大方有志的祝願。"}}});
  var errors=NamingContract.Validate(naming,context);if(errors.Count>0)throw new InvalidOperationException(string.Join("；",errors));
  start=runs.Start(item.NamingCaseID,item.CaseRevisionID,context,4);
  runs.End(start.Run,start.Step,"completed",Step2Json.Element(new{canProceed=false,canTrialProceed=true,ruleStatus="experimental",naming,dictionaryVersion=context.GetProperty("policy").GetProperty("dictionaryVersion"),ruleVersion=NamingContract.Version,schemaVersion="naming-output-v2",pools=context.GetProperty("pools"),candidates=context.GetProperty("candidates"),coverage=context.GetProperty("coverage"),warnings=new[]{"離線虛構示範；H1 土金為條件式假設，尚未排除 H2。"}}),null,null,item.NamingCaseID,item.CaseRevisionID,context.GetProperty("sourceChartRunId").GetInt64(),context.GetProperty("sourceAnalysisRunId").GetInt64());
  string[] wishes=["待人寬厚，遇事守住自己的方向。","珍惜善意，也願意給人餘地。","腳步踏實，心裡留著溫暖。","有自己的光彩，也懂得欣賞他人。","照顧所愛，在磨練中成長。","保有溫潤初心，成為可靠的陪伴。","珍惜情誼，常懷感謝。","勇於嘗試，記得一路相助的人。","能承擔，也能理解不同聲音。","心思清朗，讓所學成為他人的助力。"];
  var prose=Step2Json.Element(new{groups=new[]{firstPool,secondPool}.Select((p,i)=>new{poolId=p.GetProperty("poolId").GetString(),entries=exampleNames[i].Select((n,j)=>new{candidateId=candidateIds[n],wish=wishes[i*5+j],reason=selectionReasons[i*5+j]})}),summary="前組偏向寬厚堅毅，後組偏向溫潤開朗，可先讀出聲音，再選擇喜歡的字義。示範方向仍屬研究試行。"});
  return new(item,new ReportRepository(DatabasePath).CreateOfflineDemo(item.NamingCaseID,item.CaseRevisionID,prose));
 }
}

