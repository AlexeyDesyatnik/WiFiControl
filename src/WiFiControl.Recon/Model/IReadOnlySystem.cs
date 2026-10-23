namespace WiFiControl.Recon.Model;

/// <summary>
/// Компьютер глазами разведочной сборки — единственная граница с Windows, в тестах подменяется.
/// Только читает: ни один метод ничего не меняет в системе. Каждый метод либо возвращает данные,
/// либо бросает исключение, в котором есть код ошибки Windows.
/// </summary>
internal interface IReadOnlySystem
{
    string ComputerName { get; }

    WindowsVersion ReadWindows();

    /// <summary>От чьего имени и с какими правами работает программа.</summary>
    ProcessIdentity ReadProcess();

    /// <summary>Сведения, по которым видно, запущена ли программа из Планировщика.</summary>
    LaunchOrigin ReadLaunch();

    /// <summary>Wi-Fi-адаптеры, которые видит WLAN API.</summary>
    IReadOnlyList<WifiAdapter> ReadWifiAdapters();

    /// <summary>Все адаптеры «Сетевых подключений», не только Wi-Fi.</summary>
    IReadOnlyList<NetworkConnection> ReadNetworkConnections();

    /// <returns>null, если адаптер не подключён.</returns>
    WifiConnection? ReadCurrentConnection(Guid adapter);

    /// <returns>null, если точки доступа с таким BSSID нет в списке BSS адаптера.</returns>
    ConnectedBss? ReadConnectedBss(Guid adapter, string bssid);
}

/// <summary>Источник времени; в тестах подменяется.</summary>
internal interface IClock
{
    DateTime Now { get; }
}

internal sealed class SystemClock : IClock
{
    public DateTime Now => DateTime.Now;
}
