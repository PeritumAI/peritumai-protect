namespace Peritum.Protect.Core.Files;

public sealed record CopyResult
{
    public string? OutputPath { get; init; }
    public string OriginalHashBefore { get; init; } = "";
    public string OriginalHashAfter { get; init; } = "";
    public string? Error { get; init; }
    public string? Note { get; init; }
    public string? SidecarPath { get; init; }
    public bool VerificationRefused { get; init; }
    public bool Reversible { get; init; }
    public string? MapId { get; init; }
    public bool OriginalUnchanged =>
        OriginalHashBefore.Length > 0 &&
        string.Equals(OriginalHashBefore, OriginalHashAfter, StringComparison.OrdinalIgnoreCase);

    public static CopyResult Fail(string error, string hashBefore) => new()
    {
        Error = error,
        OriginalHashBefore = hashBefore,
        OriginalHashAfter = hashBefore
    };
}
