using System.Windows;
using System.Windows.Input;
using WiFiControl.Core.Model;

namespace WiFiControl;

/// <summary>
/// Данные для подключения к новой или скрытой сети. Для знакомой сети вводится только новый пароль.
/// </summary>
internal partial class PasswordWindow : Window
{
    private readonly NewNetworkSecurity? _security;
    private readonly bool _hidden;

    /// <param name="security">
    /// Защита новой сети; у открытой пароля нет, остаётся только флажок. null — «Ввести пароль заново»: только пароль,
    /// а подходит ли ключ к профилю сети Windows, проверяет переключение.
    /// </param>
    public PasswordWindow(string network, NewNetworkSecurity? security) : this(network, security, hidden: false, autoConnect: true)
    {
    }

    public PasswordWindow(NewNetwork request) : this(request.Name, request.Security, request.Hidden, request.AutoConnect)
    {
    }

    private PasswordWindow(string network, NewNetworkSecurity? security, bool hidden, bool autoConnect)
    {
        _security = security;
        _hidden = hidden;
        InitializeComponent();
        Title = "WiFiControl";
        AutoConnect.IsChecked = autoConnect;
        if (hidden)
        {
            Heading.Text = "Подключение к скрытой сети";
            HiddenNetworkPanel.Visibility = Visibility.Visible;
            NetworkName.Text = network;
            SecurityChoice.SelectedValue = security ?? NewNetworkSecurity.Wpa2Personal;
            FocusManager.SetFocusedElement(this, network.Length == 0 ? NetworkName
                : security == NewNetworkSecurity.Open ? Connect : Password);
        }
        else
        {
            Heading.Text = security == null ? "Новый пароль для сети «" + network + "»" : "Подключение к сети «" + network + "»";
            if (security == null) AutoConnect.Visibility = Visibility.Collapsed;
            FocusManager.SetFocusedElement(this, security == NewNetworkSecurity.Open ? Connect : Password);
        }
        UpdateInput();
    }

    public string HiddenName => NetworkName.Text;
    public NewNetworkSecurity? Security => _hidden ? SecurityChoice.SelectedValue as NewNetworkSecurity? : _security;

    /// <summary>Введённый пароль; у открытой сети — пустой.</summary>
    public string PasswordText => Security == NewNetworkSecurity.Open ? "" : Password.Password;

    public bool IsAutoConnect => AutoConnect.IsChecked == true;

    private void OnInputChanged(object sender, RoutedEventArgs e)
    {
        if (Connect != null) UpdateInput();
    }

    private void UpdateInput()
    {
        NewNetworkSecurity? security = Security;
        bool open = security == NewNetworkSecurity.Open;
        bool acceptsKey = security == null || WindowsProfileXml.AcceptsKey(security.Value);
        Hint.Text = acceptsKey ? "От 8 до 63 символов или 64 шестнадцатеричные цифры." : "От 8 до 63 символов.";
        PasswordPanel.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        OpenNote.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        Connect.IsEnabled = (!_hidden || (security != null && WindowsProfileXml.IsValidHiddenName(HiddenName)))
            && (open || WifiPassword.IsValid(Password.Password, acceptsKey));
    }

    private void OnConnectClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
