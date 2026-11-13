using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Peritum.Protect.Core.Config;

public sealed class PolicyFileWatcher : IDisposable
{
    private readonly string _filePath;
    private FileSystemWatcher? _watcher;
    private int _disposed;

    public PolicyFileWatcher(string filePath) => _filePath = filePath;

    public event Action? Changed;

    public void Start()
    {
        if (Volatile.Read(ref _disposed) == 1 || _watcher is not null)
            return;

        var full = Path.GetFullPath(_filePath);
        var directory = WatchDirectory(full, out var includeChildren);
        if (directory is null)
            return;

        var watcher = new FileSystemWatcher(directory)
        {
            Filter = Path.GetFileName(full),
            IncludeSubdirectories = includeChildren,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size
        };
        watcher.Changed += OnEvent;
        watcher.Created += OnEvent;
        watcher.Deleted += OnEvent;
        watcher.Renamed += (_, args) => OnPath(args.FullPath);
        watcher.Error += (_, _) => Changed?.Invoke();
        watcher.EnableRaisingEvents = true;
        _watcher = watcher;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;
        _watcher?.Dispose();
        _watcher = null;
    }

    private void OnEvent(object sender, FileSystemEventArgs args) => OnPath(args.FullPath);

    private void OnPath(string path)
    {
        if (Volatile.Read(ref _disposed) == 1)
            return;
        if (!PathsMatch(path, _filePath))
            return;
        Changed?.Invoke();
    }

    private static bool PathsMatch(string left, string right) =>
        Path.GetFullPath(left).Equals(Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static string? WatchDirectory(string filePath, out bool includeChildren)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (directory is not null && Directory.Exists(directory))
        {
            includeChildren = false;
            return directory;
        }

        includeChildren = true;
        while (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            directory = Path.GetDirectoryName(directory);
        return directory;
    }
}

public sealed class PolicyRegistryWatcher : IDisposable
{
    private const uint ChangeName = 0x00000001;
    private const uint ChangeLastSet = 0x00000004;

    private readonly ManualResetEvent _stop = new(false);
    private Thread? _thread;
    private int _disposed;

    public event Action? Changed;

    public void Start()
    {
        if (Volatile.Read(ref _disposed) == 1 || _thread is not null)
            return;
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "Peritum policy registry"
        };
        _thread.Start();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;
        _stop.Set();
        _thread?.Join(TimeSpan.FromSeconds(2));
        _stop.Dispose();
    }

    private void Loop()
    {
        while (Volatile.Read(ref _disposed) == 0)
        {
            try
            {
                if (!WaitForChange())
                    return;
                if (Volatile.Read(ref _disposed) == 0)
                    Changed?.Invoke();
            }
            catch (Exception)
            {
                if (Volatile.Read(ref _disposed) == 1)
                    return;
                try
                {
                    if (_stop.WaitOne(TimeSpan.FromSeconds(5)))
                        return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
            }
        }
    }

    private bool WaitForChange()
    {
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = hive.OpenSubKey(@"Software\Policies", writable: false)
            ?? hive.OpenSubKey("Software", writable: false);
        if (key is null)
            return !_stop.WaitOne(TimeSpan.FromSeconds(5));

        using var notify = new AutoResetEvent(false);
        var error = RegNotifyChangeKeyValue(key.Handle, true, ChangeName | ChangeLastSet, notify.SafeWaitHandle, true);
        if (error != 0)
            return !_stop.WaitOne(TimeSpan.FromSeconds(5));

        var signaled = WaitHandle.WaitAny([notify, _stop], TimeSpan.FromSeconds(30));
        if (signaled == 1)
            return false;
        return signaled == 0;
    }

    [DllImport("advapi32.dll", ExactSpelling = true)]
    private static extern int RegNotifyChangeKeyValue(
        SafeRegistryHandle hKey,
        [MarshalAs(UnmanagedType.Bool)] bool bWatchSubtree,
        uint dwNotifyFilter,
        SafeWaitHandle hEvent,
        [MarshalAs(UnmanagedType.Bool)] bool fAsynchronous);
}

public sealed class PolicyChangeWatcher : IDisposable
{
    private readonly PolicyFileWatcher _files;
    private readonly PolicyRegistryWatcher _registry;
    private readonly object _gate = new();
    private Timer? _debounce;
    private int _disposed;

    public PolicyChangeWatcher(string filePath)
    {
        _files = new PolicyFileWatcher(filePath);
        _registry = new PolicyRegistryWatcher();
        _files.Changed += Schedule;
        _registry.Changed += Schedule;
    }

    public event Action? Changed;

    public void Start()
    {
        _files.Start();
        _registry.Start();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;
        _files.Dispose();
        _registry.Dispose();
        lock (_gate)
        {
            _debounce?.Dispose();
            _debounce = null;
        }
    }

    private void Schedule()
    {
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) == 1)
                return;
            _debounce?.Dispose();
            _debounce = new Timer(_ =>
            {
                if (Volatile.Read(ref _disposed) == 1)
                    return;
                Changed?.Invoke();
            }, null, 250, Timeout.Infinite);
        }
    }
}
