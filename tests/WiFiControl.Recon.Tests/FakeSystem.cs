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

    public List<WifiAdapter> WifiAdapters = [new(WifiId, "Intel(R) Wi-Fi 6 AX200 160MHz", "Connected")];

    public List<NetworkConnection> NetworkConnections =
    [
        new(new Guid("5d1c0a4e-2f7b-4c1a-9e1d-3b8f6a2c7d40"), "Ethernet", "Realtek PCIe GbE Family Controller", "Down"),
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

    public Exception? WindowsFailure { get; set; }
    public Exception? ProcessFailure { get; set; }
    public Exception? LaunchFailure { get; set; }
    public Exception? WifiAdaptersFailure { get; set; }
    public Exception? NetworkConnectionsFailure { get; set; }
    public Exception? ConnectionFailure { get; set; }
    public Exception? BssFailure { get; set; }

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
}

internal sealed class FakeClock : IClock
{
    public DateTime Now { get; set; } = new(2026, 10, 23, 9, 15, 42, 317);
}
