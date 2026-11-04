using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace OnlyFate.Database;

public sealed class BirthplaceCatalog
{
    public static BirthplaceCatalog Default => cached.Value;
    private static readonly Lazy<BirthplaceCatalog> cached = new(Load);
    private readonly Dictionary<string, string> countries;
    private readonly List<Entry> entries;
    private sealed record Entry(BirthplaceCandidate Place, HashSet<string> Names, long Population);

    private BirthplaceCatalog(Dictionary<string, string> countries, List<Entry> entries)
    {
        this.countries = countries; this.entries = entries;
    }

    public IReadOnlyList<BirthplaceCandidate> Find(string country, string city)
    {
        if (!countries.TryGetValue(Normalize(country), out string? code))
            throw new Step2Exception("COUNTRY_NOT_FOUND", "無法辨識出生國家／地區，請補充通用名稱或 ISO 國家代碼。");
        string name = Normalize(city);
        if (name.Length == 0) return [];
        Entry[] exact = entries.Where(e => e.Place.CountryCode == code && e.Names.Contains(name)).ToArray();
        IEnumerable<Entry> matches = exact.Length != 0 ? exact : entries.Where(e => e.Place.CountryCode == code
            && name.Length >= 2 && e.Names.Any(n => n.StartsWith(name, StringComparison.Ordinal)));
        return matches.OrderByDescending(e => e.Population).Take(30).Select(e => e.Place).ToArray();
    }

    internal static Stream Resource(string name) => Assembly.GetExecutingAssembly().GetManifestResourceStream("Geography." + name)
        ?? throw new InvalidOperationException("缺少本機城市資料，請確認程式檔案完整。");

    private static BirthplaceCatalog Load()
    {
        using Stream sourceStream = Resource("source.json");
        using JsonDocument source = JsonDocument.Parse(sourceStream);
        string version = "GeoNames/" + source.RootElement.GetProperty("retrievedDate").GetString() + "/"
            + source.RootElement.GetProperty("files")[0].GetProperty("sha256").GetString();
        Dictionary<string, string> countries = new(StringComparer.Ordinal);
        using (StreamReader reader = new(Resource("countryInfo.txt")))
        {
            while (reader.ReadLine() is string line)
            {
                if (line.StartsWith('#')) continue;
                string[] fields = line.Split('\t');
                if (fields.Length < 5) continue;
                foreach (string name in new[] { fields[0], fields[1], fields[4] }) countries[Normalize(name)] = fields[0];
            }
        }
        foreach ((string name, string code) in new[] { ("台灣", "TW"), ("臺灣", "TW"), ("中華民國", "TW"), ("中國", "CN"),
            ("中國大陸", "CN"), ("香港", "HK"), ("澳門", "MO"), ("日本", "JP"), ("新加坡", "SG"), ("馬來西亞", "MY"),
            ("美國", "US"), ("加拿大", "CA"), ("澳洲", "AU"), ("澳大利亞", "AU"), ("英國", "GB"), ("韓國", "KR") })
            countries[Normalize(name)] = code;
        Dictionary<string, string> regions = new();
        using (StreamReader reader = new(Resource("admin1CodesASCII.txt")))
        {
            while (reader.ReadLine() is string line)
            {
                string[] fields = line.Split('\t');
                if (fields.Length >= 2) regions[fields[0]] = fields[1];
            }
        }
        List<Entry> entries = [];
        using Stream zipStream = Resource("cities15000.zip");
        using ZipArchive zip = new(zipStream);
        using StreamReader cities = new(zip.GetEntry("cities15000.txt")!.Open());
        while (cities.ReadLine() is string line)
        {
            string[] f = line.Split('\t');
            if (f.Length < 19 || string.IsNullOrWhiteSpace(f[17])) continue;
            string region = regions.GetValueOrDefault(f[8] + "." + f[10], f[10]);
            BirthplaceCandidate place = new(long.Parse(f[0], CultureInfo.InvariantCulture), f[1], f[8], region,
                double.Parse(f[4], CultureInfo.InvariantCulture), double.Parse(f[5], CultureInfo.InvariantCulture), f[17], version);
            HashSet<string> names = f[3].Split(',').Append(f[1]).Append(f[2]).Select(Normalize).Where(n => n.Length != 0).ToHashSet(StringComparer.Ordinal);
            entries.Add(new(place, names, long.TryParse(f[14], out long population) ? population : 0));
        }
        return new(countries, entries);
    }

    private static string Normalize(string? text)
    {
        string name = new((text ?? "").Normalize(NormalizationForm.FormKC).Replace('臺', '台').ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        return name.EndsWith('市') && name.Length > 2 ? name[..^1] : name;
    }
}
