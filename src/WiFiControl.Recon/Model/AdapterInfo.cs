namespace WiFiControl.Recon.Model;

// Перенесено из NetSwitch

internal enum AdapterState
{
    /// <summary>Включён, соединение есть.</summary>
    Connected,
    /// <summary>Включён, но кабель не подключён или Wi-Fi не в сети.</summary>
    NoLink,
    /// <summary>Включён и находится в процессе подключения.</summary>
    Connecting,
    /// <summary>Выключен.</summary>
    Disabled,
    /// <summary>Устройство отсутствует или неисправно.</summary>
    Unavailable
}

/// <summary>Адаптер из «Сетевых подключений» (<c>Win32_NetworkAdapter</c>) вместе с его настройками IPv4.</summary>
/// <param name="Id">GUID адаптера; <see cref="Guid.Empty"/>, если WMI его не отдала.</param>
/// <param name="Name">Имя, под которым адаптер виден в «Сетевых подключениях».</param>
internal sealed record Adapter(Guid Id, string Name, string Description, string PnpDeviceId, string MacAddress, bool IsPhysical, AdapterState State)
{
    /// <summary>Известно только для включённого адаптера.</summary>
    public bool IsWireless { get; init; }

    /// <summary>То, что на адаптере настроено (действует и когда соединения нет).</summary>
    public Ipv4Settings Configured { get; init; } = Ipv4Settings.Automatic;

    /// <summary>
    /// То, что адаптер имеет прямо сейчас, включая полученное автоматически; null без соединения —
    /// тогда стек отдаёт самоназначенный адрес 169.254.x.x и старые DNS, и они не читаются.
    /// </summary>
    public ActiveIpv4? Active { get; init; }
}

/// <summary>Настройки IPv4: способ получения адреса и DNS и заданные вручную значения.</summary>
/// <param name="AutoDns">При адресе, заданном вручную, DNS всегда задаётся вручную (возможно, пустым списком).</param>
internal sealed record Ipv4Settings(bool AutoAddress, string Address, string Mask, string Gateway, bool AutoDns, IReadOnlyList<string> Dns)
{
    public static readonly Ipv4Settings Automatic = new(true, "", "", "", true, []);
}

/// <summary>Адреса, которые адаптер реально имеет в данный момент.</summary>
/// <param name="Addresses">Адреса с длиной префикса: 10.68.12.37/24.</param>
/// <param name="HasConflict">Windows обнаружила в сети другой компьютер с таким же адресом.</param>
internal sealed record ActiveIpv4(IReadOnlyList<string> Addresses, IReadOnlyList<string> Gateways, IReadOnlyList<string> Dns, bool HasConflict);

internal static class AdapterClassifier
{
    // Шины, на которых не бывает настоящих сетевых устройств: программные адаптеры
    // (Hyper-V, VPN, виртуальные машины), Bluetooth, Wi-Fi Direct.
    private static readonly string[] VirtualPrefixes = [@"ROOT\", @"SWD\", @"BTH\", @"BTHENUM\", "{"];

    /// <summary>
    /// Физический адаптер определяется по шине устройства. Незнакомая шина считается физической:
    /// лучше показать лишний адаптер, чем спрятать настоящий.
    /// </summary>
    public static bool IsPhysical(string? pnpDeviceId)
    {
        if (string.IsNullOrEmpty(pnpDeviceId)) return false;
        return !VirtualPrefixes.Any(prefix => pnpDeviceId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
