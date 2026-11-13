using Peritum.Protect.App.ViewModels;
using Peritum.Protect.App;
using Peritum.Protect.App.Services;
using System.Windows.Input;

namespace Peritum.Protect.App.Views;

public partial class PasteWarningWindow
{
    private bool _decided;

    public PasteWarningWindow(PasteWarningViewModel model)
    {
        InitializeComponent();
        UiScale.Apply(this);
        AppIcon.Apply(this);
        ThemeService.BindWindow(this);
        DataContext = model;
        model.Decided += _ =>
        {
            _decided = true;
            Close();
        };
        Loaded += (_, _) =>
        {
            var area = System.Windows.SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - 16;
            Top = area.Bottom - ActualHeight - 16;
        };
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (!_decided && DataContext is PasteWarningViewModel model)
            model.Dismiss();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }
        base.OnPreviewKeyDown(e);
    }
}
