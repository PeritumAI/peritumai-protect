namespace Peritum.Protect.App.Services;

public static class ExecutablePathResolver
{
    public static string Resolve(string baseDirectory, string? processPath)
    {
        var published = Path.Combine(baseDirectory, "Peritum.Protect.exe");
        if (File.Exists(published))
            return published;

        if (!string.IsNullOrWhiteSpace(processPath) &&
            !string.Equals(Path.GetFileName(processPath), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        throw new InvalidOperationException(
            "The Explorer menu is available from the published app. It cannot be added while running through the .NET SDK.");
    }
}
