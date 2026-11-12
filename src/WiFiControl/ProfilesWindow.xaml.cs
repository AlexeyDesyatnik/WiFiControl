using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using WiFiControl.Core.Model;

namespace WiFiControl;

internal partial class ProfilesWindow : Window
{
    private readonly MainViewModel _model;
    private bool _saving;

    public ProfilesWindow(MainViewModel model)
    {
        _model = model;
        InitializeComponent();
        Reload();
    }

    private void Reload(Guid? profileId = null, string? network = null)
    {
        ProfileSet profiles = _model.ProfilesForEditor;
        profileId ??= (ProfileList.SelectedItem as Profile)?.Id;
        network ??= (BindingList.SelectedItem as BindingRow)?.Network;

        Profile[] choices = [Profile.Automatic, .. profiles.Profiles.OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase)];
        ProfileList.ItemsSource = choices;
        ProfileList.SelectedValue = profileId ?? profiles.DefaultProfileId;
        if (ProfileList.SelectedIndex < 0) ProfileList.SelectedValue = profiles.DefaultProfileId;

        DefaultProfile.Text = "Профиль по умолчанию: " + profiles.Find(profiles.DefaultProfileId)!.Name;
        DefaultWarning.Visibility = profiles.DefaultProfileId == Profile.AutomaticId ? Visibility.Collapsed : Visibility.Visible;

        BindingProfile.ItemsSource = choices;
        BindingProfile.SelectedValue = profiles.DefaultProfileId;
        var names = choices.ToDictionary(profile => profile.Id, profile => profile.Name);
        BindingList.ItemsSource = profiles.Bindings.OrderBy(binding => binding.Key, NetworkKey.Comparer)
            .Select(binding => new BindingRow(binding.Key, binding.Value, names[binding.Value])).ToArray();
        BindingList.SelectedValue = network;
        if (BindingList.SelectedIndex < 0) NetworkName.Clear();
        DeleteBinding.IsEnabled = BindingList.SelectedItem != null;
        UpdateProfileActions();
    }

    private void OnProfileSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateProfileActions();

    private void UpdateProfileActions()
    {
        Profile? selected = ProfileList.SelectedItem as Profile;
        EditProfile.IsEnabled = DeleteProfile.IsEnabled = selected is { IsAutomatic: false };
        SetDefault.IsEnabled = selected != null && selected.Id != _model.ProfilesForEditor.DefaultProfileId;
    }

    private void OnBindingSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        DeleteBinding.IsEnabled = BindingList.SelectedItem is BindingRow;
        if (BindingList.SelectedItem is BindingRow row)
        {
            NetworkName.Text = row.Network;
            BindingProfile.SelectedValue = row.ProfileId;
        }
    }

    private async void OnCreateProfile(object sender, RoutedEventArgs e) => await EditAsync(new Profile());

    private async void OnEditProfile(object sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is Profile { IsAutomatic: false } profile)
            await EditAsync(profile);
    }

    private async Task EditAsync(Profile profile)
    {
        var dialog = new ProfileEditorWindow(profile, _model.SaveProfile) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        Reload(profile.Id);
        await _model.LoadAsync();
    }

    private async void OnSetDefault(object sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is not Profile profile) return;
        if (!profile.IsAutomatic && MessageBox.Show(this,
            "Ко всем сетям без привязки будут применяться настройки профиля " + profile.Name + ". Назначить его по умолчанию?",
            Title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;

        await SaveAsync(() => _model.ChangeDefaultProfileAsync(profile.Id, ConfirmApply));
    }

    private async void OnDeleteProfile(object sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is not Profile { IsAutomatic: false } profile) return;
        ProfileSet profiles = _model.ProfilesForEditor;
        bool isDefault = profile.Id == profiles.DefaultProfileId;
        IEnumerable<string> affected = profiles.Bindings.Where(binding => binding.Value == profile.Id)
            .Select(binding => binding.Key);
        if (isDefault)
        {
            IEnumerable<string> unbound = _model.Networks.Select(row => row.Name).Append(_model.CurrentNetworkForEditor)
                .OfType<string>().Where(network => !profiles.Bindings.ContainsKey(network));
            affected = affected.Concat(unbound);
        }
        string fallback = isDefault ? Profile.AutomaticName : profiles.Find(profiles.DefaultProfileId)!.Name;
        string[] networks = affected.Distinct(NetworkKey.Comparer).Order(NetworkKey.Comparer).ToArray();
        var dialog = new RemoveProfileWindow(profile.Name, networks, isDefault, fallback) { Owner = this };
        if (dialog.ShowDialog() == true)
            await SaveAsync(() => _model.RemoveProfileAsync(profile.Id, ConfirmApply));
    }

    private async void OnSaveBinding(object sender, RoutedEventArgs e)
    {
        string network = NetworkName.Text;
        if (!_model.ProfilesForEditor.Bindings.ContainsKey(network) && !NetworkKey.IsValidText(network))
        {
            MessageBox.Show(this, "Укажите имя сети длиной от 1 до 32 байт в UTF-8.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            NetworkName.Focus();
            return;
        }
        if (BindingProfile.SelectedValue is Guid id)
            await SaveAsync(() => _model.ChangeBindingAsync(network, id, ConfirmApply), network);
    }

    private async void OnDeleteBinding(object sender, RoutedEventArgs e)
    {
        if (BindingList.SelectedItem is BindingRow row)
            await SaveAsync(() => _model.ChangeBindingAsync(row.Network, null, ConfirmApply));
    }

    private bool ConfirmApply(Profile profile)
    {
        if (!MainWindow.ConfirmApply(this, profile)) return false;
        SaveStatus.Text = "Применение профиля " + profile.Name + "…";
        return true;
    }

    private async Task SaveAsync(Func<Task<string?>> save, string? network = null)
    {
        ProfileSet previous = _model.ProfilesForEditor;
        _saving = true;
        EditorTabs.IsEnabled = false;
        CloseEditor.IsEnabled = false;
        SaveStatus.Text = "Сохранение изменений…";
        try
        {
            string? error = await save();
            if (error != null)
                MessageBox.Show(this, error, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            if (error == null || !ReferenceEquals(previous, _model.ProfilesForEditor))
                Reload(network: network);
        }
        finally
        {
            EditorTabs.IsEnabled = true;
            CloseEditor.IsEnabled = true;
            SaveStatus.Text = "";
            _saving = false;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_saving) e.Cancel = true;
    }

    private sealed record BindingRow(string Network, Guid ProfileId, string ProfileName);
}
