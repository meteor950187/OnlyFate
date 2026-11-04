using System.Text.Json;

namespace OnlyFate.Database;

public sealed record NamingCharacter(string Character, int KangxiStrokes, string[] Readings, string Meaning, string? TrialElement, string Source, string SourceNote, bool NameEligible, NamingCharacterProfile? Profile = null);
public sealed record NamingCharacterProfile(string LiteralMeaning, string NamingWish, string Style, string Caution, string MeaningSource, string ReadingSource, string StrokeSource, string ElementSource, string ElementRationale);
public sealed record NameGrids(int Heaven, int Person, int Earth, int Outer, int Total, string ThreeTalents, bool Pass);
public sealed record NamingCandidate(string Id, string Name, NamingCharacter Surname, NamingCharacter First, NamingCharacter Last, NameGrids Grids);
public sealed record NamingPool(string DictionaryVersion, string RuleVersion, IReadOnlyList<NamingCandidate> Candidates, IReadOnlyList<string> Coverage, IReadOnlyList<string> Warnings);

public static class NamingEngine
{
    public const string Version = "naming-pools-trial-v2";
    private sealed record AvoidReading(string Character, string[] Readings, string Source);
    private sealed record DictionaryData(string Version, string RuleVersion, NamingCharacter[] Characters, AvoidReading[] AvoidReadings, int[] GoodNumbers);
    private static readonly DictionaryData Data = Load();
    private static DictionaryData Load()
    {
        using var stream = typeof(NamingEngine).Assembly.GetManifestResourceStream("Naming.limited-v2.json")!;
        var data=JsonSerializer.Deserialize<DictionaryData>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        if(data.RuleVersion!=Version || data.Characters.Any(c=>c.KangxiStrokes<=0||c.Readings.Length==0||string.IsNullOrWhiteSpace(c.Source)||c.Profile is null||new[]{c.Profile.LiteralMeaning,c.Profile.NamingWish,c.Profile.Style,c.Profile.Caution,c.Profile.MeaningSource,c.Profile.ReadingSource,c.Profile.StrokeSource,c.Profile.ElementSource,c.Profile.ElementRationale}.Any(string.IsNullOrWhiteSpace)))
            throw new InvalidOperationException("字庫缺少可追溯的筆畫、讀音或字義profile，已停止選名。");
        return data;
    }
    public static NameGrids Calculate(int surname, int first, int last)
    {
        if (surname <= 0 || first <= 0 || last <= 0) throw new ArgumentOutOfRangeException(nameof(surname));
        int heaven=surname+1, person=surname+first, earth=first+last, outer=last+1, total=surname+first+last;
        string a=Element(heaven), b=Element(person), c=Element(earth);
        bool Controls(string x,string y) => (x,y) is ("木","土") or ("土","木") or ("土","水") or ("水","土") or ("水","火") or ("火","水") or ("火","金") or ("金","火") or ("金","木") or ("木","金");
        return new(heaven,person,earth,outer,total,a+b+c,new[]{person,earth,outer,total}.All(Data.GoodNumbers.Contains) && !Controls(a,b) && !Controls(b,c));
    }
    private static string Element(int number) => (number%10) switch { 1 or 2=>"木",3 or 4=>"火",5 or 6=>"土",7 or 8=>"金",_=>"水" };
    public static NamingPool Generate(CaseInput input, IReadOnlyList<string> namingElements)
    {
        var validated = CaseInputValidator.Validate(input);
        return GenerateCore(input,namingElements,validated.Surname,validated.AvoidCharacters,validated.RequiredCharacter);
    }
    internal static NamingPool FromContext(JsonElement context)
    {
        var input=context.GetProperty("caseData").Deserialize<CaseInput>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        string[] avoid=input.AvoidCharacters.EnumerateRunes().Where(r=>System.Text.Rune.IsLetter(r)).Select(r=>r.ToString()).Distinct().ToArray();
        return GenerateCore(input,context.GetProperty("analysis").GetProperty("namingElements").EnumerateArray().Select(e=>e.GetString()!).ToArray(),input.Surname,avoid,input.RequiredCharacter);
    }
    private static NamingPool GenerateCore(CaseInput input,IReadOnlyList<string> namingElements,string surnameText,string[] avoidCharacters,string requiredCharacter)
    {
        if (namingElements.Count is <1 or >2 || namingElements.Distinct().Count()!=namingElements.Count || namingElements.Any(e=>!new[]{"木","火","土","金","水"}.Contains(e)))
            throw new InvalidOperationException("缺少有效的結構化選字五行，請重新判讀。");
        var surname=Data.Characters.SingleOrDefault(c=>c.Character==surnameText);
        if (surname is null) throw new InvalidOperationException("有限字庫沒有此姓的康熙筆畫與國語讀音，已停止選名。");
        List<string> warnings=["有限字庫與字五行為未驗證試行規則，不代表全字庫最優或正式交付資格。"];
        List<string> coverage=[$"字庫 {Data.Version}：{Data.Characters.Length} 字，宏志僅避音；只涵蓋已收國語讀音。", "全名任兩字重字或已知同音同調一律排除。"];
        if (avoidCharacters.Length==0) coverage.Add(input.NoAvoidanceCharacters ? "已明確確認無避諱字。" : "避諱字未提供，尚未確認家族限制。");
        if (requiredCharacter.Length==0) coverage.Add(input.NoRequiredCharacter ? "已明確確認無指定字。" : "指定字未提供，尚未確認家族限制。");
        if ((!input.NoAvoidanceCharacters && avoidCharacters.Length==0) || (!input.NoRequiredCharacter && requiredCharacter.Length==0)) warnings.Add("家族限制有未提供欄位，需確認後才可考慮正式交付。");
        var avoidSounds=new HashSet<string>(StringComparer.Ordinal);
        if (input.AvoidMatchMode=="same_sound_tone") foreach (string ch in avoidCharacters)
        {
            string[]? readings=Data.Characters.FirstOrDefault(c=>c.Character==ch)?.Readings ?? Data.AvoidReadings.FirstOrDefault(c=>c.Character==ch)?.Readings;
            if (readings is null || readings.Length==0) throw new InvalidOperationException($"有限字庫沒有避諱字「{ch}」的國語讀音，已停止選名。");
            avoidSounds.UnionWith(readings);
        }
        if (requiredCharacter.Length!=0 && !Data.Characters.Any(c=>c.Character==requiredCharacter && c.NameEligible && c.TrialElement is not null))
            throw new InvalidOperationException("有限字庫無法完整驗證指定字的筆畫、讀音與試行五行，已停止選名。");
        var pool=Data.Characters.Where(c=>c.NameEligible && c.TrialElement is not null && namingElements.Contains(c.TrialElement)
            && !avoidCharacters.Contains(c.Character) && !c.Readings.Any(avoidSounds.Contains)).ToArray();
        List<NamingCandidate> candidates=[];
        foreach (var first in pool) foreach (var last in pool)
        {
            var full=new[]{surname,first,last};
            if (full.Select(c=>c.Character).Distinct().Count()!=3) continue;
            if (full.SelectMany(c=>c.Readings).Count()!=full.SelectMany(c=>c.Readings).Distinct().Count()) continue;
            string required=requiredCharacter;
            if (required.Length!=0 && !(input.RequiredPosition switch { "first"=>first.Character==required,"last"=>last.Character==required,_=>first.Character==required || last.Character==required })) continue;
            var grids=Calculate(surname.KangxiStrokes,first.KangxiStrokes,last.KangxiStrokes);
            if (!grids.Pass) continue;
            string id=$"{Data.Version}-{char.ConvertToUtf32(surname.Character,0):X}-{char.ConvertToUtf32(first.Character,0):X}-{char.ConvertToUtf32(last.Character,0):X}";
            candidates.Add(new(id,surname.Character+first.Character+last.Character,surname,first,last,grids));
        }
        if (candidates.Count==0) warnings.Add("目前有限字庫沒有通過全部條件的候選，未放寬任何限制；請擴充有來源字庫或確認輸入。");
        return new(Data.Version,Version,candidates,coverage,warnings);
    }
}
