using WiFiControl.Recon.Model;

namespace WiFiControl.Recon.Tests;

/// <summary>Компьютер в памяти: что задано в полях, то и прочитается; заданный отказ бросается при чтении.</summary>
internal sealed class FakeSystem : IReadOnlySystem
{
    public string ComputerName { get; set; } = "LAPTOP-7";

    public WindowsVersion Windows = new("Windows 10 Pro", "Professional", "24H2", 26100, 2033);
    public ProcessIdentity Process = new(@"LAPTOP-7\alex", true);
    public LaunchOrigin Launch = new(new ParentProcess(4120, "explorer"), 1388);

    public static readonly Guid WifiId = new("b0fbe93a-b7d1-490e-9b93-c44d06b82760");
    public static readonly Guid EthernetId = new("5d1c0a4e-2f7b-4c1a-9e1d-3b8f6a2c7d40");

    public List<WifiAdapter> WifiAdapters = [new(WifiId, "Intel(R) Wi-Fi 6 AX200 160MHz", "Connected")];

    public List<NetworkConnection> NetworkConnections =
    [
        new(EthernetId, "Ethernet", "Realtek PCIe GbE Family Controller", "Down"),
        new(WifiId, "Беспроводная сеть", "Intel(R) Wi-Fi 6 AX200 160MHz", "Up"),
    ];

    /// <summary>Текущие подключения по GUID адаптера; адаптера нет в словаре — он не подключён.</summary>
    public Dictionary<Guid, WifiConnection> Connections = new()
    {
        [WifiId] = new("Office", "CC-2D-21-3B-83-96", "Office", true, "RSNA_PSK", "CCMP", 82, "Profile"),
    };

    /// <summary>Список BSS по BSSID; BSSID нет в словаре — точки доступа нет в списке.</summary>
    public Dictionary<string, ConnectedBss> Bss = new()
    {
        ["CC-2D-21-3B-83-96"] = new(5180000, 5f, 36, -61),
    };

    /// <summary>Адаптеры «Сетевых подключений» с настройками IPv4, как их читает код NetSwitch.</summary>
    public List<Adapter> Adapters =
    [
        new(EthernetId, "Ethernet", "Realtek PCIe GbE Family Controller",
            @"PCI\VEN_10EC&DEV_8168\01", "00-E0-4C-68-01-02", true, AdapterState.NoLink),
        new(WifiId, "Беспроводная сеть", "Intel(R) Wi-Fi 6 AX200 160MHz", @"PCI\VEN_8086&DEV_2723\01", "A4-C3-F0-11-22-33", true, AdapterState.Connected)
        {
            IsWireless = true,
            Active = new(["192.168.1.37/24"], ["192.168.1.1"], ["192.168.1.1"], false),
        },
    ];

    public InteractiveUser InteractiveUser = new(@"LAPTOP-7\alex", "S-1-5-21-1004336348-1177238915-682003330-1001", true);

    public SystemProxyState SystemProxy = new(
        ProxySettings.FromRaw(true, "10.0.68.52:3128", "*.corp.local;10.*;<local>"), true, "http://wpad.corp.local/proxy.pac");

    public ProxyPolicy ProxyPolicy = new(false, false, false);

    public ProxySettings WinHttpProxy = ProxySettings.FromRaw(false, null, null);

    public List<IpInterface> IpInterfaces = [new(EthernetId, 12, 25, true), new(WifiId, 18, 35, true)];

    public List<Route> DefaultRoutes = [new(WifiId, 18, "0.0.0.0/0", "192.168.1.1", 0)];

    /// <summary>Лучший маршрут по адресу назначения; адреса нет в словаре — маршрута к нему нет.</summary>
    public Dictionary<string, BestRoute> BestRoutes = new()
    {
        ["8.8.8.8"] = new(new(WifiId, 18, "0.0.0.0/0", "192.168.1.1", 0), "192.168.1.37"),
    };

    public Exception? WindowsFailure { get; set; }
    public Exception? ProcessFailure { get; set; }
    public Exception? LaunchFailure { get; set; }
    public Exception? WifiAdaptersFailure { get; set; }
    public Exception? NetworkConnectionsFailure { get; set; }
    public Exception? ConnectionFailure { get; set; }
    public Exception? BssFailure { get; set; }
    public Exception? AdaptersFailure { get; set; }
    public Exception? InteractiveUserFailure { get; set; }
    public Exception? SystemProxyFailure { get; set; }
    public Exception? ProxyPolicyFailure { get; set; }
    public Exception? WinHttpProxyFailure { get; set; }
    public Exception? IpInterfacesFailure { get; set; }
    public Exception? DefaultRoutesFailure { get; set; }
    public Exception? BestRouteFailure { get; set; }

    public WindowsVersion ReadWindows() => WindowsFailure != null ? throw WindowsFailure : Windows;

    public ProcessIdentity ReadProcess() => ProcessFailure != null ? throw ProcessFailure : Process;

    public LaunchOrigin ReadLaunch() => LaunchFailure != null ? throw LaunchFailure : Launch;

    public IReadOnlyList<WifiAdapter> ReadWifiAdapters() => WifiAdaptersFailure != null ? throw WifiAdaptersFailure : WifiAdapters;

    public IReadOnlyList<NetworkConnection> ReadNetworkConnections() =>
        NetworkConnectionsFailure != null ? throw NetworkConnectionsFailure : NetworkConnections;

    public WifiConnection? ReadCurrentConnection(Guid adapter) =>
        ConnectionFailure != null ? throw ConnectionFailure : Connections.GetValueOrDefault(adapter);

    public ConnectedBss? ReadConnectedBss(Guid adapter, string bssid) =>
        BssFailure != null ? throw BssFailure : Bss.GetValueOrDefault(bssid);

    public IReadOnlyList<Adapter> ReadAdapters() => AdaptersFailure != null ? throw AdaptersFailure : Adapters;

    public InteractiveUser ReadInteractiveUser() => InteractiveUserFailure != null ? throw InteractiveUserFailure : InteractiveUser;

    public SystemProxyState ReadSystemProxy(string userSid) =>
        SystemProxyFailure != null ? throw SystemProxyFailure : userSid == InteractiveUser.Sid ? SystemProxy : throw new InvalidOperationException("Нет пользователя " + userSid);

    public ProxyPolicy ReadProxyPolicy(string userSid) =>
        ProxyPolicyFailure != null ? throw ProxyPolicyFailure : userSid == InteractiveUser.Sid ? ProxyPolicy : throw new InvalidOperationException("Нет пользователя " + userSid);

    public ProxySettings ReadWinHttpProxy() => WinHttpProxyFailure != null ? throw WinHttpProxyFailure : WinHttpProxy;

    public IReadOnlyList<IpInterface> ReadIpInterfaces() => IpInterfacesFailure != null ? throw IpInterfacesFailure : IpInterfaces;

    public IReadOnlyList<Route> ReadDefaultRoutes() => DefaultRoutesFailure != null ? throw DefaultRoutesFailure : DefaultRoutes;

    public BestRoute? ReadBestRoute(string destination) =>
        BestRouteFailure != null ? throw BestRouteFailure : BestRoutes.GetValueOrDefault(destination);
}

internal sealed class FakeClock : IClock
{
    public DateTime Now { get; set; } = new(2026, 10, 23, 9, 15, 42, 317);
}
