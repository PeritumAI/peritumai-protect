using System.Security.Cryptography;

namespace Peritum.Protect.Core.Config;

public static class InstallKey
{
    public static byte[] LoadOrCreate(string path)
    {
        if (File.Exists(path))
        {
            try
            {
                return ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
            }
            catch (CryptographicException)
            {
                // The old key cannot be read. A new key starts a new hash series.
            }
        }

        var key = RandomNumberGenerator.GetBytes(32);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllBytes(path, ProtectedData.Protect(key, null, DataProtectionScope.CurrentUser));
        return key;
    }
}
