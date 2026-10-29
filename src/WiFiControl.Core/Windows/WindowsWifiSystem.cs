using System.ComponentModel;
using System.Globalization;
using ManagedNativeWifi;
using WiFiControl.Core.Model;

namespace WiFiControl.Core.Windows;

/// <summary>
/// Настоящий Wi-Fi: перечисление, сканирование и профили сетей Windows — через ManagedNativeWifi,
/// уведомления — собственным P/Invoke (docs/adr/0001).
/// </summary>
public sealed class WindowsWifiSystem : IWifiSystem
{
    private const int ErrorInvalidState = 5023;
    private static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DisconnectTimeout = TimeSpan.FromSeconds(5);

    static WindowsWifiSystem()
    {
        // Без этого ManagedNativeWifi сводит часть отказов WLAN API к ActionResult и теряет код ошибки Windows.
        // Настройка общая на весь процесс
        NativeWifi.ThrowsOnAnyFailure = true;
    }

    public Task<IReadOnlyList<WifiAdapter>> ReadAdaptersAsync(CancellationToken cancellationToken)
    {
        return RunAsync("список Wi-Fi-адаптеров", () => (IReadOnlyList<WifiAdapter>)NativeWifi.EnumerateInterfaces()
            .Select(adapter => new WifiAdapter(adapter.Id, adapter.Description))
            .ToList(), cancellationToken);
    }

    public Task<IReadOnlyList<VisibleNetwork>> ReadVisibleNetworksAsync(Guid adapter, CancellationToken cancellationToken)
    {
        return RunAsync("список сетей", () =>
        {
            (ActionResult result, IEnumerable<AvailableNetworkInfo> list) = NativeWifi.EnumerateAvailableNetworks(adapter);
            if (result != ActionResult.Success)
                throw new InvalidOperationException("ManagedNativeWifi не вернул список сетей (" + result + ").");

            return (IReadOnlyList<VisibleNetwork>)list
                .Where(network => network.BssType == BssType.Infrastructure)
                .Select(network => new VisibleNetwork(network.Ssid.ToBytes(), network.SignalQuality,
                    WifiSecurityMap.From(network.AuthenticationAlgorithm, network.IsSecurityEnabled), network.ProfileName ?? ""))
                .ToList();
        }, cancellationToken);
    }

    public Task<byte[]?> ReadCurrentNetworkAsync(Guid adapter, CancellationToken cancellationToken)
    {
        return RunAsync("имя текущей сети", () =>
        {
            (ActionResult result, CurrentConnectionInfo connection) value;
            try
            {
                value = NativeWifi.GetCurrentConnection(adapter);
            }
            catch (Win32Exception e) when (e.NativeErrorCode == ErrorInvalidState)
            {
                // Так WlanQueryInterface отвечает за адаптер без подключения
                return null;
            }
            // Обёртка не разобрала подключение (незнакомый шифр и т. п.) — имя сети ей при этом известно не наверняка
            if (value.result != ActionResult.Success)
                throw new InvalidOperationException("ManagedNativeWifi не разобрал текущее подключение (" + value.result + ").");
            return value.connection.Ssid.ToBytes();
        }, cancellationToken);
    }

    public Task<IReadOnlyList<WindowsProfile>> ReadWindowsProfilesAsync(Guid adapter, CancellationToken cancellationToken)
    {
        return RunAsync("профили сетей Windows", () => (IReadOnlyList<WindowsProfile>)NativeWifi.EnumerateProfiles()
            .Where(profile => profile.InterfaceInfo.Id == adapter)
            .Select(profile => new WindowsProfile(profile.Name, NetworkKey.FromBytes(profile.Document.Ssid.ToBytes())))
            .ToList(), cancellationToken);
    }

    public Task<string> ReadWindowsProfileXmlAsync(Guid adapter, string name, CancellationToken cancellationToken)
    {
        return RunAsync("профиль сети Windows «" + name + "»", () => NativeWifi.EnumerateProfiles()
            .FirstOrDefault(profile => profile.InterfaceInfo.Id == adapter && profile.Name == name)?.Document.Xml
            ?? throw new InvalidOperationException("Профиля сети Windows «" + name + "» больше нет."), cancellationToken);
    }

    public Task SaveWindowsProfileAsync(Guid adapter, string xml, bool overwrite, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            bool saved;
            try
            {
                // Профиль из системного меню обычно принадлежит пользователю: перезапись для всех пользователей
                // оставила бы рядом второй профиль с прежним паролем
                string name = new ProfileDocument(xml).Name;
                ProfileType type = (overwrite ? NativeWifi.EnumerateProfiles()
                    .FirstOrDefault(profile => profile.InterfaceInfo.Id == adapter && profile.Name == name)?.ProfileType : null) ?? ProfileType.AllUser;
                saved = NativeWifi.SetProfile(adapter, type, xml, null, overwrite);
            }
            // Сюда же приходит профиль с тем же именем, когда перезаписывать нельзя (ERROR_ALREADY_EXISTS)
            catch (Win32Exception e)
            {
                throw new InvalidOperationException("Windows не сохранила профиль сети Windows: " + e.Message, e);
            }
            if (!saved) throw new InvalidOperationException("Windows не сохранила профиль сети Windows.");
        }, cancellationToken);
    }

    public Task DeleteWindowsProfileAsync(Guid adapter, string name, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            bool deleted;
            try
            {
                deleted = NativeWifi.DeleteProfile(adapter, name);
            }
            catch (Win32Exception e)
            {
                throw new InvalidOperationException("Windows не удалила профиль сети Windows «" + name + "»: " + e.Message, e);
            }
            if (!deleted) throw new InvalidOperationException("Windows не удалила профиль сети Windows «" + name + "».");
        }, cancellationToken);
    }

    public async Task ScanAsync(Guid adapter, CancellationToken cancellationToken)
    {
        try
        {
            // Библиотека ждёт уведомления о конце сканирования, удачного или нет
            await NativeWifi.ScanNetworksAsync(ScanMode.OnlySpecified, [adapter], ScanTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Win32Exception e)
        {
            throw new InvalidOperationException("Windows не запустила поиск сетей: " + e.Message, e);
        }
    }

    public async Task DisconnectAsync(Guid adapter, CancellationToken cancellationToken)
    {
        bool disconnected;
        try
        {
            // Библиотека ждёт уведомления об отключении: настройки IPv4 пишутся уже на отключённый адаптер (ADR 0004)
            disconnected = await NativeWifi.DisconnectNetworkAsync(adapter, DisconnectTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Win32Exception e)
        {
            throw new InvalidOperationException("Windows не отключила адаптер от сети: " + e.Message, e);
        }
        if (!disconnected)
            throw new InvalidOperationException("Windows не сообщила об отключении от сети за " + DisconnectTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture) + " с.");
    }

    public Task ConnectAsync(Guid adapter, string windowsProfile, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            try
            {
                NativeWifi.ConnectNetwork(adapter, windowsProfile, BssType.Infrastructure, null);
            }
            catch (Win32Exception e)
            {
                throw new InvalidOperationException("Windows не начала подключение через профиль сети Windows «" + windowsProfile + "»: " + e.Message, e);
            }
        }, cancellationToken);
    }

    public IDisposable Watch(Action<WifiNotification> onNotification)
    {
        try
        {
            return new WlanNotifications(onNotification);
        }
        catch (Win32Exception e)
        {
            throw new InvalidOperationException("Windows не дала подписаться на уведомления Wi-Fi: " + e.Message, e);
        }
    }

    // WLAN API отвечает синхронно, поэтому вызов уходит с вызывающего потока
    private static Task<T> RunAsync<T>(string what, Func<T> read, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            try
            {
                return read();
            }
            catch (Win32Exception e)
            {
                throw new InvalidOperationException("Windows не отдала " + what + ": " + e.Message, e);
            }
        }, cancellationToken);
    }
}

internal static class WifiSecurityMap
{
    /// <summary>Защита по алгоритму проверки подлинности; WEP — это «открытая» или «общий ключ» с шифрованием.</summary>
    public static WifiSecurity From(AuthenticationAlgorithm authentication, bool securityEnabled)
    {
        return authentication switch
        {
            AuthenticationAlgorithm.Open when !securityEnabled => WifiSecurity.Open,
            AuthenticationAlgorithm.Open or AuthenticationAlgorithm.Shared => WifiSecurity.Wep,
            AuthenticationAlgorithm.WPA_PSK => WifiSecurity.WpaPersonal,
            AuthenticationAlgorithm.RSNA_PSK => WifiSecurity.Wpa2Personal,
            AuthenticationAlgorithm.WPA3_SAE => WifiSecurity.Wpa3Personal,
            AuthenticationAlgorithm.WPA or AuthenticationAlgorithm.RSNA or AuthenticationAlgorithm.WPA3_ENT
                or AuthenticationAlgorithm.WPA3_ENT_192 => WifiSecurity.Enterprise,
            _ => WifiSecurity.Other,
        };
    }
}
