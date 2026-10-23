using System.Text;

namespace WiFiControl.Recon.Windows;

/// <summary>
/// Двоичное значение, в котором Windows хранит параметры прокси подключения
/// (DefaultConnectionSettings у пользователя, WinHttpSettings у компьютера).
/// «Автоматическое определение параметров» хранится только здесь. Перенесено из NetSwitch — только разбор.
/// </summary>
internal sealed record InternetSettingsBlob(int Version, int Counter, int Flags, string Proxy, string Bypass, string AutoConfigUrl)
{
    private const int FlagProxy = 0x2;
    private const int FlagAutoConfig = 0x4;
    private const int FlagAutoDetect = 0x8;

    public bool ProxyEnabled => (Flags & FlagProxy) != 0;

    public bool AutoConfigEnabled => (Flags & FlagAutoConfig) != 0;

    public bool AutoDetect => (Flags & FlagAutoDetect) != 0;

    /// <summary>WinHttpSettings короче: в нём нет сценария автонастройки, поэтому хвост необязателен.</summary>
    public static InternetSettingsBlob Parse(byte[] data)
    {
        if (data.Length < 12) throw new FormatException("Параметры подключения короче заголовка: " + data.Length + " байт.");

        int offset = 12;
        string proxy = ReadString(data, ref offset);
        string bypass = ReadString(data, ref offset);
        string script = ReadString(data, ref offset);
        // Служебные данные Windows после строк чтению не нужны
        return new InternetSettingsBlob(BitConverter.ToInt32(data, 0), BitConverter.ToInt32(data, 4), BitConverter.ToInt32(data, 8), proxy, bypass, script);
    }

    private static string ReadString(byte[] data, ref int offset)
    {
        if (offset + 4 > data.Length) return "";

        int length = BitConverter.ToInt32(data, offset);
        offset += 4;
        if (length < 0 || offset + length > data.Length)
            throw new FormatException("Строка в параметрах подключения выходит за границы значения.");

        string text = Encoding.ASCII.GetString(data, offset, length);
        offset += length;
        return text;
    }
}
