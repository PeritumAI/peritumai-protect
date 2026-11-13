using System.Text.Json;

namespace Peritum.Protect.Core.Config;

public sealed class AllowListStore
{
    private readonly string _path;
    private readonly byte[] _key;
    private readonly Dictionary<string, string> _hints = new(StringComparer.Ordinal);

    public AllowListStore(string path, byte[]? key = null)
    {
        _path = path;
        _key = key ?? InstallKey.LoadOrCreate(path + ".key");
        Load();
    }

    public IReadOnlyCollection<string> Values => _hints.Keys;

    public IReadOnlyList<string> Hints => _hints.Values.OrderBy(value => value, StringComparer.Ordinal).ToList();

    public bool Contains(string value) => _hints.ContainsKey(Hash(value));

    public void Remove(string value)
    {
        var hash = _hints.ContainsKey(Hash(value)) ? Hash(value) : _hints.FirstOrDefault(pair => pair.Value == value).Key;
        if (!string.IsNullOrEmpty(hash) && _hints.Remove(hash))
            Save();
    }

    public void Clear()
    {
        _hints.Clear();
        Save();
    }

    public void Add(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 256)
            return;
        var hash = Hash(value);
        if (!_hints.TryAdd(hash, Hint(value)))
            return;
        Save();
    }

    private string Hash(string value) => Hashing.HmacText(value, _key);

    private static string Hint(string value)
    {
        if (value.Length <= 4)
            return new string('•', value.Length);
        return value[..2] + "…" + value[^2..];
    }

    private void Load()
    {
        if (!File.Exists(_path))
            return;
        try
        {
            var dto = JsonSerializer.Deserialize<AllowFile>(File.ReadAllText(_path), JsonDefaults.Indented);
            if (dto?.Values is null)
                return;
            var migrated = false;
            foreach (var value in dto.Values)
            {
                if (string.IsNullOrEmpty(value))
                    continue;
                if (value.Length == 64 && value.All(Uri.IsHexDigit))
                    _hints.TryAdd(value.ToLowerInvariant(), dto.Hints.GetValueOrDefault(value) ?? "saved value");
                else
                {
                    _hints[Hash(value)] = Hint(value);
                    migrated = true;
                }
            }
            if (migrated)
                Save();
        }
        catch
        {
            // A broken allow list must not stop the app. Start empty.
        }
    }

    private void Save()
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(new AllowFile
        {
            Values = _hints.Keys.OrderBy(value => value, StringComparer.Ordinal).ToList(),
            Hints = _hints
        }, JsonDefaults.Indented);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, json);
        if (File.Exists(_path))
            File.Replace(tmp, _path, null);
        else
            File.Move(tmp, _path);
    }

    private sealed class AllowFile
    {
        public List<string> Values { get; set; } = [];
        public Dictionary<string, string> Hints { get; set; } = [];
    }
}
