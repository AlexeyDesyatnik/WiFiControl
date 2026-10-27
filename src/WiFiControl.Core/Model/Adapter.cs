namespace WiFiControl.Core.Model;

// Перенесено из NetSwitch

public enum AdapterState
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

/// <summary>Адаптер компьютера вместе с его настройками IPv4.</summary>
public sealed class Adapter
{
    /// <summary>GUID адаптера в фигурных скобках.</summary>
    public string Id { get; set; } = "";
    /// <summary>Имя, под которым адаптер виден в «Сетевых подключениях».</summary>
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string PnpDeviceId { get; set; } = "";
    public string MacAddress { get; set; } = "";
    public bool IsPhysical { get; set; }
    /// <summary>Известно только для включённого адаптера.</summary>
    public bool IsWireless { get; set; }
    public AdapterState State { get; set; }

    /// <summary>То, что на адаптере настроено (действует и когда соединения нет).</summary>
    public Ipv4Settings Configured { get; set; } = new();
    /// <summary>То, что адаптер имеет прямо сейчас, включая полученное автоматически. Пусто, если соединения нет.</summary>
    public ActiveIpv4 Active { get; set; } = new();
}

/// <summary>Адреса, которые адаптер реально имеет в данный момент.</summary>
public sealed class ActiveIpv4
{
    /// <summary>Адреса с длиной префикса: 10.68.12.37/24.</summary>
    public List<string> Addresses { get; } = [];
    public List<string> Gateways { get; } = [];
    public List<string> Dns { get; } = [];
    /// <summary>Windows обнаружила в сети другой компьютер с таким же адресом.</summary>
    public bool HasConflict { get; set; }
}

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
