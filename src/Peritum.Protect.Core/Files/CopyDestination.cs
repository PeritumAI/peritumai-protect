namespace Peritum.Protect.Core.Files;

public static class CopyDestination
{
    public const string FallbackNote = "That folder is not available, so the copy was saved next to the original.";

    public static (string? Folder, string? Note) Resolve(string? mode, string? folder)
    {
        if (!string.Equals(mode, "folder", StringComparison.OrdinalIgnoreCase))
            return (null, null);
        if (string.IsNullOrWhiteSpace(folder) || !CanWrite(folder))
            return (null, FallbackNote);
        return (Path.GetFullPath(folder), null);
    }

    private static bool CanWrite(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
                return false;
            var probe = Path.Combine(folder, ".peritum-write-" + Guid.NewGuid().ToString("n"));
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
