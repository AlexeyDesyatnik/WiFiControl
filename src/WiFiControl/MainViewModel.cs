using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WiFiControl.Core.Model;
using WiFiControl.Core.Storage;

namespace WiFiControl;

/// <summary>Главное окно: блок «Сейчас» и список сетей из снимка текущего состояния.</summary>
internal sealed partial class MainViewModel(INetworkSystem network, IWifiSystem wifi, ProfileStore store) : ObservableObject
{
    private const string Missing = "—";

    private bool _loading;
    private bool _loadAgain;
    // Отказы, которые удачное чтение не отменяет: их видно, пока причина не уйдёт
    private string? _scanError;
    private string? _notificationsError;

    [ObservableProperty]
    private string _adapterName = "";

    [ObservableProperty]
    private string _networkTitle = "";

    [ObservableProperty]
    private string _networkProfile = "";

    [ObservableProperty]
    private int _signal;

    [ObservableProperty]
    private string _address = Missing;

    [ObservableProperty]
    private string _gateway = Missing;

    [ObservableProperty]
    private string _dns = Missing;

    [ObservableProperty]
    private string _proxy = Missing;

    /// <summary>Почему состояние не прочитано; null, если прочитано.</summary>
    [ObservableProperty]
    private string? _error;

    public ObservableCollection<NetworkRowViewModel> Networks { get; } = [];

    /// <summary>Подписка на уведомления Wi-Fi не удалась: окно обновляется только по таймеру и «Обновить».</summary>
    public string? NotificationsError
    {
        set
        {
            _notificationsError = value;
            Error = value;
        }
    }

    /// <summary>«Обновить»: поиск сетей, затем чтение.</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        try
        {
            IReadOnlyList<WifiAdapter> adapters = await wifi.ReadAdaptersAsync(CancellationToken.None);
            if (adapters.Count > 0) await wifi.ScanAsync(adapters[0].Id, CancellationToken.None);
            _scanError = null;
        }
        catch (InvalidOperationException e)
        {
            _scanError = e.Message;
        }
        await LoadAsync();
    }

    /// <summary>Перечитывает состояние без поиска сетей. Вызов во время чтения откладывается до его конца.</summary>
    public async Task LoadAsync()
    {
        if (_loading)
        {
            _loadAgain = true;
            return;
        }
        _loading = true;
        try
        {
            do
            {
                _loadAgain = false;
                try
                {
                    Show(await CurrentState.ReadAsync(network, wifi, store.LoadError == null ? store.Profiles : null, CancellationToken.None));
                    Error = _scanError ?? _notificationsError;
                }
                catch (InvalidOperationException e)
                {
                    Error = e.Message;
                }
            } while (_loadAgain);
        }
        finally
        {
            _loading = false;
        }
    }

    private void Show(CurrentState state)
    {
        AdapterName = state.WifiAdapter?.Description ?? "";
        NetworkTitle = state.WifiAdapter == null ? "Wi-Fi-адаптер не найден" : state.Network ?? "Не подключено";
        NetworkProfile = state.Network == null ? ""
            : state.Profile == null ? "профиль неизвестен"
            : state.IsBound ? "профиль: " + state.Profile.Name + " (привязана)"
            : "по умолчанию: " + state.Profile.Name;
        Signal = state.Networks.FirstOrDefault(row => row.IsCurrent)?.Signal ?? 0;

        ActiveIpv4? active = state.Adapter?.Active;
        Address = active == null || active.Addresses.Count == 0 ? Missing
            : string.Join(", ", active.Addresses) + (state.Adapter!.Configured.AutoAddress ? " · DHCP" : " · статический");
        Gateway = Join(active?.Gateways);
        Dns = Join(active?.Dns);
        Proxy = ProxyText(state.Proxy.SystemProxy);

        Networks.Clear();
        foreach (NetworkRow row in state.Networks) Networks.Add(new NetworkRowViewModel(row));
    }

    private static string Join(List<string>? values) => values == null || values.Count == 0 ? Missing : string.Join(", ", values);

    private static string ProxyText(SystemProxyState proxy)
    {
        var parts = new List<string>();
        if (proxy.Settings.Enabled)
        {
            parts.Add(proxy.Settings.Server);
            if (proxy.Settings.PrivateNetworksDirect) parts.Add("не для частных сетей");
        }
        if (proxy.AutoDetect) parts.Add("автоопределение");
        if (proxy.AutoConfigUrl.Length > 0) parts.Add("сценарий " + proxy.AutoConfigUrl);
        return parts.Count == 0 ? "нет" : string.Join(" · ", parts);
    }
}

/// <summary>Строка списка сетей.</summary>
internal sealed class NetworkRowViewModel(NetworkRow row)
{
    public string Name => row.Network;

    public int Signal => row.Signal;

    public bool IsCurrent => row.IsCurrent;

    public bool IsSecured => row.Security != WifiSecurity.Open;

    public string Security => row.Security switch
    {
        WifiSecurity.Open => "Открытая сеть",
        WifiSecurity.Wep => "WEP",
        WifiSecurity.WpaPersonal => "WPA-Personal",
        WifiSecurity.Wpa2Personal => "WPA2-Personal",
        WifiSecurity.Wpa3Personal => "WPA3-Personal",
        WifiSecurity.Enterprise => "Корпоративная (802.1X)",
        _ => "Другая защита",
    };

    public string Profile => row.Profile == null ? "профиль неизвестен"
        : row.IsBound ? row.Profile.Name
        : "по умолчанию: " + row.Profile.Name;
}
