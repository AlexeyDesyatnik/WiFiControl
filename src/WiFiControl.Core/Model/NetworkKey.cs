using System.Text;

namespace WiFiControl.Core.Model;

/// <summary>
/// Ключ, по которому инструмент узнаёт сеть (docs/adr/0007): если байты имени — корректный UTF-8, то сам текст,
/// иначе <c>hex:</c> и шестнадцатеричные байты. Имя, введённое руками, — уже ключ: это текст.
/// Ключи сравниваются точно, с учётом регистра.
/// </summary>
public static class NetworkKey
{
    public const string HexPrefix = "hex:";

    public static readonly StringComparer Comparer = StringComparer.Ordinal;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static string FromBytes(ReadOnlySpan<byte> ssid)
    {
        try
        {
            return StrictUtf8.GetString(ssid);
        }
        catch (DecoderFallbackException)
        {
            return HexPrefix + Convert.ToHexString(ssid);
        }
    }
}
