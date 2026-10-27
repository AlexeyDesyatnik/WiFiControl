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
}
