namespace Peritum.Protect.Core.Links;

public enum LinkScope
{
    Document,
    Clipboard
}

public enum LinkSeverity
{
    Low = 1,
    Medium = 2,
    High = 3
}

public static class LinkCodes
{
    public const string Lookalike = "lookalike";
    public const string BrandElsewhere = "brand_elsewhere";
    public const string UserinfoTrick = "userinfo_trick";
    public const string DangerousScheme = "dangerous_scheme";
    public const string IpHost = "ip_host";
    public const string RiskyDownload = "risky_download";
    public const string Shortener = "shortener";
    public const string OddPort = "odd_port";
    public const string TextMismatch = "text_mismatch";
    public const string SenderMismatch = "sender_mismatch";
    public const string ReplyToDiffers = "reply_to_differs";
}

public sealed record LinkFlag(string Code, LinkSeverity Severity);

public sealed class InspectedLink
{
    public required string Raw { get; init; }
    public required string Url { get; init; }
    public required string Scheme { get; init; }
    public required string Host { get; init; }
    public required string UserInfo { get; init; }
    public required string Path { get; init; }
    public int? Port { get; init; }
    public string? RegistrableDomain { get; init; }
    public string? VisibleText { get; init; }
    public string? ShownDomain { get; init; }
    public string? BrandName { get; init; }
    public required IReadOnlyList<LinkFlag> Flags { get; init; }

    public bool Warns => Flags.Any(flag => flag.Severity >= LinkSeverity.Medium);

    public bool Has(string code) => Flags.Any(flag => flag.Code == code);

    public LinkFlag? Flag(string code) => Flags.FirstOrDefault(flag => flag.Code == code);
}
