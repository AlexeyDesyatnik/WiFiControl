namespace WiFiControl.Core.Model;

/// <summary>
/// Сетевые настройки компьютера. Единственное место, через которое ядро обращается к системе;
/// в тестах подменяется. Методы при неудаче бросают <see cref="InvalidOperationException"/> с объяснением, понятным человеку.
/// Перенесено из NetSwitch.
/// </summary>
public interface INetworkSystem
{
    /// <summary>Все адаптеры, видимые в «Сетевых подключениях», физические и виртуальные.</summary>
    Task<IReadOnlyList<Adapter>> ReadAdaptersAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Записывает настройки IPv4 на адаптер; работает и без соединения. Возвращается, когда записанное
    /// прочитано с адаптера. Выключенный адаптер при этом включается: иначе записать нельзя.
    /// </summary>
    Task WriteIpv4Async(Adapter adapter, Ipv4Settings settings, CancellationToken cancellationToken);

    /// <summary>
    /// Системный прокси того, кто запустил инструмент, и WinHTTP-прокси. Полный набор исключений частных сетей
    /// читается как флажок «Не через прокси для частных сетей».
    /// </summary>
    Task<ProxyState> ReadProxyAsync(CancellationToken cancellationToken);

    /// <summary>Записывает системный прокси целиком, вместе с автоопределением и сценарием, и сообщает о смене запущенным программам.</summary>
    Task WriteSystemProxyAsync(SystemProxyState state, CancellationToken cancellationToken);

    /// <summary>Записывает WinHTTP-прокси; выключенный прокси — это «напрямую».</summary>
    Task WriteWinHttpProxyAsync(ProxySettings settings, CancellationToken cancellationToken);

    /// <summary>
    /// Маршруты по умолчанию IPv4 (0.0.0.0/0) из таблицы маршрутов на всех адаптерах. Бывают и на отключённом
    /// адаптере с ручным шлюзом (ADR 0006).
    /// </summary>
    Task<IReadOnlyList<DefaultRoute>> ReadDefaultRoutesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Адаптер, через который Windows отправила бы трафик в интернет: маршрут, выбранный <c>GetBestRoute2</c>
    /// к внешнему адресу (ADR 0006). Ничего не отправляет. null, если маршрута нет.
    /// </summary>
    Task<Guid?> ReadInternetAdapterAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Добавляет маршрут по умолчанию через адаптер только в действующие настройки, как
    /// <c>netsh interface ipv4 add route 0.0.0.0/0 … store=active</c>: в постоянных он уже записан (ADR 0004).
    /// </summary>
    Task AddDefaultRouteAsync(Adapter adapter, string gateway, CancellationToken cancellationToken);

    /// <summary>Одна попытка ping; true, если ответ пришёл за <paramref name="timeout"/>.</summary>
    Task<bool> PingAsync(string address, TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>true, если TCP-соединение с портом установилось за <paramref name="timeout"/>. Соединение сразу закрывается.</summary>
    Task<bool> CanConnectAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>Маршрут по умолчанию IPv4.</summary>
/// <param name="Adapter">GUID адаптера, тот же, что в <see cref="Model.Adapter.Id"/>.</param>
/// <param name="Gateway">Следующий узел; 0.0.0.0 у маршрута без шлюза.</param>
public sealed record DefaultRoute(Guid Adapter, string Gateway);
