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

    /// <summary>Подключения и отключения по порядку: «disconnect», «connect профиль».</summary>
    public List<string> Calls { get; } = [];

    /// <summary>Профили сетей Windows, подключение через которые не удаётся, и код причины в попытке.</summary>
    public Dictionary<string, int> Failing { get; } = [];

    /// <summary>Профили сетей Windows, на подключение через которые Windows молчит.</summary>
    public HashSet<string> Silent { get; } = [];

    public Exception? ConnectFailure { get; set; }

    public Exception? CurrentFailure { get; set; }

    /// <summary>Уведомления, которые приходят во время подключения перед его исходом.</summary>
    public List<WifiNotification> DuringConnect { get; } = [];

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
        if (CurrentFailure != null) throw CurrentFailure;
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

    public async Task DisconnectAsync(Guid adapter, CancellationToken cancellationToken)
    {
        await Task.Yield();
        Calls.Add("disconnect");
        if (Current == null) return;
        string network = NetworkKey.FromBytes(Current);
        Current = null;
        Raise(new WifiDisconnected(adapter, network));
    }

    /// <summary>Как Windows по ручной проверке подключения: исход приходит уведомлениями (docs/connect-check.md).</summary>
    public async Task ConnectAsync(Guid adapter, string windowsProfile, CancellationToken cancellationToken)
    {
        await Task.Yield();
        Calls.Add("connect " + windowsProfile);
        if (ConnectFailure != null) throw ConnectFailure;

        string network = WindowsProfiles.Single(profile => profile.Name == windowsProfile).Network;
        foreach (WifiNotification notification in DuringConnect) Raise(notification);
        if (Silent.Contains(windowsProfile)) return;
        if (Failing.TryGetValue(windowsProfile, out int reason))
        {
            Raise(new WifiConnectionAttemptFailed(adapter, network, reason));
            Raise(new WifiConnectionFailed(adapter, network, WlanReason.NetworkNotAvailable));
            return;
        }
        Current = Encoding.UTF8.GetBytes(network);
        Raise(new WifiConnected(adapter, network));
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
