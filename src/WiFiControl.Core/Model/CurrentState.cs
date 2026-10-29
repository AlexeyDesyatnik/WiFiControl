namespace WiFiControl.Core.Model;

/// <summary>Строка списка сетей: одна на ключ сети.</summary>
/// <param name="Signal">Качество сигнала самой сильной записи, 0–100.</param>
/// <param name="WindowsProfileName">Профиль сети Windows самой сильной записи; пусто, если его нет.</param>
/// <param name="Profile">Профиль для сети; null, если профили неизвестны (файл профилей не прочитан).</param>
/// <param name="IsBound">Сеть привязана к профилю, а не получает профиль по умолчанию.</param>
public sealed record NetworkRow(string Network, int Signal, WifiSecurity Security, string WindowsProfileName, bool IsCurrent,
    Profile? Profile, bool IsBound);

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

    /// <summary>Видимые сети: текущая первой, остальные по убыванию сигнала.</summary>
    public IReadOnlyList<NetworkRow> Networks { get; private init; } = [];

    /// <param name="profiles">Профили и привязки; null, если файл профилей не прочитан.</param>
    public static async Task<CurrentState> ReadAsync(INetworkSystem network, IWifiSystem wifi, ProfileSet? profiles, CancellationToken cancellationToken)
    {
        ProxyState proxy = await network.ReadProxyAsync(cancellationToken).ConfigureAwait(false);
        // Выбор из нескольких Wi-Fi-адаптеров появится позже, пока — первый
        WifiAdapter? wifiAdapter = (await wifi.ReadAdaptersAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault();
        if (wifiAdapter == null) return new CurrentState { Proxy = proxy };

        Adapter? adapter = (await network.ReadAdaptersAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(candidate => Guid.TryParse(candidate.Id, out Guid id) && id == wifiAdapter.Id);
        byte[]? current = await wifi.ReadCurrentNetworkAsync(wifiAdapter.Id, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<VisibleNetwork> entries = await wifi.ReadVisibleNetworksAsync(wifiAdapter.Id, cancellationToken).ConfigureAwait(false);

        string? currentKey = current == null ? null : NetworkKey.FromBytes(current);
        return new CurrentState
        {
            WifiAdapter = wifiAdapter,
            Adapter = adapter,
            Network = currentKey,
            Profile = currentKey == null ? null : profiles?.ProfileFor(currentKey),
            IsBound = currentKey != null && profiles != null && profiles.Bindings.ContainsKey(currentKey),
            Proxy = proxy,
            Networks = BuildList(entries, currentKey, profiles),
        };
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
