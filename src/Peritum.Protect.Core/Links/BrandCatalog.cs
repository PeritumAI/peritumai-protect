using System.Text.Json;
using System.Text.Json.Serialization;
using Peritum.Protect.Core.Rules;

namespace Peritum.Protect.Core.Links;

public sealed record BrandKeyword(string Text, bool CommonWord);

public sealed class BrandRecord
{
    public string Name { get; }
    public string Country { get; }
    public IReadOnlyList<string> Domains { get; }
    public IReadOnlyList<BrandKeyword> Keywords { get; }

    public BrandRecord(string name, string country, IReadOnlyList<string> domains, IReadOnlyList<BrandKeyword> keywords)
    {
        Name = name;
        Country = country;
        Domains = domains;
        Keywords = keywords;
    }
}

public sealed class BrandCatalog
{
    private static readonly string[] Countries =
    [
        "rs", "hr", "si", "ba", "me", "mk", "de", "fr", "es", "it", "nl", "pl", "gb", "us", "br", "in"
    ];

    private readonly Dictionary<string, string> _domains;
    private readonly Dictionary<string, string> _keywords;
    private readonly Dictionary<string, string> _domainLabels;
    private readonly HashSet<string> _common;
    private readonly List<(string Label, string Brand)> _labels;

    private BrandCatalog(
        IReadOnlyList<BrandRecord> brands,
        IReadOnlyList<string> sources,
        string asOf,
        Dictionary<string, string> domains,
        Dictionary<string, string> keywords,
        Dictionary<string, string> domainLabels,
        HashSet<string> common,
        List<(string Label, string Brand)> labels)
    {
        Brands = brands;
        Sources = sources;
        AsOf = asOf;
        _domains = domains;
        _keywords = keywords;
        _domainLabels = domainLabels;
        _common = common;
        _labels = labels;
    }

    public IReadOnlyList<BrandRecord> Brands { get; }
    public IReadOnlyList<string> Sources { get; }
    public string AsOf { get; }

    public static BrandCatalog LoadGlobal() => Load(includeCountries: false, region: null, extraCountries: null, extraDomains: null);

    public static BrandCatalog LoadAll(IEnumerable<string>? extraDomains = null) =>
        Load(includeCountries: true, region: null, extraCountries: null, extraDomains: extraDomains);

    public static BrandCatalog ForRegion(
        string? region,
        IEnumerable<string>? extraCountries = null,
        IEnumerable<string>? extraDomains = null) =>
        Load(includeCountries: false, region: region, extraCountries: extraCountries, extraDomains: extraDomains);

    public bool IsOfficial(string? registrableDomain)
    {
        var key = Confusables.ToAscii(registrableDomain);
        return key.Length > 0 && _domains.ContainsKey(key);
    }

    public string? Owner(string? registrableDomain)
    {
        var key = Confusables.ToAscii(registrableDomain);
        return key.Length > 0 && _domains.TryGetValue(key, out var name) ? name : null;
    }

    public bool TryKeyword(string token, out string brand) =>
        _keywords.TryGetValue(token, out brand!);

    public bool IsCommonWord(string token) => _common.Contains(token);

    public bool TryDomainLabel(string token, out string brand) =>
        _domainLabels.TryGetValue(token, out brand!);

    public IReadOnlyList<(string Label, string Brand)> LongLabels => _labels;

    private static BrandCatalog Load(
        bool includeCountries,
        string? region,
        IEnumerable<string>? extraCountries,
        IEnumerable<string>? extraDomains)
    {
        var global = Read("links.brands.json");
        var brands = new List<BrandRecord>();
        foreach (var brand in global.Brands ?? [])
            brands.Add(ToRecord(brand, ""));

        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (includeCountries)
        {
            foreach (var code in Countries)
                wanted.Add(code);
        }
        else
        {
            var home = CountryRegions.Canonical(region);
            if (home.Length == 2)
                wanted.Add(home);
            if (extraCountries is not null)
            {
                foreach (var code in extraCountries)
                {
                    var canonical = CountryRegions.Canonical(code);
                    if (canonical.Length == 2)
                        wanted.Add(canonical);
                }
            }
        }

        foreach (var code in wanted)
        {
            var pack = Read("links.country." + code.ToLowerInvariant() + ".json");
            var country = (pack.Country ?? code).ToUpperInvariant();
            foreach (var brand in pack.Brands ?? [])
                brands.Add(ToRecord(brand, country));
        }

        if (extraDomains is not null)
        {
            var domains = extraDomains
                .Select(domain => Confusables.ToAscii(domain))
                .Where(domain => domain.Contains('.'))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (domains.Count > 0)
            {
                var keywords = domains
                    .Select(domain => domain.Split('.')[0])
                    .Where(label => label.Length >= 5)
                    .Distinct(StringComparer.Ordinal)
                    .Select(label => new BrandKeyword(label, false))
                    .ToList();
                brands.Add(new BrandRecord("Sites I use", "", domains, keywords));
            }
        }

        return Compile(brands, global.Sources ?? [], global.AsOf ?? "");
    }

    private static BrandCatalog Compile(
        List<BrandRecord> brands,
        IReadOnlyList<string> sources,
        string asOf)
    {
        var domains = new Dictionary<string, string>(StringComparer.Ordinal);
        var keywords = new Dictionary<string, string>(StringComparer.Ordinal);
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        var common = new HashSet<string>(StringComparer.Ordinal);
        foreach (var brand in brands)
        {
            foreach (var domain in brand.Domains)
            {
                domains.TryAdd(domain, brand.Name);
                var label = domain.Split('.')[0];
                if (label.Length >= 5)
                    labels.TryAdd(label, brand.Name);
            }

            foreach (var keyword in brand.Keywords)
            {
                if (keyword.Text.Length >= 3)
                    keywords.TryAdd(keyword.Text, brand.Name);
                if (keyword.CommonWord && keyword.Text.Length > 0)
                    common.Add(keyword.Text);
            }
        }

        var pairs = labels.Select(pair => (pair.Key, pair.Value)).ToList();
        return new BrandCatalog(brands, sources, asOf, domains, keywords, labels, common, pairs);
    }

    private static BrandRecord ToRecord(BrandDto brand, string country)
    {
        var domains = (brand.Domains ?? [])
            .Select(domain => Confusables.ToAscii(domain))
            .Where(domain => domain.Contains('.'))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var keywords = (brand.Keywords ?? [])
            .Where(keyword => !string.IsNullOrWhiteSpace(keyword.Text))
            .GroupBy(keyword => keyword.Text, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        return new BrandRecord((brand.Name ?? "").Trim(), country, domains, keywords);
    }

    private static BrandFile Read(string resource)
    {
        using var stream = typeof(BrandCatalog).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("Missing " + resource);
        var file = JsonSerializer.Deserialize<BrandFile>(stream, JsonDefaults.Compact);
        return file ?? new BrandFile();
    }

    private sealed class BrandFile
    {
        public string? AsOf { get; set; }
        public string? Country { get; set; }
        public List<string>? Sources { get; set; }
        public List<BrandDto>? Brands { get; set; }
    }

    private sealed class BrandDto
    {
        public string? Name { get; set; }
        public List<string>? Domains { get; set; }
        [JsonConverter(typeof(KeywordListConverter))]
        public List<BrandKeyword>? Keywords { get; set; }
    }

    private sealed class KeywordListConverter : JsonConverter<List<BrandKeyword>>
{
    public override List<BrandKeyword> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException();
        var list = new List<BrandKeyword>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
                return list;
            if (reader.TokenType == JsonTokenType.String)
            {
                list.Add(new BrandKeyword((reader.GetString() ?? "").Trim().ToLowerInvariant(), false));
                continue;
            }

            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException();
            var text = "";
            var common = false;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                    continue;
                var name = reader.GetString();
                reader.Read();
                if (name is "word" or "text")
                    text = reader.GetString() ?? "";
                else if (name == "commonWord")
                    common = reader.TokenType == JsonTokenType.True;
            }

            list.Add(new BrandKeyword(text.Trim().ToLowerInvariant(), common));
        }

        throw new JsonException();
    }

        public override void Write(Utf8JsonWriter writer, List<BrandKeyword> value, JsonSerializerOptions options) =>
            throw new NotSupportedException();
    }
}
