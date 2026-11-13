using System.Text.Json;
using System.Text.RegularExpressions;
using Peritum.Protect.Core;

namespace Peritum.Protect.Core.Tests;

public class LanguageCatalogTests
{
    private static readonly string[] Brands =
    [
        "PeritumAI Protect",
        "Peritum",
        "PeritumAI",
        "peritumai.com",
        "https://peritumai.com",
        "https://peritumai.com/protect"
    ];

    [Fact]
    public void Language_files_match_english_keys_and_placeholders()
    {
        var root = Repo();
        var english = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "lang", "en.json")));
        var englishStrings = Keys(english.RootElement, "strings");
        var englishExplain = Keys(english.RootElement, "explanations");
        Assert.NotEmpty(englishStrings);
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "lang"), "*.json"))
        {
            var json = File.ReadAllText(path);
            var document = JsonDocument.Parse(json);
            var name = Path.GetFileName(path);
            var strings = Keys(document.RootElement, "strings");
            var explain = Keys(document.RootElement, "explanations");
            Assert.Equal(englishStrings.OrderBy(key => key, StringComparer.Ordinal), strings.OrderBy(key => key, StringComparer.Ordinal));
            Assert.Equal(englishExplain.OrderBy(key => key, StringComparer.Ordinal), explain.OrderBy(key => key, StringComparer.Ordinal));
            foreach (var key in englishStrings)
            {
                var expected = Placeholders(StringValue(english.RootElement, "strings", key));
                var actual = Placeholders(StringValue(document.RootElement, "strings", key));
                Assert.Equal(expected, actual);
            }
            _ = name;
        }
    }

    [Fact]
    public void Xaml_and_view_models_have_no_hardcoded_user_text()
    {
        var root = Repo();
        var app = Path.Combine(root, "src", "Peritum.Protect.App");
        var problems = new List<string>();
        foreach (var path in Directory.EnumerateFiles(app, "*.xaml", SearchOption.AllDirectories))
        {
            if (path.Contains(Path.DirectorySeparatorChar + "Themes" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                continue;
            var text = File.ReadAllText(path);
            foreach (Match match in Regex.Matches(text, @"(?:(?<=\s)|(?<=^))(?:Text|Content|Header|ToolTip|Title|AutomationProperties\.Name)\s*=\s*""([^""]*)""", RegexOptions.Multiline))
            {
                var value = match.Groups[1].Value;
                if (value.StartsWith('{') || value.StartsWith("&#", StringComparison.Ordinal) || !value.Any(char.IsLetter) || Brands.Contains(value))
                    continue;
                problems.Add(Path.GetFileName(path) + ": " + value);
            }
        }

        var sources = Directory.EnumerateFiles(Path.Combine(app, "ViewModels"), "*.cs")
            .Concat(Directory.EnumerateFiles(Path.Combine(app, "Views"), "*.cs"))
            .Append(Path.Combine(app, "App.xaml.cs"))
            .Append(Path.Combine(app, "MainWindow.xaml.cs"))
            .Append(Path.Combine(app, "Services", "TrayService.cs"));
        foreach (var path in sources)
        {
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var code = line.TrimStart();
                if (code.StartsWith("//", StringComparison.Ordinal))
                    continue;
                foreach (var raw in Quoted(line))
                {
                    if (!raw.Any(char.IsLetter) || !raw.Contains(' '))
                        continue;
                    if (Brands.Contains(raw) || raw.Contains("yyyy", StringComparison.Ordinal) || raw.Equals("Segoe UI", StringComparison.Ordinal))
                        continue;
                    problems.Add(Path.GetFileName(path) + ":" + (i + 1) + ": " + raw);
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    private static IEnumerable<string> Quoted(string line)
    {
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] != '"')
                continue;
            if (i > 0 && line[i - 1] == '\'')
                continue;
            var start = i + 1;
            var interpolated = i > 0 && line[i - 1] == '$';
            var sb = new System.Text.StringBuilder();
            for (i = start; i < line.Length; i++)
            {
                if (line[i] == '\\' && i + 1 < line.Length)
                {
                    sb.Append(line[i + 1]);
                    i++;
                    continue;
                }
                if (line[i] == '"')
                    break;
                if (interpolated && line[i] == '{')
                {
                    var end = line.IndexOf('}', i);
                    if (end > i)
                    {
                        i = end;
                        continue;
                    }
                }
                sb.Append(line[i]);
            }
            yield return sb.ToString();
        }
    }

    private static List<string> Keys(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var section))
            return [];
        return section.EnumerateObject().Select(item => item.Name).ToList();
    }

    private static string StringValue(JsonElement root, string section, string key)
    {
        var value = root.GetProperty(section).GetProperty(key);
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.GetRawText();
    }

    private static List<string> Placeholders(string text) => TextCatalog.Placeholders(text).OrderBy(item => item, StringComparer.Ordinal).ToList();

    private static string Repo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "lang", "en.json")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("lang/en.json was not found.");
    }
}
