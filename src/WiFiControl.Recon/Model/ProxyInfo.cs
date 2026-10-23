namespace WiFiControl.Recon.Model;

// Перенесено из NetSwitch

/// <summary>Прокси, заданный вручную: включён или выключен, сервер, исключения.</summary>
/// <param name="Server">Сервер в виде адрес:порт.</param>
/// <param name="BypassLocal">Не использовать прокси для локальных адресов (<c>&lt;local&gt;</c> в списке исключений).</param>
internal sealed record ProxySettings(bool Enabled, string Server, IReadOnlyList<string> Exceptions, bool BypassLocal)
{
    private const string LocalMarker = "<local>";

    /// <summary>Собирает настройки из того вида, в котором их хранит Windows: исключения одной строкой через «;».</summary>
    public static ProxySettings FromRaw(bool enabled, string? server, string? bypassList)
    {
        var exceptions = new List<string>();
        bool bypassLocal = false;
        foreach (string entry in (bypassList ?? "").Split([';', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(entry, LocalMarker, StringComparison.OrdinalIgnoreCase))
                bypassLocal = true;
            else
                exceptions.Add(entry);
        }
        return new ProxySettings(enabled, (server ?? "").Trim(), exceptions, bypassLocal);
    }
}

/// <summary>Системный прокси пользователя вместе с автоопределением и сценарием настройки.</summary>
/// <param name="AutoDetect">«Автоматическое определение параметров».</param>
/// <param name="AutoConfigUrl">Адрес сценария настройки (PAC); пусто, если сценарий не задан.</param>
internal sealed record SystemProxyState(ProxySettings Settings, bool AutoDetect, string AutoConfigUrl);

/// <summary>Признаки того, что прокси на компьютере задаётся групповой политикой.</summary>
/// <param name="PerMachine">Политика «прокси один на компьютер, а не на пользователя».</param>
/// <param name="SetsValues">Политика сама задаёт значения прокси пользователю.</param>
/// <param name="ChangeLocked">Политика запрещает менять параметры прокси.</param>
internal sealed record ProxyPolicy(bool PerMachine, bool SetsValues, bool ChangeLocked)
{
    public bool IsManaged => PerMachine || SetsValues || ChangeLocked;
}

/// <summary>Пользователь, вошедший в сеанс, в котором работает программа. Системный прокси — его.</summary>
/// <param name="IsProcessOwner">Ложно, если права администратора получены под другой учётной записью.</param>
internal sealed record InteractiveUser(string Name, string Sid, bool IsProcessOwner);
