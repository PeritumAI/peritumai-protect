using System.Reflection;
using System.Text.Json;

namespace Peritum.Protect.Core.Rules;

public static class BuiltinRules
{
    public static IReadOnlyList<RulePack> LoadEmbedded()
    {
        var assembly = typeof(BuiltinRules).Assembly;
        var names = new[] { "builtin.pii.json", "builtin.secrets.json", "builtin.finance.json", "builtin.court.us.json", "builtin.court.eu.json" };
        var packs = new List<RulePack>();
        foreach (var name in names)
            packs.Add(RulePackLoader.Parse(Read(assembly, name)));
        packs.Add(LoadLabels(assembly));
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith("country.", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal)).OrderBy(name => name, StringComparer.Ordinal))
            packs.Add(RulePackLoader.Parse(Read(assembly, name)));
        return packs;
    }

    private static RulePack LoadLabels(Assembly assembly)
    {
        using var json = JsonDocument.Parse(Read(assembly, "builtin.labels.json"));
        var pack = new RulePack
        {
            Id = "builtin.labels",
            Name = "Reference labels",
            Version = "1.0.0",
            Description = "English labels. Other languages can add their own list.",
            Enabled = true
        };
        foreach (var group in json.RootElement.GetProperty("groups").EnumerateArray())
        {
            var labels = group.GetProperty("labels").EnumerateArray().Select(item => item.GetString() ?? "").ToList();
            pack.Rules.Add(LabelAnchor.RuleFor(
                group.GetProperty("id").GetString() ?? "",
                group.GetProperty("displayLabel").GetString() ?? "",
                group.GetProperty("plural").GetString() ?? "",
                group.GetProperty("placeholder").GetString() ?? "REF",
                labels));
        }

        return pack;
    }

    private static string Read(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Missing embedded rule pack " + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
