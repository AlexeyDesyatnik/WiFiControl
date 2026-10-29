namespace WiFiControl.Core.Model;

/// <summary>Строка списка сетей: одна на ключ сети.</summary>
/// <param name="Signal">Качество сигнала самой сильной записи, 0–100.</param>
/// <param name="WindowsProfileName">Профиль сети Windows самой сильной записи; пусто, если его нет.</param>
/// <param name="Profile">Профиль для сети; null, если профили неизвестны (файл профилей не прочитан).</param>
/// <param name="IsBound">Сеть привязана к профилю, а не получает профиль по умолчанию.</param>
public sealed record NetworkRow(string Network, int Signal, WifiSecurity Security, string WindowsProfileName, bool IsCurrent,
    Profile? Profile, bool IsBound);

/// <summary>Признак проблемы в блоке «Сейчас».</summary>
public enum Problem
{
    /// <summary>Адрес не получен: адреса нет или он самоназначенный 169.254.x.x.</summary>
    NoAddress,
    /// <summary>Windows обнаружила в сети другой компьютер с тем же адресом.</summary>
    AddressConflict,
    /// <summary>Адрес есть, но маршрута по умолчанию через Wi-Fi-адаптер нет: интернета нет.</summary>
    NoDefaultRoute,
    /// <summary>Интернет сейчас идёт через проводной адаптер (ADR 0006).</summary>
    InternetViaEthernet,
}

/// <summary>Снимок текущего состояния для блока «Сейчас» и списка сетей.</summary>
public sealed class CurrentState
{
    /// <summary>Wi-Fi-адаптер, с которым работает инструмент; null, если Wi-Fi-адаптеров нет.</summary>
    public WifiAdapter? WifiAdapter { get; private init; }

    /// <summary>Тот же адаптер со своими настройками IPv4; null, если его нет среди сетевых адаптеров.</summary>
    public Adapter? Adapter { get; private init; }

    /// <summary>Ключ текущей сети; null, если адаптер не подключён.</summary>
    public string? Network { get; private init; }

    /// <summary>Профиль для текущей сети; null, если сети нет или профили неизвестны.</summary>
    public Profile? Profile { get; private init; }

    /// <summary>Текущая сеть привязана к профилю.</summary>
    public bool IsBound { get; private init; }

    public required ProxyState Proxy { get; init; }

    /// <summary>Адаптер, через который Windows отправляет трафик в интернет; null, если маршрута в интернет нет.</summary>
    public Adapter? InternetAdapter { get; private init; }

    /// <summary>Признаки проблем по порядку <see cref="Model.Problem"/>.</summary>
    public IReadOnlyList<Problem> Problems { get; private init; } = [];

    /// <summary>
    /// Ожидаемый профиль (<see cref="Model.Mismatch.ExpectedProfile"/>); null, если сети нет или профили неизвестны.
    /// Не всегда совпадает с <see cref="Profile"/>: после «Применить профиль…» ожидается применённый.
    /// </summary>
    public Profile? ExpectedProfile { get; private init; }

    /// <summary>Расхождение с ожидаемым профилем; null, если его нет или не с чем сравнить.</summary>
    public Mismatch? Mismatch { get; private init; }

    /// <summary>Видимые сети: текущая первой, остальные по убыванию сигнала.</summary>
    public IReadOnlyList<NetworkRow> Networks { get; private init; } = [];

    /// <param name="profiles">Профили и привязки; null, если файл профилей не прочитан.</param>
    /// <param name="last">Последнее применение; null, если его не было.</param>
    public static async Task<CurrentState> ReadAsync(INetworkSystem network, IWifiSystem wifi, ProfileSet? profiles, LastApplication? last,
        CancellationToken cancellationToken)
    {
        ProxyState proxy = await network.ReadProxyAsync(cancellationToken).ConfigureAwait(false);
        // Выбор из нескольких Wi-Fi-адаптеров появится позже, пока — первый
        WifiAdapter? wifiAdapter = (await wifi.ReadAdaptersAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault();
        if (wifiAdapter == null) return new CurrentState { Proxy = proxy };

        IReadOnlyList<Adapter> adapters = await network.ReadAdaptersAsync(cancellationToken).ConfigureAwait(false);
        Adapter? adapter = adapters.FirstOrDefault(candidate => IsAdapter(candidate, wifiAdapter.Id));
        IReadOnlyList<DefaultRoute> routes = await network.ReadDefaultRoutesAsync(cancellationToken).ConfigureAwait(false);
        Guid? internetId = await network.ReadInternetAdapterAsync(cancellationToken).ConfigureAwait(false);
        Adapter? internetAdapter = internetId == null ? null : adapters.FirstOrDefault(candidate => IsAdapter(candidate, internetId.Value));
        byte[]? current = await wifi.ReadCurrentNetworkAsync(wifiAdapter.Id, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<VisibleNetwork> entries = await wifi.ReadVisibleNetworksAsync(wifiAdapter.Id, cancellationToken).ConfigureAwait(false);

        string? currentKey = current == null ? null : NetworkKey.FromBytes(current);
        Profile? expected = currentKey == null || profiles == null ? null : Model.Mismatch.ExpectedProfile(last, currentKey, profiles);
        return new CurrentState
        {
            WifiAdapter = wifiAdapter,
            Adapter = adapter,
            Network = currentKey,
            Profile = currentKey == null ? null : profiles?.ProfileFor(currentKey),
            IsBound = currentKey != null && profiles != null && profiles.Bindings.ContainsKey(currentKey),
            Proxy = proxy,
            InternetAdapter = internetAdapter,
            Problems = FindProblems(currentKey == null ? null : adapter, routes, internetAdapter),
            ExpectedProfile = expected,
            Mismatch = expected == null || adapter == null ? null : FindMismatch(expected, adapter, proxy),
            Networks = BuildList(entries, currentKey, profiles),
        };
    }

    private static Mismatch? FindMismatch(Profile expected, Adapter adapter, ProxyState proxy)
    {
        IReadOnlyList<Difference> differences = Model.Mismatch.Find(expected, adapter.Configured, proxy);
        return differences.Count == 0 ? null : new Mismatch(expected, differences);
    }

    private static bool IsAdapter(Adapter candidate, Guid id) => Guid.TryParse(candidate.Id, out Guid candidateId) && candidateId == id;

    /// <param name="adapter">Wi-Fi-адаптер, если он подключён к сети.</param>
    private static List<Problem> FindProblems(Adapter? adapter, IReadOnlyList<DefaultRoute> routes, Adapter? internetAdapter)
    {
        var problems = new List<Problem>();
        if (adapter != null)
        {
            // Адрес-дубликат в действующие не попадает, и без признака конфликта это выглядело бы как «адрес не получен».
            // Без адреса нет и маршрута, поэтому о маршруте говорим только при рабочем адресе.
            if (adapter.Active.HasConflict)
                problems.Add(Problem.AddressConflict);
            else if (!adapter.Active.Addresses.Any(address => !IsSelfAssigned(address)))
                problems.Add(Problem.NoAddress);
            else if (!routes.Any(route => IsAdapter(adapter, route.Adapter)))
                problems.Add(Problem.NoDefaultRoute);
        }
        // Виртуальный адаптер — обычно VPN: это не «через Ethernet»
        if (internetAdapter != null && internetAdapter.IsPhysical && !internetAdapter.IsWireless)
            problems.Add(Problem.InternetViaEthernet);
        return problems;
    }

    /// <param name="address">Адрес с длиной префикса или без неё.</param>
    private static bool IsSelfAssigned(string address)
    {
        return Ipv4.TryParse(address.Split('/')[0], out uint value) && (value >> 16) == (169u << 8 | 254u);
    }

    private static List<NetworkRow> BuildList(IReadOnlyList<VisibleNetwork> entries, string? current, ProfileSet? profiles)
    {
        return entries
            // Скрытые сети без профиля сети Windows: для них в окне есть строка «Скрытая сеть…»
            .Where(entry => entry.Ssid.Length > 0)
            .GroupBy(entry => NetworkKey.FromBytes(entry.Ssid), NetworkKey.Comparer)
            .Select(group =>
            {
                // При равном сигнале — запись с профилем сети Windows: Windows дублирует запись без него
                VisibleNetwork strongest = group
                    .OrderByDescending(entry => entry.Signal)
                    .ThenByDescending(entry => entry.WindowsProfileName.Length > 0)
                    .First();
                return new NetworkRow(group.Key, strongest.Signal, strongest.Security, strongest.WindowsProfileName,
                    NetworkKey.Comparer.Equals(group.Key, current),
                    profiles?.ProfileFor(group.Key), profiles?.Bindings.ContainsKey(group.Key) ?? false);
            })
            .OrderByDescending(row => row.IsCurrent)
            .ThenByDescending(row => row.Signal)
            .ToList();
    }
}
