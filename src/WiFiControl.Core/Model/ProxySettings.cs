namespace WiFiControl.Core.Model;

/// <summary>
/// Настройки прокси, заданного вручную. Действуют сразу на системный прокси и на WinHTTP-прокси.
/// Оба флажка — отдельные поля: в исключения они превращаются только при записи в систему.
/// </summary>
public sealed class ProxySettings
{
    private const string LocalMarker = "<local>";

    /// <summary>
    /// Исключения флажка «Не через прокси для частных сетей». Шаблоны исключений не умеют задавать диапазон,
    /// поэтому 172.16.0.0/12 — по записи на каждую вторую цифру (ADR 0006).
    /// </summary>
    private static readonly string[] PrivateNetworks =
        ["10.*", "192.168.*", .. Enumerable.Range(16, 16).Select(second => "172." + second + ".*")];

    public bool Enabled { get; set; }
    /// <summary>Сервер в виде адрес:порт.</summary>
    public string Server { get; set; } = "";
    public List<string> Exceptions { get; } = [];
    /// <summary>«Не использовать прокси для локальных адресов» (<c>&lt;local&gt;</c>).</summary>
    public bool BypassLocal { get; set; }
    /// <summary>«Не через прокси для частных сетей»: 10.*, 172.16.* … 172.31.*, 192.168.*.</summary>
    public bool PrivateNetworksDirect { get; set; }

    /// <summary>
    /// Собирает настройки из того вида, в котором их хранит Windows: исключения одной строкой через «;».
    /// Полный набор исключений частных сетей становится флажком, неполный остаётся обычными исключениями.
    /// Перенесено из NetSwitch.
    /// </summary>
    public static ProxySettings FromRaw(bool enabled, string? server, string? bypassList)
    {
        var settings = new ProxySettings { Enabled = enabled, Server = (server ?? "").Trim() };

        foreach (string entry in (bypassList ?? "").Split([';', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(entry, LocalMarker, StringComparison.OrdinalIgnoreCase))
                settings.BypassLocal = true;
            else
                settings.Exceptions.Add(entry);
        }

        if (PrivateNetworks.All(network => settings.Exceptions.Contains(network, StringComparer.OrdinalIgnoreCase)))
        {
            settings.PrivateNetworksDirect = true;
            settings.Exceptions.RemoveAll(entry => PrivateNetworks.Contains(entry, StringComparer.OrdinalIgnoreCase));
        }
        return settings;
    }

    public ProxySettings Clone()
    {
        var copy = new ProxySettings { Enabled = Enabled, Server = Server, BypassLocal = BypassLocal, PrivateNetworksDirect = PrivateNetworksDirect };
        copy.Exceptions.AddRange(Exceptions);
        return copy;
    }

    /// <summary>Исключения в том виде, в котором их хранит Windows, вместе с тем, во что превращаются флажки.</summary>
    public string ToBypassList()
    {
        var entries = new List<string>(Exceptions);
        if (PrivateNetworksDirect)
            entries.AddRange(PrivateNetworks.Where(network => !Exceptions.Contains(network, StringComparer.OrdinalIgnoreCase)));
        if (BypassLocal) entries.Add(LocalMarker);
        return string.Join(";", entries);
    }

    /// <summary>Два выключенных прокси одинаковы независимо от того, что осталось в полях сервера и исключений. Перенесено из NetSwitch.</summary>
    public bool SameAs(ProxySettings other)
    {
        if (Enabled != other.Enabled) return false;
        if (!Enabled) return true;

        return string.Equals(Server, other.Server, StringComparison.OrdinalIgnoreCase)
            && BypassLocal == other.BypassLocal
            && PrivateNetworksDirect == other.PrivateNetworksDirect
            && new HashSet<string>(Exceptions, StringComparer.OrdinalIgnoreCase).SetEquals(other.Exceptions);
    }
}
