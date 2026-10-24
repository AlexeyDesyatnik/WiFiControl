namespace WiFiControl.Recon.Model;

/// <summary>Wi-Fi-адаптер, как его видит WLAN API (<c>WlanEnumInterfaces</c>).</summary>
/// <param name="State">Состояние в названиях ManagedNativeWifi, например «Connected».</param>
internal sealed record WifiAdapter(Guid Id, string Description, string State);

/// <summary>Адаптер из «Сетевых подключений». С Wi-Fi-адаптером сопоставляется по GUID.</summary>
/// <param name="Name">Имя подключения, например «Беспроводная сеть».</param>
/// <param name="Status">Рабочее состояние, например «Up».</param>
internal sealed record NetworkConnection(Guid Id, string Name, string Description, string Status);

/// <summary>Текущее подключение Wi-Fi-адаптера (<c>WlanQueryInterface</c>, <c>current_connection</c>).</summary>
/// <param name="Bssid">Вида «CC-2D-21-3B-83-96».</param>
/// <param name="WindowsProfileName">Имя профиля сети Windows, через который адаптер подключён.</param>
/// <param name="Authentication">Алгоритм проверки подлинности в названиях ManagedNativeWifi, например «RSNA_PSK».</param>
/// <param name="Cipher">Шифр в названиях ManagedNativeWifi, например «CCMP».</param>
/// <param name="SignalQuality">Качество сигнала, 0–100.</param>
/// <param name="Mode">Как подключились, например «Profile».</param>
internal sealed record WifiConnection(
    string Ssid,
    string Bssid,
    string WindowsProfileName,
    bool IsSecurityEnabled,
    string Authentication,
    string Cipher,
    int SignalQuality,
    string Mode);

/// <summary>
/// Точка доступа из списка BSS адаптера (<c>WlanGetNetworkBssList</c>): частоты нет ни в текущем подключении,
/// ни в списке сетей.
/// </summary>
/// <param name="Ssid">Пусто у скрытой сети.</param>
/// <param name="Bssid">Вида «CC-2D-21-3B-83-96».</param>
/// <param name="FrequencyKhz">Центральная частота канала, кГц.</param>
/// <param name="BandGhz">Диапазон, ГГц: 2.4, 5 или 6.</param>
/// <param name="Rssi">Уровень сигнала, дБм.</param>
internal sealed record BssEntry(string Ssid, string Bssid, int FrequencyKhz, float BandGhz, int Channel, int Rssi);

/// <summary>
/// Запись списка сетей (<c>WlanGetAvailableNetworkList</c>). У сети с профилем сети Windows Windows отдаёт
/// по записи на каждый её профиль и ещё одну без профиля, поэтому одно имя может встретиться несколько раз.
/// </summary>
/// <param name="Ssid">Пусто у скрытой сети.</param>
/// <param name="BssType">В названиях ManagedNativeWifi, например «Infrastructure».</param>
/// <param name="SignalQuality">Качество сигнала, 0–100.</param>
/// <param name="WindowsProfileName">Пусто, если запись без профиля сети Windows.</param>
/// <param name="Authentication">Алгоритм проверки подлинности в названиях ManagedNativeWifi, например «RSNA_PSK».</param>
/// <param name="Cipher">Шифр в названиях ManagedNativeWifi, например «CCMP».</param>
internal sealed record AvailableNetwork(
    string Ssid,
    string BssType,
    bool IsConnectable,
    int SignalQuality,
    bool IsSecurityEnabled,
    string WindowsProfileName,
    string Authentication,
    string Cipher);

/// <summary>Профиль сети Windows (<c>WlanGetProfileList</c>, <c>WlanGetProfile</c>) — без ключа сети.</summary>
/// <param name="Type">Для кого профиль, в названиях ManagedNativeWifi, например «AllUser».</param>
/// <param name="Position">Место в порядке предпочтения, с 0.</param>
/// <param name="Ssid">Из XML профиля; null, если элемента нет.</param>
/// <param name="ConnectionMode">Значение <c>connectionMode</c> из XML: «auto» или «manual»; null, если элемента нет.</param>
/// <param name="MacRandomization">Дочерние элементы <c>MacRandomization</c> из XML как есть; null, если элемента нет.</param>
internal sealed record WindowsProfile(
    string Name,
    string Type,
    int Position,
    string? Ssid,
    string? ConnectionMode,
    IReadOnlyList<XmlValue>? MacRandomization);

/// <summary>Элемент XML с текстовым значением.</summary>
internal sealed record XmlValue(string Name, string Value);
