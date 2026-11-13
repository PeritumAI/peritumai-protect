using Peritum.Protect.Core.Rules;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Tests;

public class EuUkIdTests
{
    [Fact]
    public void Each_eu_and_uk_id_accepts_a_synthetic_number_and_rejects_a_bad_one()
    {
        Assert.Equal("de.steuerid", Hit("Steuer-ID 12345678911", "de.steuerid"));
        Assert.Equal("de.phone", Hit("+49 151 00000000", "de.phone"));
        Assert.Equal("de.phone", Hit("030 1234567", "de.phone"));
        Assert.Equal("fr.nir", Hit("190017505600195", "fr.nir"));
        Assert.Equal("es.dni", Hit("12345678Z", "es.dni"));
        Assert.Equal("es.nie", Hit("Y1234567X", "es.nie"));
        Assert.True(NationalId.ValidCodice("RSSMRA85T10A562S"));
        Assert.Equal("it.codice", Hit("RSSMRA85T10A562S", "it.codice"));
        Assert.Equal("nl.bsn", Hit("BSN 123456782", "nl.bsn"));
        Assert.Equal("pl.pesel", Hit("14051401352", "pl.pesel"));
        Assert.Equal("gb.nino", Hit("NI number AB123456C", "gb.nino"));
        Assert.Equal("gb.nhs", Hit("NHS 1000000028", "gb.nhs"));

        Assert.Null(Hit("12345678910", "de.steuerid"));
        Assert.Null(Hit("01234567891", "de.steuerid"));
        Assert.Null(Hit("BG123456A", "gb.nino"));
        Assert.Null(Hit("QQ123456A", "gb.nino"));
        Assert.Null(Hit("12345678A", "es.dni"));
        Assert.Null(Hit("RSSMRA85T10A562A", "it.codice"));
        Assert.Null(Hit("064/000-0001", "de.phone"));
        Assert.Null(Hit("+381 64 000 0001", "de.phone"));
        Assert.Null(Hit("+49 151 00000000", "rs.phone"));
    }

    [Theory]
    [InlineData("2026-09-30")]
    [InlineData("28.09.2026")]
    [InlineData("20260928")]
    [InlineData("Order #12345678")]
    [InlineData("Invoice 2026-0912-4411")]
    [InlineData("+381 64 000 0001")]
    [InlineData("+33 6 00 00 00 00")]
    [InlineData("+1 202 555 0100")]
    public void Dates_orders_invoices_and_foreign_phones_are_not_eu_or_uk_ids(string text)
    {
        var ids = new[] { "de.steuerid", "de.phone", "fr.nir", "es.dni", "es.nie", "it.codice", "nl.bsn", "pl.pesel", "gb.nino", "gb.nhs" };
        foreach (var id in ids)
            Assert.Null(Hit(text, id));
    }

    [Fact]
    public void Germany_turns_on_for_region_de_and_not_for_the_window_language()
    {
        var packs = BuiltinRules.LoadEmbedded();
        var germany = CountryRegions.RuleIds(packs, "DE");
        Assert.Contains("de.steuerid", germany);
        Assert.Contains("de.phone", germany);
        Assert.DoesNotContain("fr.nir", germany);
        var active = RulePackLoader.SelectActive(packs, [], []).Select(rule => rule.Id);
        Assert.DoesNotContain("de.steuerid", active);
        Assert.DoesNotContain("gb.nhs", active);
    }

    private static string? Hit(string text, string ruleId)
    {
        var rule = BuiltinRules.LoadEmbedded().SelectMany(pack => pack.Rules).Single(item => item.Id == ruleId);
        var findings = new RuleScanner().Find("ID " + text + " end", [rule]);
        return findings.Count == 0 ? null : findings[0].RuleId;
    }
}
