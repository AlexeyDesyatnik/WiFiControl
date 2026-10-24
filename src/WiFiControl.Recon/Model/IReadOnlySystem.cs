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

    /// <summary>Точки доступа, которые адаптер видел при последнем сканировании.</summary>
    IReadOnlyList<BssEntry> ReadBssList(Guid adapter);

    /// <summary>
    /// Запускает сканирование (<c>WlanScan</c>) и ждёт уведомления о его конце. Настроек не меняет.
    /// </summary>
    /// <returns>false, если уведомление не пришло за <paramref name="timeout"/>.</returns>
    bool Scan(Guid adapter, TimeSpan timeout);

    /// <summary>Сети, которые видит адаптер, по итогам последнего сканирования.</summary>
    IReadOnlyList<AvailableNetwork> ReadAvailableNetworks(Guid adapter);

    /// <summary>Профили сетей Windows адаптера в порядке предпочтения. Ключ сети не запрашивается.</summary>
    IReadOnlyList<WindowsProfile> ReadWindowsProfiles(Guid adapter);

    /// <summary>Адаптеры «Сетевых подключений» (<c>Win32_NetworkAdapter</c>) с настроенными и действующими настройками IPv4.</summary>
    IReadOnlyList<Adapter> ReadAdapters();

    /// <summary>Пользователь, вошедший в сеанс программы: системный прокси — его, даже если права получены под другой учётной записью.</summary>
    InteractiveUser ReadInteractiveUser();

    /// <summary>Системный прокси пользователя вместе с автоопределением и сценарием настройки.</summary>
    SystemProxyState ReadSystemProxy(string userSid);

    ProxyPolicy ReadProxyPolicy(string userSid);

    ProxySettings ReadWinHttpProxy();

    /// <summary>Интерфейсы IPv4 сетевого стека с их метриками.</summary>
    IReadOnlyList<IpInterface> ReadIpInterfaces();

    /// <summary>Маршруты по умолчанию (0.0.0.0/0) из таблицы маршрутов IPv4.</summary>
    IReadOnlyList<Route> ReadDefaultRoutes();

    /// <summary>Маршрут, которым Windows отправила бы трафик на <paramref name="destination"/>. Сам трафик не отправляется.</summary>
    /// <returns>null, если маршрута к адресу нет.</returns>
    BestRoute? ReadBestRoute(string destination);

    /// <summary>Записи о доступе к местоположению пользователя: доступ выдаётся ему, а не процессу с правами администратора.</summary>
    LocationConsent ReadUserLocationConsent(string userSid);

    /// <summary>Записи о доступе к местоположению для всего компьютера.</summary>
    LocationConsent ReadMachineLocationConsent();
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
