using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace WiFiControl.Core.Model;

/// <summary>Защита, с которой инструмент сам создаёт профиль сети Windows. 802.1X и WEP — только через системное меню.</summary>
public enum NewNetworkSecurity
{
    Open,
    Wpa2Personal,
    /// <summary>Только WPA3 (SAE).</summary>
    Wpa3Personal,
    /// <summary>Смешанный режим: WPA3 (SAE), а с точками доступа без него — WPA2.</summary>
    Wpa2Wpa3Personal,
}

/// <summary>Профиль сети Windows, который переключение создаёт для новой сети.</summary>
/// <param name="Xml">XML профиля; в нём пароль открытым текстом, поэтому он нигде не сохраняется и не показывается.</param>
public sealed class NewWindowsProfile(string name, string xml)
{
    public string Name { get; } = name;

    public string Xml { get; } = xml;
}

/// <summary>XML профиля сети Windows (схема WLAN_profile): построение для новой сети и замена пароля в существующем.</summary>
public static class WindowsProfileXml
{
    private static readonly XNamespace V1 = "http://www.microsoft.com/networking/WLAN/profile/v1";
    // transitionMode появился в четвёртой версии схемы
    private static readonly XNamespace V4 = "http://www.microsoft.com/networking/WLAN/profile/v4";

    /// <summary>Защита для профиля новой сети по тому, что объявляет точка доступа; null, если такой профиль инструмент не создаёт.</summary>
    public static NewNetworkSecurity? SecurityFor(WifiSecurity security) => security switch
    {
        WifiSecurity.Open => NewNetworkSecurity.Open,
        WifiSecurity.Wpa2Personal => NewNetworkSecurity.Wpa2Personal,
        // Смешанный режим Windows в списке сетей показывает как WPA3, а с точкой доступа только на WPA3 он работает так же
        WifiSecurity.Wpa3Personal => NewNetworkSecurity.Wpa2Wpa3Personal,
        _ => null,
    };

    /// <summary>Пароль может быть ключом из 64 шестнадцатеричных цифр: у WPA3 (SAE) ключа нет, только парольная фраза.</summary>
    public static bool AcceptsKey(NewNetworkSecurity security) => security == NewNetworkSecurity.Wpa2Personal;

    /// <summary>Профиль сети Windows для новой сети; имя профиля — ключ сети.</summary>
    /// <param name="network">Ключ сети (ADR 0007).</param>
    /// <param name="password">Пароль (<see cref="WifiPassword"/>); у открытой сети — пустой.</param>
    /// <param name="autoConnect">Режим автоподключения: Windows подключается к сети сама.</param>
    public static NewWindowsProfile Build(string network, NewNetworkSecurity security, string password, bool autoConnect) =>
        Build(network, NetworkKey.ToBytes(network), security, password, autoConnect, hidden: false);

    /// <summary>Имя скрытой сети должно помещаться в 32 байта UTF-8 и записываться в XML профиля сети Windows.</summary>
    public static bool IsValidHiddenName(string name)
    {
        try
        {
            XmlConvert.VerifyXmlChars(name);
            return Encoding.UTF8.GetByteCount(name) is > 0 and <= 32;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    /// <summary>Профиль скрытой сети. Введённое имя — текст и одновременно ключ сети (ADR 0007).</summary>
    public static NewWindowsProfile BuildHidden(string name, NewNetworkSecurity security, string password, bool autoConnect)
    {
        if (!IsValidHiddenName(name)) throw new ArgumentException("Имя сети должно занимать от 1 до 32 байт в UTF-8.", nameof(name));
        return Build(name, Encoding.UTF8.GetBytes(name), security, password, autoConnect, hidden: true);
    }

    private static NewWindowsProfile Build(string network, byte[] ssid, NewNetworkSecurity security, string password, bool autoConnect, bool hidden)
    {
        bool open = security == NewNetworkSecurity.Open;
        if (open ? password.Length > 0 : !WifiPassword.IsValid(password, AcceptsKey(security)))
            throw new ArgumentException(open ? "У открытой сети нет пароля." : "Пароль не подходит для сети.", nameof(password));

        var authEncryption = new XElement(V1 + "authEncryption",
            new XElement(V1 + "authentication", security switch
            {
                NewNetworkSecurity.Open => "open",
                NewNetworkSecurity.Wpa2Personal => "WPA2PSK",
                _ => "WPA3SAE",
            }),
            new XElement(V1 + "encryption", open ? "none" : "AES"),
            new XElement(V1 + "useOneX", "false"));
        if (security == NewNetworkSecurity.Wpa2Wpa3Personal) authEncryption.Add(new XElement(V4 + "transitionMode", "true"));

        var securityElement = new XElement(V1 + "security", authEncryption);
        if (!open) securityElement.Add(SharedKey(password));

        var root = new XElement(V1 + "WLANProfile",
            new XElement(V1 + "name", network),
            new XElement(V1 + "SSIDConfig",
                new XElement(V1 + "SSID",
                    new XElement(V1 + "hex", Convert.ToHexString(ssid)),
                    new XElement(V1 + "name", network)),
                hidden ? new XElement(V1 + "nonBroadcast", true) : null),
            new XElement(V1 + "connectionType", "ESS"),
            new XElement(V1 + "connectionMode", autoConnect ? "auto" : "manual"),
            new XElement(V1 + "MSM", securityElement));
        return new NewWindowsProfile(network, root.ToString());
    }

    /// <summary>
    /// «Ввести пароль заново»: тот же профиль сети Windows с новым паролем, остальное без изменений.
    /// Профиль без пароля (открытый, 802.1X) — <see cref="InvalidOperationException"/>.
    /// </summary>
    public static string WithPassword(string xml, string password)
    {
        if (!WifiPassword.IsValid(password)) throw new ArgumentException("Пароль не подходит для сети.", nameof(password));

        XDocument document = XDocument.Parse(xml);
        XElement root = document.Root!;
        XElement? security = root.Element(V1 + "MSM")?.Element(V1 + "security");
        XElement? sharedKey = security?.Element(V1 + "sharedKey");
        string? name = root.Element(V1 + "name")?.Value;
        if (sharedKey == null) throw new InvalidOperationException("В профиле сети Windows «" + name + "» нет пароля, который можно заменить.");
        if (WifiPassword.IsKey(password) && security!.Element(V1 + "authEncryption")?.Element(V1 + "authentication")?.Value == "WPA3SAE")
        {
            throw new InvalidOperationException("Сеть «" + name + "» с WPA3 принимает только пароль от 8 до "
                + WifiPassword.MaxPassphraseLength + " символов, а не ключ из 64 шестнадцатеричных цифр.");
        }
        sharedKey.ReplaceWith(SharedKey(password));
        return document.ToString();
    }

    // Windows шифрует пароль сама, когда сохраняет профиль
    private static XElement SharedKey(string password)
    {
        return new XElement(V1 + "sharedKey",
            new XElement(V1 + "keyType", WifiPassword.IsKey(password) ? "networkKey" : "passPhrase"),
            new XElement(V1 + "protected", "false"),
            new XElement(V1 + "keyMaterial", password));
    }
}
