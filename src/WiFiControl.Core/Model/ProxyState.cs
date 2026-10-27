namespace WiFiControl.Core.Model;

/// <summary>Системный прокси как он есть, вместе с тем, чем настройки прокси не управляют. Перенесено из NetSwitch.</summary>
public sealed class SystemProxyState
{
    public ProxySettings Settings { get; set; } = new();
    /// <summary>«Автоматическое определение параметров».</summary>
    public bool AutoDetect { get; set; }
    /// <summary>Адрес сценария автоматической настройки (PAC); пусто, если сценарий не задан.</summary>
    public string AutoConfigUrl { get; set; } = "";
}

/// <summary>Прочитанные системный прокси и WinHTTP-прокси.</summary>
public sealed class ProxyState(SystemProxyState systemProxy, ProxySettings winHttp)
{
    public SystemProxyState SystemProxy { get; } = systemProxy;
    public ProxySettings WinHttp { get; } = winHttp;
}
