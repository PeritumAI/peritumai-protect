namespace Peritum.Protect.Core.Files;

/// <summary>
/// Writes a sibling file. Refuses the original path.
/// </summary>
public static class ProtectedCopyWriter
{
    public static string Write(string originalPath, byte[] redactedBytes, string? pattern, string? folder = null, string? extensionOverride = null)
    {
        if (string.IsNullOrWhiteSpace(originalPath))
            throw new ArgumentException("Original path is required.", nameof(originalPath));
        if (redactedBytes is null)
            throw new ArgumentNullException(nameof(redactedBytes));

        var originalFull = Path.GetFullPath(originalPath);
        var beside = Path.GetDirectoryName(originalFull)
            ?? throw new InvalidOperationException("Original path has no folder.");
        var directory = string.IsNullOrWhiteSpace(folder) ? beside : Path.GetFullPath(folder);
        var name = Path.GetFileNameWithoutExtension(originalFull);
        var ext = string.IsNullOrWhiteSpace(extensionOverride) ? Path.GetExtension(originalFull) : extensionOverride;
        var fileName = ApplyPattern(pattern, name, ext);
        var dest = Path.GetFullPath(Path.Combine(directory, fileName));
        dest = NextFree(directory, dest, originalFull);

        if (string.Equals(dest, originalFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to write the original path.");

        var before = Hashing.Sha256File(originalFull);
        File.WriteAllBytes(dest, redactedBytes);
        var after = Hashing.Sha256File(originalFull);
        if (!string.Equals(before, after, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(dest); } catch { /* the sibling is the only extra file */ }
            throw new InvalidOperationException("Original file hash changed.");
        }

        return dest;
    }

    public static string ApplyPattern(string? pattern, string name, string ext)
    {
        if (string.IsNullOrWhiteSpace(pattern) || !pattern.Contains("{name}", StringComparison.Ordinal))
            pattern = "{name}_protect{ext}";
        var file = pattern.Replace("{name}", name).Replace("{ext}", ext);
        file = Path.GetFileName(file);
        if (string.IsNullOrWhiteSpace(file) || file is "." or "..")
            file = name + "_protect" + ext;
        return file;
    }

    private static string NextFree(string directory, string dest, string originalFull)
    {
        if (!File.Exists(dest) && !string.Equals(dest, originalFull, StringComparison.OrdinalIgnoreCase))
            return dest;

        var stem = Path.GetFileNameWithoutExtension(dest);
        var ext = Path.GetExtension(dest);
        for (var n = 2; n < 10000; n++)
        {
            var candidate = Path.GetFullPath(Path.Combine(directory, $"{stem}_{n}{ext}"));
            if (string.Equals(candidate, originalFull, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!File.Exists(candidate))
                return candidate;
        }

        throw new IOException("Could not find a free name for the protected copy.");
    }
}
