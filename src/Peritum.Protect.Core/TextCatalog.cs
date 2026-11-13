using System.Text.Json;

namespace Peritum.Protect.Core;

public sealed record LanguageInfo(string Code, string EnglishName, string NativeName, string Status, string Translators);

/// <summary>
/// UI strings from lang/*.json. English is embedded and used when a key is missing.
/// </summary>
public static class TextCatalog
{
    public const string English = "en";
    private static readonly object Gate = new();
    private static Dictionary<string, string> _english = new(StringComparer.Ordinal);
    private static Dictionary<string, string> _current = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, LanguageInfo> Languages = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Dictionary<string, string>> Packs = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, Dictionary<string, string>> _englishPlurals = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Dictionary<string, Dictionary<string, string>>> PluralPacks = new(StringComparer.OrdinalIgnoreCase);
    private static bool _pseudo;

    public static event Action? LanguageChanged;

    public static string Language { get; private set; } = English;

    public static bool IsRightToLeft => PluralRules.IsRightToLeft(Language);

    static TextCatalog()
    {
        var embedded = ReadEmbedded();
        _english = embedded.Strings;
        _englishPlurals = embedded.Plurals;
        _current = _english;
        Packs[English] = _english;
        PluralPacks[English] = _englishPlurals;
        Languages[English] = new LanguageInfo(English, "English", "English", "reviewed", "");
    }

    public static string Get(string key)
    {
        lock (Gate)
        {
            if (_current.TryGetValue(key, out var value) && value.Length > 0)
                return Finish(value);
            if (_english.TryGetValue(key, out var fallback))
                return Finish(fallback);
        }
        return key;
    }

    public static string Plural(string key, long count, params (string Name, object? Value)[] values)
    {
        var category = PluralRules.Category(Language, count);
        string template;
        lock (Gate)
            template = Form(key, category);
        foreach (var (name, value) in values)
            template = template.Replace("{" + name + "}", value?.ToString() ?? "", StringComparison.Ordinal);
        return _pseudo ? PseudoLoc.Apply(template) : template;
    }

    public static string Format(string key, params (string Name, object? Value)[] values)
    {
        var text = Get(key);
        foreach (var (name, value) in values)
            text = text.Replace("{" + name + "}", value?.ToString() ?? "", StringComparison.Ordinal);
        return text;
    }

    public static bool IsUserLanguage(string? code) =>
        !string.IsNullOrWhiteSpace(code) && code.Trim().Equals(English, StringComparison.OrdinalIgnoreCase);

    public static bool IsDeveloperLanguage(string? code)
    {
        var next = code?.Trim() ?? "";
        return next.Equals("qps-ploc", StringComparison.OrdinalIgnoreCase)
            || next.Equals("qps-rtl", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Blank means English. A hidden language such as de becomes English. Developer modes stay.</summary>
    public static string NormalizeSetting(string? code)
    {
        if (IsDeveloperLanguage(code))
            return code!.Trim();
        return "";
    }

    public static IReadOnlyList<LanguageInfo> Available() => UserLanguages();

    public static IReadOnlyList<LanguageInfo> UserLanguages()
    {
        lock (Gate)
        {
            return Languages.Values
                .Where(item => IsUserLanguage(item.Code))
                .OrderBy(item => item.EnglishName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public static void LoadDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            return;
        lock (Gate)
        {
            foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
            {
                var code = Path.GetFileNameWithoutExtension(path);
                if (!IsUserLanguage(code))
                    continue;
                var json = File.ReadAllText(path);
                var read = Read(code, json);
                Packs[code] = read.Strings;
                PluralPacks[code] = read.Plurals;
                _english = read.Strings;
                _englishPlurals = read.Plurals;
                Languages[code] = read.Info;
                if (Language.Equals(English, StringComparison.OrdinalIgnoreCase))
                    _current = read.Strings;
            }
        }
    }

    public static void Use(string? code)
    {
        var next = string.IsNullOrWhiteSpace(code) ? English : code.Trim();
        if (!IsDeveloperLanguage(next))
            next = English;
        lock (Gate)
        {
            Language = next;
            _pseudo = next.Equals("qps-ploc", StringComparison.OrdinalIgnoreCase);
            _current = Packs.TryGetValue(next, out var pack) ? pack : _english;
        }
        LanguageChanged?.Invoke();
    }

    public static (LanguageInfo Info, Dictionary<string, string> Strings, Dictionary<string, Dictionary<string, string>> Plurals) Read(string code, string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var englishName = Text(root, "language");
        var nativeName = Text(root, "nativeName");
        var status = Text(root, "status");
        var translators = Text(root, "translators");
        return (new LanguageInfo(code, englishName, nativeName, status, translators), ReadStrings(root), ReadPlurals(root));
    }

    public static IReadOnlyList<string> Placeholders(string text)
    {
        var found = new List<string>();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '{')
                continue;
            var end = text.IndexOf('}', i + 1);
            if (end < 0)
                break;
            var name = text[(i + 1)..end];
            if (name.Length > 0 && name.All(c => char.IsLetterOrDigit(c)))
                found.Add(name);
            i = end;
        }
        return found;
    }

    private static string Finish(string value) => _pseudo ? PseudoLoc.Apply(value) : value;

    private static string Form(string key, string category)
    {
        var pack = PluralPacks.TryGetValue(Language, out var forms) ? forms : _englishPlurals;
        if (!pack.TryGetValue(key, out var set) || set.Count == 0)
            _englishPlurals.TryGetValue(key, out set);
        if (set is not null)
        {
            if (set.TryGetValue(category, out var chosen) && chosen.Length > 0)
                return chosen;
            if (set.TryGetValue("other", out var other))
                return other;
        }
        return _current.TryGetValue(key, out var plain) ? plain : key;
    }

    private static (Dictionary<string, string> Strings, Dictionary<string, Dictionary<string, string>> Plurals) ReadEmbedded()
    {
        using var stream = typeof(TextCatalog).Assembly.GetManifestResourceStream("lang.en.json");
        if (stream is null)
            return (new Dictionary<string, string>(StringComparer.Ordinal), new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal));
        using var reader = new StreamReader(stream);
        var read = Read(English, reader.ReadToEnd());
        return (read.Strings, read.Plurals);
    }

    private static Dictionary<string, Dictionary<string, string>> ReadPlurals(JsonElement root)
    {
        var map = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        if (!root.TryGetProperty("plurals", out var plurals) || plurals.ValueKind != JsonValueKind.Object)
            return map;
        foreach (var item in plurals.EnumerateObject())
        {
            if (item.Value.ValueKind != JsonValueKind.Object)
                continue;
            var forms = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var form in item.Value.EnumerateObject())
            {
                if (form.Value.ValueKind == JsonValueKind.String)
                    forms[form.Name] = form.Value.GetString() ?? "";
            }
            map[item.Name] = forms;
        }
        return map;
    }

    private static Dictionary<string, string> ReadStrings(JsonElement root)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!root.TryGetProperty("strings", out var strings))
            return map;
        foreach (var item in strings.EnumerateObject())
        {
            if (item.Value.ValueKind == JsonValueKind.String)
                map[item.Name] = item.Value.GetString() ?? "";
        }
        return map;
    }

    private static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}
