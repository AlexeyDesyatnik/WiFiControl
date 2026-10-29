using WiFiControl.Core.Model;

namespace WiFiControl.Core.Tests;

/// <summary>
/// Компьютер в памяти: отдаёт заданные тестом адаптеры или заданный отказ, а запись меняет то,
/// что вернёт следующее чтение, как это происходит с настоящей системой. Перенесено из NetSwitch.
/// </summary>
/// <param name="time">Часы для изменений, запланированных через <see cref="Later"/>.</param>
internal sealed class FakeSystem(TimeProvider? time = null) : INetworkSystem
{
    private readonly List<(DateTimeOffset At, Action Change)> _later = [];

    public List<Adapter> Adapters { get; } = [];

    /// <summary>Выполненные записи по порядку.</summary>
    public List<string> Calls { get; } = [];

    public Exception? AdaptersFailure { get; set; }

    public Exception? Ipv4Failure { get; set; }

    public SystemProxyState SystemProxy { get; set; } = new();

    public ProxySettings WinHttp { get; set; } = new();

    public Exception? ProxyReadFailure { get; set; }

    public Exception? SystemProxyFailure { get; set; }

    public Exception? WinHttpFailure { get; set; }

    public List<DefaultRoute> DefaultRoutes { get; } = [];

    /// <summary>Что вернёт <see cref="ReadInternetAdapterAsync"/>.</summary>
    public Guid? InternetAdapter { get; set; }

    /// <summary>Адреса, отвечающие на ping.</summary>
    public HashSet<string> Answering { get; } = [];

    /// <summary>Сколько первых ping остаются без ответа и у отвечающих адресов.</summary>
    public int LostPings { get; set; }

    /// <summary>Открытые TCP-порты: «адрес:порт».</summary>
    public HashSet<string> OpenPorts { get; } = [];

    public Exception? RouteFailure { get; set; }

    /// <summary>Добавление маршрута «проходит», но маршрут не появляется.</summary>
    public bool RouteIgnored { get; set; }

    /// <summary>Изменение, которое случится, когда по часам пройдёт <paramref name="after"/>; видно со следующего чтения.</summary>
    public void Later(TimeSpan after, Action change)
    {
        if (time == null) throw new InvalidOperationException("Для Later подмене нужны часы.");
        _later.Add((time.GetUtcNow() + after, change));
    }

    public static Adapter WiFi(AdapterState state = AdapterState.NoLink)
    {
        return new Adapter
        {
            Id = "{00000000-0000-0000-0000-000000000002}",
            Name = "Беспроводная сеть",
            Description = "Intel(R) Wi-Fi 6E AX211 160MHz",
            IsPhysical = true,
            IsWireless = true,
            State = state
        };
    }

    public static Ipv4Settings Static(string address, string gateway = "10.68.12.1", params string[] dns)
    {
        var settings = new Ipv4Settings { AutoAddress = false, Address = address, Mask = "255.255.255.0", Gateway = gateway, AutoDns = false };
        settings.Dns.AddRange(dns);
        return settings;
    }

    public async Task<IReadOnlyList<Adapter>> ReadAdaptersAsync(CancellationToken cancellationToken)
    {
        // Как настоящая система, отвечает не сразу: вызывающий не должен полагаться на синхронный ответ
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        if (AdaptersFailure != null) throw AdaptersFailure;
        CatchUp();
        return [.. Adapters];
    }

    public async Task WriteIpv4Async(Adapter adapter, Ipv4Settings settings, CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        Calls.Add("ipv4 " + adapter.Name);
        if (Ipv4Failure != null) throw Ipv4Failure;
        Adapters.Single(candidate => candidate.Id == adapter.Id).Configured = settings.Clone();
    }

    public async Task<ProxyState> ReadProxyAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        if (ProxyReadFailure != null) throw ProxyReadFailure;
        // Как Windows, отдаёт исключения строкой: флажки получаются только из неё
        return new ProxyState(Reread(SystemProxy), Reread(WinHttp));
    }

    public async Task WriteSystemProxyAsync(SystemProxyState state, CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        Calls.Add("system proxy");
        if (SystemProxyFailure != null) throw SystemProxyFailure;
        SystemProxy = Reread(state);
    }

    public async Task WriteWinHttpProxyAsync(ProxySettings settings, CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        Calls.Add("winhttp");
        if (WinHttpFailure != null) throw WinHttpFailure;
        WinHttp = Reread(settings);
    }

    public async Task<IReadOnlyList<DefaultRoute>> ReadDefaultRoutesAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        CatchUp();
        return [.. DefaultRoutes];
    }

    public async Task<Guid?> ReadInternetAdapterAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        return InternetAdapter;
    }

    public async Task AddDefaultRouteAsync(Adapter adapter, string gateway, CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        Calls.Add("add route " + gateway);
        if (RouteFailure != null) throw RouteFailure;
        if (!RouteIgnored) DefaultRoutes.Add(new DefaultRoute(Guid.Parse(adapter.Id), gateway));
    }

    public async Task<bool> PingAsync(string address, TimeSpan timeout, CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        Calls.Add("ping " + address);
        if (LostPings > 0)
        {
            LostPings--;
            return false;
        }
        return Answering.Contains(address);
    }

    public async Task<bool> CanConnectAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        Calls.Add("connect " + host + ":" + port);
        return OpenPorts.Contains(host + ":" + port);
    }

    // Наступившие по часам изменения
    private void CatchUp()
    {
        if (time == null) return;
        foreach ((DateTimeOffset at, Action change) in _later.Where(item => item.At <= time.GetUtcNow()).ToList())
        {
            change();
            _later.Remove((at, change));
        }
    }

    private static ProxySettings Reread(ProxySettings settings)
    {
        return ProxySettings.FromRaw(settings.Enabled, settings.Server, settings.ToBypassList());
    }

    private static SystemProxyState Reread(SystemProxyState state)
    {
        return new SystemProxyState { Settings = Reread(state.Settings), AutoDetect = state.AutoDetect, AutoConfigUrl = state.AutoConfigUrl };
    }
}
