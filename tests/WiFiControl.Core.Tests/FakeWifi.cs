using System.Text;
using WiFiControl.Core.Model;

namespace WiFiControl.Core.Tests;

/// <summary>Wi-Fi в памяти: отдаёт заданные тестом адаптеры, записи сетей и текущую сеть, а уведомления поднимает по команде теста.</summary>
internal sealed class FakeWifi : IWifiSystem
{
    public static readonly Guid WifiId = new("00000000-0000-0000-0000-000000000002");

    private readonly List<Action<WifiNotification>> _watchers = [];

    public List<WifiAdapter> Adapters { get; } = [new(WifiId, "Intel(R) Wi-Fi 6E AX211 160MHz")];

    public List<VisibleNetwork> Networks { get; } = [];

    public List<WindowsProfile> WindowsProfiles { get; } = [];

    public byte[]? Current { get; set; }

    public static VisibleNetwork Entry(string name, int signal, WifiSecurity security = WifiSecurity.Wpa2Personal, string windowsProfile = "")
    {
        return new VisibleNetwork(Encoding.UTF8.GetBytes(name), signal, security, windowsProfile);
    }

    public async Task<IReadOnlyList<WifiAdapter>> ReadAdaptersAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        return [.. Adapters];
    }

    public async Task<IReadOnlyList<VisibleNetwork>> ReadVisibleNetworksAsync(Guid adapter, CancellationToken cancellationToken)
    {
        await Task.Yield();
        return [.. Networks];
    }

    public async Task<byte[]?> ReadCurrentNetworkAsync(Guid adapter, CancellationToken cancellationToken)
    {
        await Task.Yield();
        return Current;
    }

    public async Task<IReadOnlyList<WindowsProfile>> ReadWindowsProfilesAsync(Guid adapter, CancellationToken cancellationToken)
    {
        await Task.Yield();
        return [.. WindowsProfiles];
    }

    public async Task ScanAsync(Guid adapter, CancellationToken cancellationToken)
    {
        await Task.Yield();
        Raise(new WifiScanCompleted(adapter));
    }

    public IDisposable Watch(Action<WifiNotification> onNotification)
    {
        _watchers.Add(onNotification);
        return new Subscription(() => _watchers.Remove(onNotification));
    }

    public void Raise(WifiNotification notification)
    {
        foreach (Action<WifiNotification> watcher in _watchers.ToList()) watcher(notification);
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
