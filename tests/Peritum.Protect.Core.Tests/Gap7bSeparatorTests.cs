using Peritum.Protect.Core.Redaction;
using Peritum.Protect.Core.Rules;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Tests;

public class Gap7bSeparatorTests
{
    [Theory]
    [InlineData("br.cpf", "529.982.247-25")]
    [InlineData("br.cnpj", "11.222.333/0001-81")]
    [InlineData("in.aadhaar", "2341 2341 2346")]
    [InlineData("gb.nhs", "943 476 5919")]
    [InlineData("gb.nhs", "943-476-5919")]
    [InlineData("de.steuerid", "12 345 678 911")]
    [InlineData("nl.bsn", "1112.22.333")]
    [InlineData("fr.nir", "2 84 05 75 123 456 72")]
    public void Standard_printed_form_is_found_and_the_token_covers_separators(string ruleId, string printed)
    {
        var text = "ID " + printed + " end";
        var finding = Find(text, ruleId);
        Assert.NotNull(finding);
        Assert.Equal(printed, text.Substring(finding!.Start, finding.Length));

        var redacted = TextRedactor.Apply(text, [finding]);
        Assert.DoesNotContain(printed, redacted, StringComparison.Ordinal);
        Assert.DoesNotContain(".", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("-", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("/", redacted, StringComparison.Ordinal);

        var (reversible, map) = ReversibleRedactor.Apply(text, [finding], reversible: true);
        Assert.DoesNotContain(printed, reversible, StringComparison.Ordinal);
        Assert.Contains(printed, map.Tokens.Values);
    }

    [Theory]
    [InlineData("br.cpf", "529.98.2247-25")]
    [InlineData("br.cpf", "529 982 247 25")]
    [InlineData("br.cnpj", "11.22.2333/0001-81")]
    [InlineData("in.aadhaar", "23412 341 2346")]
    [InlineData("gb.nhs", "9434 76 5919")]
    [InlineData("de.steuerid", "123 456 789 11")]
    [InlineData("de.steuerid", "12 345 678 903")]
    [InlineData("nl.bsn", "111.222.333")]
    [InlineData("fr.nir", "284 05 75 123 456 72")]
    [InlineData("fr.nir", "2 84 05 75 123 456 78")]
    public void Mixed_or_wrong_grouping_is_not_a_national_id(string ruleId, string printed)
    {
        Assert.Null(Find("ID " + printed + " end", ruleId));
    }

    private static Finding? Find(string text, string ruleId)
    {
        var rule = BuiltinRules.LoadEmbedded().SelectMany(pack => pack.Rules).Single(item => item.Id == ruleId);
        var findings = new RuleScanner().Find(text, [rule]);
        return findings.Count == 0 ? null : findings[0];
    }
}
