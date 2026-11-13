using Peritum.Protect.Core.Rules;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Tests;

public class Gap7bOverlapTests
{
    [Theory]
    [InlineData("OIB 75143090474", "hr.oib")]
    [InlineData("PESEL 02070803628", "pl.pesel")]
    [InlineData("PIB 301423672", "rs.pib")]
    [InlineData("OIB 02070803628", "hr.oib")]
    public void A_nearby_label_chooses_the_id(string text, string ruleId)
    {
        var findings = new RuleScanner().Find(text, CountryRules());
        var hit = Assert.Single(findings);
        Assert.Equal(ruleId, hit.RuleId);
    }

    [Theory]
    [InlineData("BR", "br.cpf")]
    [InlineData("HR", "hr.oib")]
    [InlineData("DE", "br.cpf")]
    public void Without_a_deciding_label_region_then_the_stronger_check_wins(string region, string ruleId)
    {
        var findings = new RuleScanner(region).Find("CPF OIB 75143090474", CountryRules());
        var hit = Assert.Single(findings);
        Assert.Equal(ruleId, hit.RuleId);
    }

    private static List<Rule> CountryRules() =>
        BuiltinRules.LoadEmbedded()
            .Where(pack => pack.Id.StartsWith("country.", StringComparison.Ordinal))
            .SelectMany(pack => pack.Rules)
            .ToList();
}
