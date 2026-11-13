using System.Security.Cryptography;
using System.Text;

namespace Peritum.Protect.Core;

public static class Hashing
{
    public static string Sha256Hex(ReadOnlySpan<byte> data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string Sha256Text(string text) =>
        Sha256Hex(Encoding.UTF8.GetBytes(text));

    public static string HmacText(string text, byte[] key)
    {
        var mac = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(mac).ToLowerInvariant();
    }

    public static string Sha256File(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
