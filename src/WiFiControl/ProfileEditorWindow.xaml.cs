using System.Windows;
using WiFiControl.Core.Model;

namespace WiFiControl;

internal partial class ProfileEditorWindow : Window
{
    private readonly Guid _id;
    private readonly IReadOnlyList<string> _originalProxyExceptions;
    private readonly string _originalProxyExceptionsText;
    private readonly Func<Profile, IReadOnlyList<string>> _save;

    public ProfileEditorWindow(Profile profile, Func<Profile, IReadOnlyList<string>> save)
    {
        _id = profile.Id;
        _originalProxyExceptions = profile.Proxy.Exceptions.ToArray();
        _save = save;
        InitializeComponent();
        Title = profile.Name.Length == 0 ? "Создание профиля" : "Редактирование профиля";
        ProfileName.Text = profile.Name;
        AutoAddress.IsChecked = profile.Ipv4.AutoAddress;
        Address.Text = profile.Ipv4.Address;
        Mask.Text = profile.Ipv4.Mask;
        Gateway.Text = profile.Ipv4.Gateway;
        AutoDns.IsChecked = profile.Ipv4.AutoDns;
        Dns1.Text = profile.Ipv4.Dns.ElementAtOrDefault(0) ?? "";
        Dns2.Text = profile.Ipv4.Dns.ElementAtOrDefault(1) ?? "";
        ProxyEnabled.IsChecked = profile.Proxy.Enabled;
        ProxyServer.Text = profile.Proxy.Server;
        ProxyExceptions.Text = string.Join(Environment.NewLine, _originalProxyExceptions);
        _originalProxyExceptionsText = ProxyExceptions.Text;
        BypassLocal.IsChecked = profile.Proxy.BypassLocal;
        PrivateNetworksDirect.IsChecked = profile.Proxy.PrivateNetworksDirect;
        UpdateAddressMode();
    }

    private void OnAddressModeChanged(object sender, RoutedEventArgs e) => UpdateAddressMode();

    private void UpdateAddressMode()
    {
        bool automatic = AutoAddress.IsChecked == true;
        AddressFields.IsEnabled = !automatic;
        AutoDns.IsEnabled = automatic;
        if (!automatic) AutoDns.IsChecked = false;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var profile = new Profile(_id)
        {
            Name = ProfileName.Text,
            Ipv4 = new Ipv4Settings
            {
                AutoAddress = AutoAddress.IsChecked == true,
                Address = Address.Text,
                Mask = Mask.Text,
                Gateway = Gateway.Text,
                AutoDns = AutoDns.IsChecked == true,
            },
            Proxy = new ProxySettings
            {
                Enabled = ProxyEnabled.IsChecked == true,
                Server = ProxyServer.Text,
                BypassLocal = BypassLocal.IsChecked == true,
                PrivateNetworksDirect = PrivateNetworksDirect.IsChecked == true,
            },
        };
        profile.Ipv4.Dns.AddRange([Dns1.Text, Dns2.Text]);
        profile.Proxy.Exceptions.AddRange(ProxyExceptions.Text == _originalProxyExceptionsText
            ? _originalProxyExceptions
            : ProxyExceptions.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));

        IReadOnlyList<string> errors = _save(profile);
        if (errors.Count == 0)
            DialogResult = true;
        else
            MessageBox.Show(this, string.Join(Environment.NewLine, errors), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
