using System.Windows;

namespace Peritum.Protect.App.Views;

public partial class SettingsView
{
    public SettingsView()
    {
        InitializeComponent();
    }

    public event EventHandler? BackRequested;

    private void OnBack(object sender, System.Windows.RoutedEventArgs e) =>
        BackRequested?.Invoke(this, EventArgs.Empty);

    private void OnAddFormat(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.SettingsViewModel model)
            return;
        var window = new NumberFormatWindow { Owner = Window.GetWindow(this) };
        if (window.ShowDialog() == true && window.Term is not null)
            model.AddTaughtFormat(window.Term);
    }
}
