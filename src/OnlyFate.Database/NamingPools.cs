using System.Security.Cryptography;
using System.Text;
namespace OnlyFate.Database;

public sealed record NamingCharacterPool(string PoolId, IReadOnlyList<NamingCharacter> FirstCharacters, IReadOnlyList<NamingCharacter> LastCharacters, IReadOnlyList<string> PairIds);

/// <summary>Exhaustive biclique search over the finite dictionary; never truncates candidate pairs.</summary>
public static class NamingPools
{
    public static IReadOnlyList<NamingCharacterPool> Generate(IReadOnlyList<NamingCandidate> candidates)
    {
        List<NamingCharacterPool> result=[];
        foreach(var group in candidates.GroupBy(c=>(c.First.KangxiStrokes,c.Last.KangxiStrokes)).OrderBy(g=>g.Key))
        {
            var first=group.Select(c=>c.First).DistinctBy(c=>c.Character).OrderBy(c=>c.Character,StringComparer.Ordinal).ToArray();
            var last=group.Select(c=>c.Last).DistinctBy(c=>c.Character).OrderBy(c=>c.Character,StringComparer.Ordinal).ToArray();
            var pairs=group.ToDictionary(c=>(c.First.Character,c.Last.Character));
            foreach(int sizeA in new[]{4,5}) foreach(var a in Subsets(first,sizeA))
            {
                var compatible=last.Where(b=>a.All(x=>pairs.ContainsKey((x.Character,b.Character)))).ToArray();
                foreach(int sizeB in new[]{4,5}) foreach(var b in Subsets(compatible,sizeB))
                {
                    if(a.Concat(b).Select(c=>c.Character).Distinct().Count()!=a.Length+b.Length)continue;
                    string key=string.Join(",",a.Select(c=>c.Character))+"/"+string.Join(",",b.Select(c=>c.Character));
                    string id="pool-"+Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..20];
                    result.Add(new(id,a,b,a.SelectMany(x=>b.Select(y=>pairs[(x.Character,y.Character)].Id)).ToArray()));
                }
            }
        }
        return result;
    }
    public static bool Compatible(NamingCharacterPool a,NamingCharacterPool b) => a.PoolId!=b.PoolId && Characters(a).Intersect(Characters(b)).Count()<=2;
    public static IEnumerable<string> Characters(NamingCharacterPool pool)=>pool.FirstCharacters.Concat(pool.LastCharacters).Select(c=>c.Character);
    public static void RequireTwo(IReadOnlyList<NamingCharacterPool> pools)
    {
        if(!pools.SelectMany((a,i)=>pools.Skip(i+1).Select(b=>(a,b))).Any(x=>Compatible(x.a,x.b)))
            throw new InvalidOperationException("有限字庫在目前避諱、讀音、必用字位置、五行與數理限制下，找不到兩組各位置4–5字、跨組重複不超過2字的完整字池；未放寬限制，尚未呼叫模型。指定字固定於姓名時可能無法形成自由字池。");
    }
    private static IEnumerable<NamingCharacter[]> Subsets(NamingCharacter[] source,int count)
    {
        var indices=Enumerable.Range(0,count).ToArray();
        if(source.Length<count)yield break;
        while(true) {
            yield return indices.Select(i=>source[i]).ToArray();
            int p=count-1;while(p>=0&&indices[p]==source.Length-count+p)p--;
            if(p<0)yield break;
            indices[p]++;for(int j=p+1;j<count;j++)indices[j]=indices[j-1]+1;
        }
    }
}
