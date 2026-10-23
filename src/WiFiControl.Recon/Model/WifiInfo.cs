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
/// Точка доступа, к которой подключён адаптер, из списка BSS (<c>WlanGetNetworkBssList</c>):
/// в текущем подключении частоты нет.
/// </summary>
/// <param name="FrequencyKhz">Центральная частота канала, кГц.</param>
/// <param name="BandGhz">Диапазон, ГГц: 2.4, 5 или 6.</param>
/// <param name="Rssi">Уровень сигнала, дБм.</param>
internal sealed record ConnectedBss(int FrequencyKhz, float BandGhz, int Channel, int Rssi);
