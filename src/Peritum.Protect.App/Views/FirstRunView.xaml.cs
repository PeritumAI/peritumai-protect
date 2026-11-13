using Peritum.Protect.App;
using Peritum.Protect.App.ViewModels;
using Peritum.Protect.App.Services;
using Peritum.Protect.Core.Profiles;

namespace Peritum.Protect.App.Views;

public partial class FirstRunView
{
    public FirstRunView()
        : this(profileLocked: false, lockedProfileId: null)
    {
    }

    public FirstRunView(bool profileLocked, string? lockedProfileId, bool linksLocked = false)
    {
        InitializeComponent();
        AppIcon.Apply(this);
        ThemeService.BindWindow(this);
        var profiles = ProfileCatalog.LoadEmbedded();
        ProfileList.ItemsSource = profiles;
        ProfileList.IsEnabled = !profileLocked;
        var selected = profiles.FirstOrDefault(profile =>
            profile.Id.Equals(lockedProfileId ?? "", StringComparison.OrdinalIgnoreCase));
        ProfileList.SelectedItem = selected ?? profiles[0];
        SelectedProfileId = ((ProtectionProfile)ProfileList.SelectedItem).Id;
        var chosen = (ProtectionProfile)ProfileList.SelectedItem;
        ProfileDescription.Text = ProfileNames.Description(chosen.Id, chosen.Description);
        LinkCheck.IsChecked = chosen.WatchLinks;
        LinkCheck.IsEnabled = !linksLocked;
    }

    public string SelectedProfileId { get; private set; } = "general";

    public bool WarnAboutLinks => LinkCheck.IsChecked == true;

    private void OnProfileChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ProfileList.SelectedItem is not ProtectionProfile profile)
            return;
        SelectedProfileId = profile.Id;
        ProfileDescription.Text = ProfileNames.Description(profile.Id, profile.Description);
        if (LinkCheck.IsEnabled)
            LinkCheck.IsChecked = profile.WatchLinks;
    }

    private void OnContinue(object sender, System.Windows.RoutedEventArgs e) => DialogResult = true;

    private void OnQuit(object sender, System.Windows.RoutedEventArgs e) => DialogResult = false;
}
