using Peritum.Protect.Core;

namespace Peritum.Protect.App.Views;

public partial class DropZoneView
{
    public DropZoneView()
    {
        InitializeComponent();
    }

    public event EventHandler? BrowseRequested;

    public void SetDragOver(bool over)
    {
        Title.Text = over ? TextCatalog.Get("dropToCheck") : TextCatalog.Get("dropTitle");
    }

    private void OnClick(object sender, System.Windows.RoutedEventArgs e) =>
        BrowseRequested?.Invoke(this, EventArgs.Empty);
}
