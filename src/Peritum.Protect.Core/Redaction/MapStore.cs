using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Peritum.Protect.Core.Redaction;

public enum MapStatus
{
    Found,
    Missing,
    Expired
}

public readonly record struct MapLoadResult(MapStatus Status, RedactionMap? Map)
{
    public static MapLoadResult Ok(RedactionMap map) => new(MapStatus.Found, map);

    public static MapLoadResult Missing() => new(MapStatus.Missing, null);

    public static MapLoadResult Expired() => new(MapStatus.Expired, null);
}

public sealed class MapStore
{
    private readonly string _directory;

    public MapStore(string directory) => _directory = directory;

    public void Save(RedactionMap map, DateTimeOffset? created = null)
    {
        Directory.CreateDirectory(_directory);
        var json = JsonSerializer.Serialize(new StoredMap
        {
            Id = map.Id,
            Created = created ?? DateTimeOffset.UtcNow,
            Tokens = map.Tokens
        });
        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(Path.Combine(_directory, map.Id + ".map"), protectedBytes);
    }

    public RedactionMap? Load(string id) => TryLoad(id, null).Map;

    public MapLoadResult TryLoad(string id, TimeSpan? maxAge)
    {
        var path = Path.Combine(_directory, id + ".map");
        if (!File.Exists(path))
            return MapLoadResult.Missing();
        try
        {
            var json = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser));
            var stored = JsonSerializer.Deserialize<StoredMap>(json);
            if (stored is null)
                return MapLoadResult.Missing();
            if (maxAge is not null && DateTimeOffset.UtcNow - stored.Created > maxAge.Value)
                return MapLoadResult.Expired();
            var map = new RedactionMap { Id = string.IsNullOrWhiteSpace(stored.Id) ? id : stored.Id };
            foreach (var pair in stored.Tokens)
                map.Tokens[pair.Key] = pair.Value;
            return MapLoadResult.Ok(map);
        }
        catch (Exception)
        {
            return MapLoadResult.Missing();
        }
    }

    public int DeleteOlderThan(TimeSpan age)
    {
        if (!Directory.Exists(_directory))
            return 0;
        var removed = 0;
        foreach (var file in Directory.GetFiles(_directory, "*.map"))
        {
            if (DateTime.UtcNow - File.GetCreationTimeUtc(file) > age)
            {
                File.Delete(file);
                removed++;
            }
        }
        return removed;
    }

    private sealed class StoredMap
    {
        public string Id { get; set; } = "";
        public DateTimeOffset Created { get; set; }
        public Dictionary<string, string> Tokens { get; set; } = [];
    }
}
