namespace WiFiControl.Core.Model;

/// <summary>
/// Пароль Wi-Fi-сети с личной защитой: парольная фраза от 8 до 63 печатных символов ASCII или ключ из ровно
/// 64 шестнадцатеричных цифр. Инструмент пароль у себя не хранит: он уходит только в профиль сети Windows.
/// </summary>
public static class WifiPassword
{
    public const int MinLength = 8;
    public const int MaxPassphraseLength = 63;
    public const int KeyLength = 64;

    /// <param name="keyAccepted">Ключ подходит; у WPA3 (SAE) ключа нет, только парольная фраза.</param>
    public static bool IsValid(string password, bool keyAccepted = true)
    {
        return password.Length is >= MinLength and <= MaxPassphraseLength && password.All(IsPrintableAscii)
            || keyAccepted && IsKey(password);
    }

    /// <summary>Ключ, а не парольная фраза: ровно 64 шестнадцатеричные цифры.</summary>
    public static bool IsKey(string password) => password.Length == KeyLength && password.All(char.IsAsciiHexDigit);

    // IEEE 802.11i: символы парольной фразы — с кодами от 32 до 126
    private static bool IsPrintableAscii(char c) => c is >= ' ' and <= '~';
}
