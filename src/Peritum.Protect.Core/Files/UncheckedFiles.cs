namespace Peritum.Protect.Core.Files;

public static class UncheckedFiles
{
    public const string PasswordProtected = "Not checked. This file is password-protected, so it cannot be checked.";
    public const string Corrupt = "Not checked. This file could not be read, so it was not checked.";
    public const string LegacyType = "Not checked. This file type cannot be checked in this version.";

    private static readonly string[] Legacy =
    [
        ".doc", ".xls", ".ppt", ".rtf", ".odt", ".msg"
    ];

    public static bool IsLegacy(string extension) =>
        Legacy.Contains(extension, StringComparer.OrdinalIgnoreCase);

    public static string? Explain(string path)
    {
        var extension = Path.GetExtension(path);
        if (Legacy.Contains(extension, StringComparer.OrdinalIgnoreCase))
            return LegacyType;

        if (!File.Exists(path))
            return null;

        if (extension.Equals(".docx", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".docm", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".xlsm", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".pptx", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".pptm", StringComparison.OrdinalIgnoreCase))
        {
            var header = Header(path, 8);
            if (IsCfb(header))
                return ContainsUtf16(path, "EncryptionInfo") ? PasswordProtected : Corrupt;
            if (!IsZip(header))
                return Corrupt;
        }

        if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            var header = Header(path, 5);
            if (header.Length < 5 || header[0] != (byte)'%' || header[1] != (byte)'P' || header[2] != (byte)'D' || header[3] != (byte)'F')
                return Corrupt;
            if (ContainsAscii(path, "/Encrypt"))
                return PasswordProtected;
        }

        return null;
    }

    public static string? ReasonCode(string? message)
    {
        if (message == PasswordProtected)
            return "unchecked.password";
        if (message == Corrupt)
            return "unchecked.unreadable";
        if (message == LegacyType)
            return "unchecked.legacy";
        return null;
    }

    private static bool IsCfb(byte[] header) =>
        header.Length >= 8 &&
        header[0] == 0xD0 && header[1] == 0xCF && header[2] == 0x11 && header[3] == 0xE0 &&
        header[4] == 0xA1 && header[5] == 0xB1 && header[6] == 0x1A && header[7] == 0xE1;

    private static bool IsZip(byte[] header) =>
        header.Length >= 2 && header[0] == (byte)'P' && header[1] == (byte)'K';

    private static byte[] Header(string path, int count)
    {
        var buffer = new byte[count];
        using var stream = File.OpenRead(path);
        var read = stream.Read(buffer, 0, count);
        return read == count ? buffer : buffer[..read];
    }

    private static bool ContainsUtf16(string path, string text)
    {
        var needle = System.Text.Encoding.Unicode.GetBytes(text);
        return Contains(File.ReadAllBytes(path), needle);
    }

    private static bool ContainsAscii(string path, string text)
    {
        var needle = System.Text.Encoding.ASCII.GetBytes(text);
        return Contains(File.ReadAllBytes(path), needle);
    }

    private static bool Contains(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length)
            return false;
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var same = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    same = false;
                    break;
                }
            }
            if (same)
                return true;
        }
        return false;
    }
}
