using System.Text.Json.Serialization;

namespace Peritum.Protect.Core.Scanning;

public sealed class Finding
{
    public required string RuleId { get; init; }
    public required string Label { get; init; }
    public string DisplayLabel { get; init; } = "";
    public string PluralLabel { get; init; } = "";
    public Severity Severity { get; init; }
    public int Start { get; init; }
    public int Length { get; init; }
    /// <summary>Token without brackets, for example EMAIL or CUSTOM:ClientNames.</summary>
    public required string Placeholder { get; init; }
    public string Category { get; init; } = "";
    /// <summary>Truncated matched text for the UI. Not written to the event log.</summary>
    public required string Snippet { get; init; }
    /// <summary>Where the hit sits, in plain words. Kept in memory for the open result. Not written to the data folder.</summary>
    [JsonIgnore]
    public string Location { get; init; } = "";
    /// <summary>About 40 characters around the hit, with the value masked. Memory only while the result is open.</summary>
    [JsonIgnore]
    public string Context { get; init; } = "";

    /// <summary>Registrable domain for a link finding. Memory only.</summary>
    [JsonIgnore]
    public string LinkDomain { get; init; } = "";

    /// <summary>Real destination for a link finding. Memory only.</summary>
    [JsonIgnore]
    public string LinkAddress { get; init; } = "";

    public Finding Shift(int delta) => Copy(Start + delta, Location, Context);

    public Finding Located(string location, string context) => Copy(Start, location, context);

    private Finding Copy(int start, string location, string context) => new()
    {
        RuleId = RuleId,
        Label = Label,
        DisplayLabel = DisplayLabel,
        PluralLabel = PluralLabel,
        Severity = Severity,
        Start = start,
        Length = Length,
        Placeholder = Placeholder,
        Category = Category,
        Snippet = Snippet,
        Location = location,
        Context = context,
        LinkDomain = LinkDomain,
        LinkAddress = LinkAddress
    };
}
