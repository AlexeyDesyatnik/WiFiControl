namespace WiFiControl.Core.Model;

/// <summary>
/// Настройки прокси, заданного вручную. Действуют сразу на системный прокси и на WinHTTP-прокси.
/// Оба флажка — отдельные поля: в исключения они превращаются только при записи в систему.
/// </summary>
public sealed class ProxySettings
{
    public bool Enabled { get; set; }
    /// <summary>Сервер в виде адрес:порт.</summary>
    public string Server { get; set; } = "";
    public List<string> Exceptions { get; } = [];
    /// <summary>«Не использовать прокси для локальных адресов» (<c>&lt;local&gt;</c>).</summary>
    public bool BypassLocal { get; set; }
    /// <summary>«Не через прокси для частных сетей»: 10.*, 172.16.* … 172.31.*, 192.168.*.</summary>
    public bool PrivateNetworksDirect { get; set; }

    public ProxySettings Clone()
    {
        var copy = new ProxySettings { Enabled = Enabled, Server = Server, BypassLocal = BypassLocal, PrivateNetworksDirect = PrivateNetworksDirect };
        copy.Exceptions.AddRange(Exceptions);
        return copy;
    }
}
