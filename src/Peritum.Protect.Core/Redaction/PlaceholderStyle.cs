namespace Peritum.Protect.Core.Redaction;

public static class PlaceholderStyle
{
    public static string Wrap(string? placeholder)
    {
        var token = (placeholder ?? "").Trim().Trim('[', ']');
        if (token.Length == 0)
            token = "REDACTED";
        return "[" + token + "]";
    }
}
