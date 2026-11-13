namespace Peritum.Protect.App.Views;

public partial class EventListView
{
    public EventListView()
    {
        InitializeComponent();
    }

    public event EventHandler? ExportRequested;
    public event EventHandler? ExportStatsRequested;
    public event EventHandler? MonthlyPdfRequested;
    public event EventHandler? MonthlyCsvRequested;

    private void OnExport(object sender, System.Windows.RoutedEventArgs e) =>
        ExportRequested?.Invoke(this, EventArgs.Empty);

    private void OnExportStats(object sender, System.Windows.RoutedEventArgs e) =>
        ExportStatsRequested?.Invoke(this, EventArgs.Empty);

    private void OnMonthlyPdf(object sender, System.Windows.RoutedEventArgs e) =>
        MonthlyPdfRequested?.Invoke(this, EventArgs.Empty);

    private void OnMonthlyCsv(object sender, System.Windows.RoutedEventArgs e) =>
        MonthlyCsvRequested?.Invoke(this, EventArgs.Empty);
}
