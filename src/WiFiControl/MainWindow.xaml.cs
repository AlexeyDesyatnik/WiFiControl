using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
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
        NetworkList.ItemsSource = new CompositeCollection
        {
            new CollectionContainer { Collection = model.Networks },
            NetworkList.FindResource("HiddenNetworkRow"),
        };

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
        await ConnectNewAsync(new NewNetwork(row.Row.Network, security));
    }

    private async void OnAutoConnectClick(object sender, RoutedEventArgs e)
    {
        var checkBox = (CheckBox)sender;
        var row = (NetworkRowViewModel)checkBox.DataContext;
        await _model.SetAutoConnectAsync(row, autoConnect: checkBox.IsChecked != true);
        // Если строка ещё на экране, возвращаем значение из последнего снимка
        checkBox.GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateTarget();
    }

    private async void OnConnectHiddenClick(object sender, RoutedEventArgs e) =>
        await ConnectNewAsync(new NewNetwork("", NewNetworkSecurity.Wpa2Personal, Hidden: true));

    /// <summary>
    /// У знакомой сети — новый пароль в её профиле сети Windows. Сети, чей профиль сети Windows создало и удалило
    /// неудачное переключение, — снова окно новой сети.
    /// </summary>
    private async void OnReenterPasswordClick(object sender, RoutedEventArgs e)
    {
        if (_model.PasswordRejected is not { } retry) return;
        if (retry.NewNetwork is { } request)
        {
            await ConnectNewAsync(request);
            return;
        }

        var dialog = new PasswordWindow(retry.Network, security: null) { Owner = this };
        if (dialog.ShowDialog() == true) await _model.ReenterPasswordAsync(retry, dialog.PasswordText);
    }

    private async Task ConnectNewAsync(NewNetwork request)
    {
        var dialog = new PasswordWindow(request) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Security is { } selected)
        {
            await _model.ConnectNewAsync(request with
            {
                Name = request.Hidden ? dialog.HiddenName : request.Name,
                Security = selected,
                AutoConnect = dialog.IsAutoConnect,
            }, dialog.PasswordText);
        }
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
