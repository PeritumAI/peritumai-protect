using System.Diagnostics;
using System.Reflection;
using System.Windows.Navigation;
using Peritum.Protect.Core;

namespace Peritum.Protect.App.Views;

public partial class HelpView
{
    public static Func<DateTime> Today { get; set; } = () => DateTime.Today;

    public HelpView()
    {
        InitializeComponent();
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version?.ToString() ?? "1.0.0";
        var builtOn = BuildDate();
        VersionLine.Text = TextCatalog.Format("versionLine", ("version", version), ("date", builtOn.ToString("yyyy-MM-dd")));
        AgeLine.Text = VersionAge.AgeLine(builtOn, Today());
    }

    public static DateTime BuildDate()
    {
        try
        {
            return File.GetLastWriteTime(Environment.ProcessPath ?? AppContext.BaseDirectory).Date;
        }
        catch
        {
            return DateTime.Today;
        }
    }

    private void OnSite(object sender, RequestNavigateEventArgs e)
    {
        OpenProductPage();
        e.Handled = true;
    }

    private void OnCheckNewer(object sender, System.Windows.RoutedEventArgs e) => OpenProductPage();

    public static void OpenProductPage() =>
        Process.Start(new ProcessStartInfo(VersionAge.ProductUrl) { UseShellExecute = true });

    private void OnNotices(object sender, System.Windows.RoutedEventArgs e)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.md");
        if (!File.Exists(path))
            return;
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
