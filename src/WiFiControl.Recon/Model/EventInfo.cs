namespace WiFiControl.Recon.Model;

/// <summary>Событие компьютера, которое разведка записывает в ленту. Время ставит лента: при получении, по своим часам.</summary>
internal abstract record SystemEvent;

/// <summary>Уведомление WLAN (<c>WlanRegisterNotification</c>) с тем, что удалось достать из его данных.</summary>
/// <param name="Source">Источник: «ACM» или «MSM».</param>
/// <param name="Code">Код уведомления внутри источника.</param>
/// <param name="CodeName">Имя кода из wlanapi.h без префикса, например «connection_complete»; для неизвестного кода — «?».</param>
/// <param name="Ssid">Из данных уведомления; null, если в этом уведомлении SSID нет.</param>
/// <param name="Bssid">Точка доступа из данных уведомления MSM, вида «CC-2D-21-3B-83-96».</param>
/// <param name="SignalQuality">Качество сигнала из <c>signal_quality_change</c> (MSM), 0–100.</param>
/// <param name="ReasonCode">Код причины (<c>WLAN_REASON_CODE</c>); null, если в уведомлении его нет.</param>
/// <param name="Reason">Текст причины от Windows (<c>WlanReasonCodeToString</c>).</param>
internal sealed record WlanNotification(
    Guid Adapter,
    string Source,
    int Code,
    string CodeName,
    string? Ssid = null,
    string? WindowsProfileName = null,
    string? Bssid = null,
    int? SignalQuality = null,
    int? ReasonCode = null,
    string? Reason = null) : SystemEvent;

/// <summary>Смена адресов (<c>NetworkChange.NetworkAddressChanged</c>) с действующими адресами Wi-Fi-адаптеров в этот момент.</summary>
internal sealed record AddressesChanged(IReadOnlyList<AdapterAddresses> WifiAdapters) : SystemEvent;

/// <summary>Действующие адреса Wi-Fi-адаптера из сетевого стека.</summary>
/// <param name="Name">Имя подключения, например «Беспроводная сеть».</param>
/// <param name="Status">Рабочее состояние, например «Up».</param>
internal sealed record AdapterAddresses(Guid Id, string Name, string Status, ActiveIpv4 Active);

/// <summary>Сон, выход из сна или смена питания (<c>SystemEvents.PowerModeChanged</c>).</summary>
/// <param name="Mode">«Suspend», «Resume» или «StatusChange».</param>
internal sealed record PowerModeChanged(string Mode) : SystemEvent;

/// <summary>Подписка не удалась или не удалось прочитать данные к событию; остальные подписки работают.</summary>
/// <param name="What">Что не удалось, например «Подписка на уведомления WLAN».</param>
internal sealed record EventFailure(string What, Exception Error) : SystemEvent;
