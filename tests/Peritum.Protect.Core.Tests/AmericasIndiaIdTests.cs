using Peritum.Protect.Core.Rules;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Tests;

public class AmericasIndiaIdTests
{
    [Fact]
    public void Each_americas_and_india_id_accepts_a_synthetic_number_and_rejects_a_bad_one()
    {
        var cpf = NationalId.WithCpf("100000001");
        var cnpj = NationalId.WithCnpj("100000000001");
        var aadhaar = NationalId.WithAadhaar("23456789012");
        Assert.Equal(11, cpf.Length);
        Assert.Equal(14, cnpj.Length);
        Assert.Equal(12, aadhaar.Length);
        Assert.StartsWith("2", aadhaar);

        Assert.Equal("us.ssn", Hit("211-11-1111", "us.ssn"));
        Assert.Equal("us.ssn", Hit("SSN 211111111", "us.ssn"));
        Assert.Equal("us.itin", Hit("900-70-0001", "us.itin"));
        Assert.Equal("br.cpf", Hit("CPF " + cpf, "br.cpf"));
        Assert.Equal("br.cnpj", Hit("CNPJ " + cnpj, "br.cnpj"));
        Assert.Equal("in.aadhaar", Hit("Aadhaar " + aadhaar, "in.aadhaar"));
        Assert.Equal("in.pan", Hit("ABCPE1234F", "in.pan"));

        Assert.Null(Hit("000-12-1234", "us.ssn"));
        Assert.Null(Hit("666-12-1234", "us.ssn"));
        Assert.Null(Hit("211-00-1111", "us.ssn"));
        Assert.Null(Hit("211-11-0000", "us.ssn"));
        Assert.Null(Hit("900-12-1234", "us.ssn"));
        Assert.Equal("us.itin", Hit("900-70-0001", "us.itin"));
        Assert.Null(Hit("900-12-1234", "us.itin"));
        Assert.Null(Hit("(202) 555-0143", "us.ssn"));
        Assert.Null(Hit("+1 202 555 0100", "us.ssn"));
        Assert.Null(Hit("11111111111", "br.cpf"));
        Assert.Null(Hit(Break(cpf), "br.cpf"));
        Assert.Null(Hit(Break(cnpj), "br.cnpj"));
        Assert.Null(Hit(Break(aadhaar), "in.aadhaar"));
        Assert.Null(Hit("123456789012", "in.aadhaar"));
        Assert.Null(Hit("ABCEX1234F", "in.pan"));
    }

    [Theory]
    [InlineData("2026-09-30")]
    [InlineData("28.09.2026")]
    [InlineData("20260928")]
    [InlineData("Order #12345678")]
    [InlineData("Invoice 2026-0912-4411")]
    [InlineData("+381 64 000 0001")]
    [InlineData("+49 151 00000000")]
    [InlineData("+44 20 7946 0000")]
    public void Dates_orders_invoices_and_foreign_phones_are_not_these_ids(string text)
    {
        var ids = new[] { "us.ssn", "us.itin", "br.cpf", "br.cnpj", "in.aadhaar", "in.pan" };
        foreach (var id in ids)
            Assert.Null(Hit(text, id));
    }

    [Fact]
    public void The_united_states_turns_on_for_region_us()
    {
        var packs = BuiltinRules.LoadEmbedded();
        var unitedStates = CountryRegions.RuleIds(packs, "US");
        Assert.Contains("us.ssn", unitedStates);
        Assert.Contains("us.itin", unitedStates);
        Assert.DoesNotContain("br.cpf", unitedStates);
        Assert.Contains("in.aadhaar", CountryRegions.RuleIds(packs, "IN"));
    }

    private static string? Hit(string text, string ruleId)
    {
        var rule = BuiltinRules.LoadEmbedded().SelectMany(pack => pack.Rules).Single(item => item.Id == ruleId);
        var findings = new RuleScanner().Find("ID " + text + " end", [rule]);
        return findings.Count == 0 ? null : findings[0].RuleId;
    }

    private static string Break(string value)
    {
        var chars = value.ToCharArray();
        chars[^1] = chars[^1] == '0' ? '1' : '0';
        return new string(chars);
    }
}
