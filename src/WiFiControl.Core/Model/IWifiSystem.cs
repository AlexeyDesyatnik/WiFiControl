namespace WiFiControl.Core.Model;

/// <summary>
/// Wi-Fi компьютера через Native Wifi API. Отдельно от <see cref="INetworkSystem"/>: там настройки адаптеров,
/// здесь сети и подключения. В тестах подменяется. Методы при неудаче бросают <see cref="InvalidOperationException"/>
/// с объяснением, понятным человеку.
/// </summary>
public interface IWifiSystem
{
    /// <summary>Wi-Fi-адаптеры, которые видит WLAN API.</summary>
    Task<IReadOnlyList<WifiAdapter>> ReadAdaptersAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Записи списка видимых сетей из кеша Windows, без сканирования. У одной сети бывает несколько записей:
    /// по одной на каждый её профиль сети Windows и ещё одна без профиля.
    /// </summary>
    Task<IReadOnlyList<VisibleNetwork>> ReadVisibleNetworksAsync(Guid adapter, CancellationToken cancellationToken);

    /// <summary>Байты имени сети, к которой адаптер подключён; null, если не подключён.</summary>
    Task<byte[]?> ReadCurrentNetworkAsync(Guid adapter, CancellationToken cancellationToken);

    /// <summary>Профили сетей Windows этого адаптера.</summary>
    Task<IReadOnlyList<WindowsProfile>> ReadWindowsProfilesAsync(Guid adapter, CancellationToken cancellationToken);

    /// <summary>XML профиля сети Windows этого адаптера; пароль в нём зашифрован.</summary>
    Task<string> ReadWindowsProfileXmlAsync(Guid adapter, string name, CancellationToken cancellationToken);

    /// <summary>
    /// Сохраняет профиль сети Windows по XML (<see cref="WindowsProfileXml"/>): новый — для всех пользователей,
    /// перезаписанный — с прежним владельцем. Профиль с тем же именем без <paramref name="overwrite"/> не перезаписывается —
    /// это отказ.
    /// </summary>
    Task SaveWindowsProfileAsync(Guid adapter, string xml, bool overwrite, CancellationToken cancellationToken);

    /// <summary>Удаляет профиль сети Windows этого адаптера.</summary>
    Task DeleteWindowsProfileAsync(Guid adapter, string name, CancellationToken cancellationToken);

    /// <summary>Запускает сканирование и ждёт его завершения. О завершении приходит и уведомление.</summary>
    Task ScanAsync(Guid adapter, CancellationToken cancellationToken);

    /// <summary>Отключает адаптер от сети и ждёт отключения.</summary>
    Task DisconnectAsync(Guid adapter, CancellationToken cancellationToken);

    /// <summary>
    /// Просит Windows подключиться через профиль сети Windows и не ждёт: чем кончилось, сообщают уведомления
    /// <see cref="WifiConnected"/> и <see cref="WifiConnectionFailed"/>.
    /// </summary>
    Task ConnectAsync(Guid adapter, string windowsProfile, CancellationToken cancellationToken);

    /// <summary>
    /// Подписка на уведомления; освобождение её снимает. Обратный вызов приходит в чужом потоке.
    /// </summary>
    IDisposable Watch(Action<WifiNotification> onNotification);
}

/// <summary>Wi-Fi-адаптер, как его видит WLAN API. <see cref="Id"/> совпадает с GUID адаптера в <see cref="Adapter.Id"/>.</summary>
public sealed record WifiAdapter(Guid Id, string Description);

/// <summary>Защита сети, как её объявляет точка доступа.</summary>
public enum WifiSecurity
{
    Open,
    Wep,
    WpaPersonal,
    Wpa2Personal,
    Wpa3Personal,
    /// <summary>802.1X: WPA, WPA2 и WPA3-Enterprise.</summary>
    Enterprise,
    Other
}

/// <summary>Запись списка видимых сетей.</summary>
/// <param name="Ssid">Байты имени; пустые у скрытой сети.</param>
/// <param name="Signal">Качество сигнала, 0–100.</param>
/// <param name="WindowsProfileName">Сопоставленный профиль сети Windows; пусто, если запись без профиля.</param>
public sealed record VisibleNetwork(byte[] Ssid, int Signal, WifiSecurity Security, string WindowsProfileName);

/// <summary>Профиль сети Windows.</summary>
/// <param name="Network">Ключ сети из имени в профиле (<see cref="NetworkKey"/>).</param>
public sealed record WindowsProfile(string Name, string Network);

/// <summary>Уведомление WLAN об адаптере.</summary>
public abstract record WifiNotification(Guid Adapter);

/// <summary>Адаптер подключился к сети.</summary>
public sealed record WifiConnected(Guid Adapter, string Network) : WifiNotification(Adapter);

/// <summary>Подключиться не удалось: попытка подключения закончилась неудачей.</summary>
/// <param name="Reason">
/// Код причины WLAN. Почти всегда общий — «сеть недоступна»; настоящая причина приходит раньше,
/// в <see cref="WifiConnectionAttemptFailed"/> (docs/connect-check.md).
/// </param>
public sealed record WifiConnectionFailed(Guid Adapter, string Network, int Reason) : WifiNotification(Adapter);

/// <summary>Неудачная попытка подключения; сразу за ней приходит <see cref="WifiConnectionFailed"/>.</summary>
/// <param name="Reason">Код причины WLAN: здесь настоящая причина, например неверный пароль.</param>
public sealed record WifiConnectionAttemptFailed(Guid Adapter, string Network, int Reason) : WifiNotification(Adapter);

/// <summary>Адаптер отключился от сети.</summary>
public sealed record WifiDisconnected(Guid Adapter, string Network) : WifiNotification(Adapter);

/// <summary>Сканирование закончилось, удачно или нет: список сетей в кеше обновлён.</summary>
public sealed record WifiScanCompleted(Guid Adapter) : WifiNotification(Adapter);
