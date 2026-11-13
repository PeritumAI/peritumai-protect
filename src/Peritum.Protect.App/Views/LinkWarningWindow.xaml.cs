using System.Windows.Input;
using Peritum.Protect.App;
using Peritum.Protect.App.Services;
using Peritum.Protect.App.ViewModels;

namespace Peritum.Protect.App.Views;

public partial class LinkWarningWindow
{
    private bool _decided;

    public LinkWarningWindow(LinkWarningViewModel model)
    {
        InitializeComponent();
        UiScale.Apply(this);
        AppIcon.Apply(this);
        ThemeService.BindWindow(this);
        DataContext = model;
        model.Decided += _ =>
        {
            if (_decided)
                return;
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
        if (!_decided && DataContext is LinkWarningViewModel model)
        {
            _decided = true;
            model.KeepCommand.Execute(null);
        }
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
