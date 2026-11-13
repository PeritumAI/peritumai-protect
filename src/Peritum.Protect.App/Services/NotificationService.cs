using Peritum.Protect.App.ViewModels;

namespace Peritum.Protect.App.Services;

public sealed class NotificationService
{
    private Views.PasteWarningWindow? _window;
    private Views.LinkWarningWindow? _link;

    public bool IsPopupOpen => _window is { IsVisible: true } || _link is { IsVisible: true };

    public void ShowPaste(PasteWarningViewModel model)
    {
        if (IsPopupOpen)
        {
            _window?.Activate();
            _link?.Activate();
            return;
        }

        _window = new Views.PasteWarningWindow(model);
        _window.Closed += (_, _) => _window = null;
        _window.Show();
    }

    public void ClosePopups()
    {
        _window?.Close();
        _link?.Close();
    }

    public void ShowLink(LinkWarningViewModel model)
    {
        if (IsPopupOpen)
        {
            _window?.Activate();
            _link?.Activate();
            return;
        }

        _link = new Views.LinkWarningWindow(model);
        _link.Closed += (_, _) => _link = null;
        _link.Show();
    }
}
