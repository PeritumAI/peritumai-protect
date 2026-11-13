namespace Peritum.Protect.Core.Rules;

public sealed class RulePack
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string? Description { get; set; }
    public string? Author { get; set; }
    public bool Enabled { get; set; } = true;
    public List<Rule> Rules { get; set; } = [];
}
