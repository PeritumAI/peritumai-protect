using Peritum.Protect.Core;
using Peritum.Protect.Core.Rules;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Tests;

public class BalkansIdTests
{
    [Fact]
    public void Known_synthetic_checks_match_the_published_algorithms()
    {
        Assert.Equal("0101990710008", NationalId.WithJmbgCheck("010199071000"));
        Assert.Equal("100000016", NationalId.WithPibCheck("10000001"));
        Assert.Equal("10000000000", NationalId.WithOibCheck("1000000000"));
    }

    [Fact]
    public void Each_balkan_id_accepts_a_synthetic_number_and_rejects_a_bad_check()
    {
        var jmbg = Must(NationalId.WithJmbgCheck("010199071000"));
        var pib = NationalId.WithPibCheck("10000001");
        var oib = NationalId.WithOibCheck("1000000000");
        var emso = Must(NationalId.WithJmbgCheck("010199050000"));
        var bosnian = Must(NationalId.WithJmbgCheck("020200017500"));
        var montenegrin = Must(NationalId.WithJmbgCheck("150698021001"));
        var embg = Must(NationalId.WithJmbgCheck("010199041000"));

        Assert.Equal("pii.jmbg", Hit(jmbg, "pii.jmbg"));
        Assert.Equal("rs.pib", Hit("PIB " + pib, "rs.pib"));
        Assert.Equal("hr.oib", Hit("OIB " + oib, "hr.oib"));
        Assert.Equal("si.emso", Hit(emso, "si.emso"));
        Assert.Equal("ba.jmbg", Hit(bosnian, "ba.jmbg"));
        Assert.Equal("me.jmbg", Hit(montenegrin, "me.jmbg"));
        Assert.Equal("mk.embg", Hit(embg, "mk.embg"));
        Assert.Equal("rs.phone", Hit("+381 64 000 0001", "rs.phone"));
        Assert.Equal("rs.phone", Hit("064/000-0001", "rs.phone"));

        Assert.Null(Hit(Break(jmbg), "pii.jmbg"));
        Assert.Null(Hit(Break(pib), "rs.pib"));
        Assert.Null(Hit(Break(oib), "hr.oib"));
        Assert.Null(Hit("3201990710006", "pii.jmbg"));
        Assert.Null(Hit(jmbg, "ba.jmbg"));
        Assert.Null(Hit(bosnian, "si.emso"));
    }

    [Theory]
    [InlineData("2026-09-30")]
    [InlineData("28.09.2026")]
    [InlineData("01011990")]
    [InlineData("20260928")]
    [InlineData("Order #12345678")]
    [InlineData("Ref 2026-0912-4411")]
    [InlineData("Invoice 2026-0912-4411")]
    [InlineData("+49 151 00000000")]
    [InlineData("+33 6 00 00 00 00")]
    [InlineData("+1 202 555 0100")]
    public void Dates_orders_invoices_and_foreign_phones_are_not_balkan_ids(string text)
    {
        var ids = new[] { "pii.jmbg", "rs.pib", "rs.phone", "hr.oib", "si.emso", "ba.jmbg", "me.jmbg", "mk.embg" };
        foreach (var id in ids)
            Assert.Null(Hit(text, id));
    }

    [Fact]
    public void Region_turns_a_country_on_and_the_ui_language_does_not()
    {
        var packs = BuiltinRules.LoadEmbedded();
        var serbia = CountryRegions.RuleIds(packs, "RS");
        Assert.Contains("rs.pib", serbia);
        Assert.Contains("rs.phone", serbia);
        Assert.DoesNotContain("hr.oib", serbia);
        Assert.Equal("GB", CountryRegions.Canonical("UK"));

        var active = RulePackLoader.SelectActive(packs, CountryRegions.RuleIds(packs, "HR"), []);
        var ids = active.Select(rule => rule.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("hr.oib", ids);
        Assert.Contains("pii.jmbg", ids);
        Assert.DoesNotContain("rs.pib", ids);

        TextCatalog.Use("de");
        try
        {
            Assert.Equal("en", TextCatalog.Language);
            var still = RulePackLoader.SelectActive(packs, [], []).Select(rule => rule.Id);
            Assert.Contains("pii.jmbg", still);
            Assert.DoesNotContain("hr.oib", still);
        }
        finally
        {
            TextCatalog.Use("en");
        }
    }

    [Fact]
    public void A_bosnian_number_keeps_the_bosnian_rule_when_both_jmbg_rules_are_on()
    {
        var number = Must(NationalId.WithJmbgCheck("020200017500"));
        var rules = BuiltinRules.LoadEmbedded().SelectMany(pack => pack.Rules)
            .Where(rule => rule.Id is "pii.jmbg" or "ba.jmbg")
            .ToList();
        var findings = new RuleScanner().Find(number, rules);
        var hit = Assert.Single(findings);
        Assert.Equal("ba.jmbg", hit.RuleId);
    }

    private static string? Hit(string text, string ruleId)
    {
        var rule = BuiltinRules.LoadEmbedded().SelectMany(pack => pack.Rules).Single(item => item.Id == ruleId);
        var findings = new RuleScanner().Find("ID " + text + " end", [rule]);
        return findings.Count == 0 ? null : findings[0].RuleId;
    }

    private static string Must(string? value) => value ?? throw new InvalidOperationException("no checksum");

    private static string Break(string value)
    {
        var chars = value.ToCharArray();
        chars[^1] = chars[^1] == '0' ? '1' : '0';
        return new string(chars);
    }
}
