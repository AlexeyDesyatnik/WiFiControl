using System.Windows;
using WiFiControl.Core.Model;

namespace WiFiControl;

/// <summary>
/// Пароль для новой сети с флажком автоподключения, или новый пароль для знакомой («Ввести пароль заново») — тогда без флажка:
/// режим автоподключения остаётся, какой был в профиле сети Windows. «Подключиться» доступно, только когда пароль подходит.
/// </summary>
internal partial class PasswordWindow : Window
{
    private readonly bool _open;
    private readonly bool _keyAccepted;

    /// <param name="security">
    /// Защита новой сети; у открытой пароля нет, остаётся только флажок. null — «Ввести пароль заново»: только пароль,
    /// а подходит ли ключ к профилю сети Windows, проверяет переключение.
    /// </param>
    public PasswordWindow(string network, NewNetworkSecurity? security)
    {
        InitializeComponent();
        _open = security == NewNetworkSecurity.Open;
        _keyAccepted = security is not { } kind || WindowsProfileXml.AcceptsKey(kind);
        Title = "WiFiControl";
        Heading.Text = security == null ? "Новый пароль для сети «" + network + "»" : "Подключение к сети «" + network + "»";
        Hint.Text = _keyAccepted ? "От 8 до 63 символов или 64 шестнадцатеричные цифры." : "От 8 до 63 символов.";
        if (_open)
        {
            PasswordPanel.Visibility = Visibility.Collapsed;
            OpenNote.Visibility = Visibility.Visible;
        }
        if (security == null) AutoConnect.Visibility = Visibility.Collapsed;
        Connect.IsEnabled = _open;
    }

    /// <summary>Введённый пароль; у открытой сети — пустой.</summary>
    public string PasswordText => _open ? "" : Password.Password;

    public bool IsAutoConnect => AutoConnect.IsChecked == true;

    private void OnPasswordChanged(object sender, RoutedEventArgs e) => Connect.IsEnabled = WifiPassword.IsValid(Password.Password, _keyAccepted);

    private void OnConnectClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
