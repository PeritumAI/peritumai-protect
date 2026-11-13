using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Peritum.Protect.Core.Config;

/// <summary>
/// Sites I use, encrypted for the current user. An older HMAC entry stays an allow-list match only.
/// </summary>
public sealed class TrustedSiteStore
{
    private readonly string _path;
    private readonly byte[] _key;
    private readonly HashSet<string> _domains = new(StringComparer.Ordinal);
    private readonly HashSet<string> _legacy = new(StringComparer.Ordinal);

    public TrustedSiteStore(string path)
    {
        _path = path;
        _key = InstallKey.LoadOrCreate(path + ".key");
        Load();
    }

    public IReadOnlyList<string> Domains => _domains.OrderBy(value => value, StringComparer.Ordinal).ToList();

    public int LegacyCount => _legacy.Count;

    public bool Contains(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var domain = value.Trim().ToLowerInvariant();
        if (_domains.Contains(domain))
            return true;
        return _legacy.Count > 0 && _legacy.Contains(Hash(domain));
    }

    public bool Add(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var domain = value.Trim().ToLowerInvariant();
        if (domain.Length > 256)
            return false;
        var dropped = _legacy.Remove(Hash(domain));
        if (!_domains.Add(domain))
        {
            if (dropped)
                Save();
            return false;
        }

        Save();
        return true;
    }

    public void Remove(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        if (_domains.Remove(value.Trim().ToLowerInvariant()))
            Save();
    }

    public void RemoveLegacy()
    {
        if (_legacy.Count == 0)
            return;
        _legacy.Remove(_legacy.First());
        Save();
    }

    private string Hash(string value) => Hashing.HmacText(value, _key);

    private void Load()
    {
        if (!File.Exists(_path))
            return;
        try
        {
            var bytes = File.ReadAllBytes(_path);
            if (bytes.Length == 0)
                return;
            if (LooksLikeJson(bytes))
            {
                ReadLegacy(bytes);
                Save();
                return;
            }

            var json = Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser));
            var file = JsonSerializer.Deserialize<SiteFile>(json, JsonDefaults.Compact);
            if (file is null)
                return;
            foreach (var domain in file.Domains ?? [])
            {
                if (!string.IsNullOrWhiteSpace(domain))
                    _domains.Add(domain.Trim().ToLowerInvariant());
            }

            foreach (var hash in file.Legacy ?? [])
            {
                if (IsHash(hash))
                    _legacy.Add(hash.ToLowerInvariant());
            }
        }
        catch
        {
            // A broken site list must not stop the app. Start empty.
        }
    }

    private void ReadLegacy(byte[] bytes)
    {
        var file = JsonSerializer.Deserialize<LegacyFile>(Encoding.UTF8.GetString(bytes), JsonDefaults.Indented);
        if (file?.Values is null)
            return;
        foreach (var value in file.Values)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;
            if (IsHash(value))
                _legacy.Add(value.ToLowerInvariant());
            else
                _domains.Add(value.Trim().ToLowerInvariant());
        }
    }

    private static bool IsHash(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static bool LooksLikeJson(byte[] bytes)
    {
        var i = 0;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            i = 3;
        while (i < bytes.Length && bytes[i] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
            i++;
        return i < bytes.Length && bytes[i] == (byte)'{';
    }

    private void Save()
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(new SiteFile
        {
            Domains = _domains.OrderBy(value => value, StringComparer.Ordinal).ToList(),
            Legacy = _legacy.OrderBy(value => value, StringComparer.Ordinal).ToList()
        }, JsonDefaults.Compact);
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);
        var tmp = _path + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        if (File.Exists(_path))
            File.Replace(tmp, _path, null);
        else
            File.Move(tmp, _path);
    }

    private sealed class SiteFile
    {
        public List<string>? Domains { get; set; }
        public List<string>? Legacy { get; set; }
    }

    private sealed class LegacyFile
    {
        public List<string>? Values { get; set; }
    }
}
