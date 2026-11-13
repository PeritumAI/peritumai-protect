using Microsoft.Win32;

namespace Peritum.Protect.Core.Config;

public enum PolicyValueKind
{
    Dword,
    String,
    MultiString,
    Unsupported
}

public sealed class PolicyRegistryValue
{
    public required string Name { get; init; }
    public required PolicyValueKind Kind { get; init; }
    public int Number { get; init; }
    public string? Text { get; init; }
    public IReadOnlyList<string> Lines { get; init; } = [];
}

public sealed class PolicyRegistrySnapshot
{
    public IReadOnlyList<PolicyRegistryValue> Values { get; init; } = [];
}

public readonly struct PolicyRegistryRead
{
    public PolicyRegistrySnapshot? Snapshot { get; init; }
    public bool Unreadable { get; init; }

    public static PolicyRegistryRead None { get; } = new();

    public static PolicyRegistryRead Broken { get; } = new() { Unreadable = true };
}

public interface IPolicyRegistry
{
    PolicyRegistryRead Read();
}

public sealed class EmptyPolicyRegistry : IPolicyRegistry
{
    public static EmptyPolicyRegistry Instance { get; } = new();

    public PolicyRegistryRead Read() => PolicyRegistryRead.None;
}

/// <summary>Reads HKLM policy. Never writes. A missing key is not an error.</summary>
public sealed class WindowsPolicyRegistry : IPolicyRegistry
{
    public PolicyRegistryRead Read()
    {
        try
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = hive.OpenSubKey(PolicyPaths.RegistrySubKey, writable: false);
            if (key is null)
                return PolicyRegistryRead.None;

            var values = new List<PolicyRegistryValue>();
            foreach (var name in key.GetValueNames())
            {
                if (string.IsNullOrEmpty(name))
                    continue;
                values.Add(Map(name, key.GetValueKind(name), key.GetValue(name)));
            }

            return new PolicyRegistryRead
            {
                Snapshot = new PolicyRegistrySnapshot { Values = values }
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return PolicyRegistryRead.Broken;
        }
    }

    private static PolicyRegistryValue Map(string name, RegistryValueKind kind, object? raw) => kind switch
    {
        RegistryValueKind.DWord when raw is int number => new()
        {
            Name = name,
            Kind = PolicyValueKind.Dword,
            Number = number
        },
        RegistryValueKind.String when raw is string text => new()
        {
            Name = name,
            Kind = PolicyValueKind.String,
            Text = text
        },
        RegistryValueKind.MultiString when raw is string[] lines => new()
        {
            Name = name,
            Kind = PolicyValueKind.MultiString,
            Lines = lines
        },
        _ => new()
        {
            Name = name,
            Kind = PolicyValueKind.Unsupported
        }
    };
}
