namespace WiFiControl.Recon.Model;

/// <summary>MAC-адреса адаптера из сетевого стека (<c>GetIfEntry2</c>, <c>MIB_IF_ROW2</c>).</summary>
/// <param name="Current">Адрес, с которым адаптер работает сейчас, вида «3C-21-9C-4E-8F-40».</param>
/// <param name="Permanent">Адрес, зашитый в устройство.</param>
internal sealed record MacAddresses(string Current, string Permanent)
{
    /// <summary>
    /// Во втором бите первого байта — признак локально администрируемого адреса: у случайного адреса
    /// он выставлен, у зашитого производителем — нет.
    /// </summary>
    public static bool IsLocallyAdministered(string mac) =>
        mac.Length >= 2 && (Convert.ToByte(mac[..2], 16) & 0x02) != 0;
}

/// <summary>Записи реестра, относящиеся к MAC-адресу Wi-Fi-адаптера.</summary>
/// <param name="WlanSvcKey">Раздел службы WLAN для адаптера; null, если раздела нет.</param>
/// <param name="RandomMacState">Значение <c>RandomMacState</c> как есть; null, если значения нет.</param>
/// <param name="DeviceKey">Раздел драйвера адаптера в классе сетевых устройств; null, если не найден.</param>
/// <param name="NetworkAddress">Адрес, заданный вручную в свойствах драйвера (<c>NetworkAddress</c>); null, если значения нет.</param>
internal sealed record MacRegistry(string? WlanSvcKey, object? RandomMacState, string? DeviceKey, string? NetworkAddress)
{
    public const string WlanSvcInterfaces = @"SOFTWARE\Microsoft\WlanSvc\Interfaces";
    public const string NetworkAdapterClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";
}
