using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using WiFiControl.Core.Model;

namespace WiFiControl;

/// <summary>
/// Главное окно. Здесь же правила обновления: поиск сетей при открытии, пока окно видно — чтение
/// раз в 10 с и после конца поиска, а после подключений и смены адресов — когда они утихнут.
/// </summary>
internal partial class MainWindow : Window
{
    private static readonly TimeSpan ListInterval = TimeSpan.FromSeconds(10);
    // Подключение даёт череду уведомлений и смен адресов; читать имеет смысл после последней
    private static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(2);

    private readonly MainViewModel _model;
    private readonly DispatcherTimer _listTimer;
    private readonly DispatcherTimer _settleTimer;
    private readonly IDisposable? _notifications;

    public MainWindow(MainViewModel model, IWifiSystem wifi)
    {
        InitializeComponent();
        DataContext = _model = model;

        _listTimer = new DispatcherTimer(ListInterval, DispatcherPriority.Background, (_, _) => LoadIfVisible(), Dispatcher);
        _settleTimer = new DispatcherTimer(SettleDelay, DispatcherPriority.Background, (_, _) => OnSettled(), Dispatcher) { IsEnabled = false };

        try
        {
            _notifications = wifi.Watch(notification => Dispatcher.BeginInvoke(() => OnNotification(notification)));
        }
        catch (InvalidOperationException e)
        {
            // Без уведомлений окно всё равно обновляется по таймеру и по «Обновить»
            model.NotificationsError = e.Message;
        }
        NetworkChange.NetworkAddressChanged += OnAddressChanged;

        Loaded += async (_, _) => await _model.RefreshAsync();
        Closed += (_, _) =>
        {
            _listTimer.Stop();
            _settleTimer.Stop();
            _notifications?.Dispose();
            NetworkChange.NetworkAddressChanged -= OnAddressChanged;
        };
    }

    private bool Shown => IsVisible && WindowState != WindowState.Minimized;

    private void OnNotification(WifiNotification notification)
    {
        if (notification is WifiScanCompleted) LoadIfVisible();
        else Settle();
    }

    // Приходит в чужом потоке
    private void OnAddressChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Settle);

    private void Settle()
    {
        _settleTimer.Stop();
        _settleTimer.Start();
    }

    private async void OnSettled()
    {
        _settleTimer.Stop();
        await _model.LoadAsync();
    }

    private async void LoadIfVisible()
    {
        if (Shown) await _model.LoadAsync();
    }

    private async void OnConnectNewClick(object sender, RoutedEventArgs e)
    {
        var row = (NetworkRowViewModel)((Button)sender).DataContext;
        if (row.NewSecurity is not { } security) return;
        await ConnectNewAsync(row.Row, security);
    }

    /// <summary>
    /// У знакомой сети — новый пароль в её профиле сети Windows. Сети, чей профиль сети Windows создало и удалило
    /// неудачное переключение, — снова окно новой сети.
    /// </summary>
    private async void OnReenterPasswordClick(object sender, RoutedEventArgs e)
    {
        if (_model.PasswordRejected is not { } network) return;
        if (network.WindowsProfileName.Length == 0)
        {
            if (WindowsProfileXml.SecurityFor(network.Security) is { } security) await ConnectNewAsync(network, security);
            return;
        }

        var dialog = new PasswordWindow(network.Network, security: null) { Owner = this };
        if (dialog.ShowDialog() == true) await _model.ReenterPasswordAsync(network, dialog.PasswordText);
    }

    private async Task ConnectNewAsync(NetworkRow network, NewNetworkSecurity security)
    {
        var dialog = new PasswordWindow(network.Network, security) { Owner = this };
        if (dialog.ShowDialog() == true) await _model.ConnectNewAsync(network, security, dialog.PasswordText, dialog.IsAutoConnect);
    }

    // Список профилей собирается при нажатии: профили могли измениться
    private void OnApplyProfileClick(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom };
        foreach (Profile profile in _model.ProfileChoices)
        {
            menu.Items.Add(new MenuItem { Header = profile.Name, Command = _model.ApplyProfileCommand, CommandParameter = profile });
        }
        menu.IsOpen = true;
    }
}
