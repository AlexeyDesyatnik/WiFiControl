using System.Globalization;

namespace WiFiControl.Core.Model;

/// <summary>Коды причин неудачи WLAN (WLAN_REASON_CODE из wlanapi.h) — понятным человеку текстом.</summary>
public static class WlanReason
{
    // Коды, которые встречались в ручной проверке подключения (docs/connect-check.md)
    public const int NetworkNotAvailable = 0x2800B;
    public const int AssociationFailure = 0x38002;
    public const int NoVisibleAccessPoint = 0x38014;
    public const int PskMismatchSuspected = 0x48014;

    public static string Describe(int reason) => reason switch
    {
        PskMismatchSuspected => "Похоже, пароль неверный: Windows не прошла проверку подлинности.",
        AssociationFailure or NoVisibleAccessPoint => "Сеть не отвечает: точка доступа вне зоны или выключена.",
        NetworkNotAvailable => "Сеть сейчас недоступна.",
        _ => "Windows сообщила код причины 0x" + reason.ToString("X", CultureInfo.InvariantCulture) + ".",
    };
}
