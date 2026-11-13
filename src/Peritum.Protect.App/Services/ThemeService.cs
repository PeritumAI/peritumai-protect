using System.Collections;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;
using Microsoft.Win32;

namespace Peritum.Protect.App.Services;

public enum ThemeChoice
{
    System,
    Light,
    Dark
}

public sealed class ThemeService : IDisposable
{
    public const string LightThemeKey = "AppsUseLightTheme";

    private ResourceDictionary? _applied;
    private bool _listening;
    private bool? _forceHighContrast;
    private readonly List<object> _installedKeys = [];

    public static ThemeService? Active { get; private set; }
    public event Action? Changed;

    public ThemeChoice Choice { get; private set; } = ThemeChoice.System;
    public bool HighContrast { get; private set; }
    public bool IsDark { get; private set; }

    public void Apply(string? choice) => Apply(choice, null);

    /// <param name="forceHighContrast">When set, ignores the Windows high-contrast flag.</param>
    public void Apply(string? choice, bool? forceHighContrast)
    {
        Choice = Parse(choice);
        _forceHighContrast = forceHighContrast;
        ApplyNow();
        Listen();
    }

    public void Dispose()
    {
        if (_listening)
        {
            SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
            _listening = false;
        }
    }

    public static ThemeChoice Parse(string? choice) => choice?.Trim().ToLowerInvariant() switch
    {
        "light" => ThemeChoice.Light,
        "dark" => ThemeChoice.Dark,
        _ => ThemeChoice.System
    };

    public static bool SystemPrefersLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue(LightThemeKey);
            return value is int number ? number != 0 : true;
        }
        catch
        {
            return true;
        }
    }

    private void Listen()
    {
        if (_listening)
            return;
        SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
        _listening = true;
    }

    private void OnPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (Choice != ThemeChoice.System && e.Category != UserPreferenceCategory.Accessibility)
            return;
        var app = Application.Current;
        if (app is null)
            return;
        app.Dispatcher.BeginInvoke(ApplyNow);
    }

    public static void BindWindow(Window window)
    {
        window.SetResourceReference(Window.BackgroundProperty, "Background");
        window.SetResourceReference(Window.ForegroundProperty, "Fg");
        window.SourceInitialized -= OnSourceInitialized;
        window.SourceInitialized += OnSourceInitialized;
        ApplyTitleBar(window);
    }

    private void ApplyNow()
    {
        var app = Application.Current;
        if (app is null)
            return;
        Active = this;
        HighContrast = _forceHighContrast ?? SystemParameters.HighContrast;
        IsDark = !HighContrast && Choice switch
        {
            ThemeChoice.Light => false,
            ThemeChoice.Dark => true,
            _ => !SystemPrefersLight()
        };
        var path = HighContrast
            ? "Themes/HighContrast.xaml"
            : IsDark ? "Themes/Dark.xaml" : "Themes/Light.xaml";
        var next = new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Peritum.Protect;component/" + path, UriKind.Absolute)
        };
        if (_applied is not null)
            app.Resources.MergedDictionaries.Remove(_applied);
        app.Resources.MergedDictionaries.Add(next);
        _applied = next;

        var nextKeys = new List<object>();
        foreach (DictionaryEntry entry in next)
        {
            var value = entry.Value is Freezable freezable ? freezable.Clone() : entry.Value;
            app.Resources[entry.Key] = value;
            nextKeys.Add(entry.Key);
        }
        foreach (var key in _installedKeys)
        {
            if (!nextKeys.Contains(key))
                app.Resources.Remove(key);
        }
        _installedKeys.Clear();
        _installedKeys.AddRange(nextKeys);

        foreach (Window window in app.Windows)
        {
            BindWindow(window);
            ApplyTitleBar(window);
        }
        Changed?.Invoke();
    }

    private static void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (sender is Window window)
            ApplyTitleBar(window);
    }

    private static void ApplyTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        var dark = Active is { IsDark: true, HighContrast: false };
        var value = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(hwnd, 20, ref value, sizeof(int));
        _ = DwmSetWindowAttribute(hwnd, 19, ref value, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
