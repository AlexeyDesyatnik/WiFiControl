using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using WiFiControl.Core.Model;

namespace WiFiControl.Core.Windows;

/// <summary>
/// Чтение и запись системного прокси и WinHTTP-прокси (ADR 0003). Системный прокси — в <c>HKCU</c>:
/// инструмент работает в сеансе того же пользователя, поэтому в чужой <c>HKU</c> не пишет.
/// Перенесено из NetSwitch.
/// </summary>
internal static class ProxyRegistry
{
    private const string InternetSettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private const string ConnectionsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Internet Settings\Connections";
    private const string WinHttpValue = "WinHttpSettings";

    private const int WinHttpAccessNoProxy = 1;
    private const int WinHttpAccessNamedProxy = 3;
    private const int InternetOptionRefresh = 37;
    private const int InternetOptionSettingsChanged = 39;

    public static SystemProxyState ReadSystemProxy()
    {
        using RegistryKey key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey)
            ?? throw new InvalidOperationException("Не найдены настройки прокси в реестре: HKEY_CURRENT_USER\\" + InternetSettingsKey);

        var state = new SystemProxyState
        {
            Settings = ProxySettings.FromRaw(IsSet(key.GetValue("ProxyEnable")), key.GetValue("ProxyServer") as string, key.GetValue("ProxyOverride") as string),
            AutoConfigUrl = key.GetValue("AutoConfigURL") as string ?? ""
        };

        using RegistryKey? connections = key.OpenSubKey("Connections");
        if (connections?.GetValue("DefaultConnectionSettings") is byte[] blob) state.AutoDetect = InternetSettingsBlob.Parse(blob).AutoDetect;
        return state;
    }

    public static ProxySettings ReadWinHttp()
    {
        var info = new WinHttpProxyInfo();
        if (!WinHttpGetDefaultProxyConfiguration(ref info)) throw new Win32Exception();
        try
        {
            return ProxySettings.FromRaw(info.AccessType == WinHttpAccessNamedProxy, Marshal.PtrToStringUni(info.Proxy), Marshal.PtrToStringUni(info.ProxyBypass));
        }
        finally
        {
            if (info.Proxy != IntPtr.Zero) GlobalFree(info.Proxy);
            if (info.ProxyBypass != IntPtr.Zero) GlobalFree(info.ProxyBypass);
        }
    }

    public static void WriteSystemProxy(SystemProxyState state)
    {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(InternetSettingsKey))
        {
            key.SetValue("ProxyEnable", state.Settings.Enabled ? 1 : 0, RegistryValueKind.DWord);
            SetOrDelete(key, "ProxyServer", state.Settings.Server);
            SetOrDelete(key, "ProxyOverride", state.Settings.ToBypassList());
            SetOrDelete(key, "AutoConfigURL", state.AutoConfigUrl);

            using RegistryKey connections = key.CreateSubKey("Connections");
            // Окно «Свойства браузера» читает первое значение, WinINet после перезагрузки — второе
            foreach (string name in (string[])["DefaultConnectionSettings", "SavedLegacySettings"])
            {
                connections.SetValue(name, InternetSettingsBlob.Build(Previous(connections, name), state), RegistryValueKind.Binary);
            }
        }

        // Без уведомления запущенные программы увидели бы перемену только после перезапуска
        InternetSetOption(IntPtr.Zero, InternetOptionSettingsChanged, IntPtr.Zero, 0);
        InternetSetOption(IntPtr.Zero, InternetOptionRefresh, IntPtr.Zero, 0);
    }

    public static void WriteWinHttp(ProxySettings settings)
    {
        if (settings.Enabled && settings.Server.Length == 0)
            throw new InvalidOperationException("Не задан адрес прокси-сервера.");

        string bypass = settings.ToBypassList();
        var info = new WinHttpProxyInfo
        {
            AccessType = settings.Enabled ? WinHttpAccessNamedProxy : WinHttpAccessNoProxy,
            Proxy = settings.Enabled ? Marshal.StringToHGlobalUni(settings.Server) : IntPtr.Zero,
            ProxyBypass = settings.Enabled && bypass.Length > 0 ? Marshal.StringToHGlobalUni(bypass) : IntPtr.Zero
        };
        try
        {
            if (!WinHttpSetDefaultProxyConfiguration(ref info)) throw new Win32Exception();
        }
        finally
        {
            Marshal.FreeHGlobal(info.Proxy);
            Marshal.FreeHGlobal(info.ProxyBypass);
        }

        MirrorWinHttpTo32Bit();
    }

    // На 64-разрядной Windows у 32-разрядных программ своя копия WinHTTP-прокси
    private static void MirrorWinHttpTo32Bit()
    {
        if (!Environment.Is64BitOperatingSystem || !Environment.Is64BitProcess) return;

        byte[]? written;
        using (RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
        using (RegistryKey? key = machine.OpenSubKey(ConnectionsKey))
        {
            written = key?.GetValue(WinHttpValue) as byte[];
        }
        if (written == null) return;

        using (RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
        using (RegistryKey key = machine.CreateSubKey(ConnectionsKey))
        {
            key.SetValue(WinHttpValue, written, RegistryValueKind.Binary);
        }
    }

    private static InternetSettingsBlob? Previous(RegistryKey connections, string name)
    {
        if (connections.GetValue(name) is not byte[] data) return null;

        try
        {
            return InternetSettingsBlob.Parse(data);
        }
        catch (FormatException)
        {
            // Испорченное значение не чиним, а заменяем новым
            return null;
        }
    }

    private static void SetOrDelete(RegistryKey key, string name, string value)
    {
        if (value.Length > 0)
            key.SetValue(name, value, RegistryValueKind.String);
        else
            key.DeleteValue(name, false);
    }

    private static bool IsSet(object? value) => value is int number && number != 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct WinHttpProxyInfo
    {
        public int AccessType;
        public IntPtr Proxy;
        public IntPtr ProxyBypass;
    }

    [DllImport("winhttp.dll", SetLastError = true)]
    private static extern bool WinHttpGetDefaultProxyConfiguration(ref WinHttpProxyInfo info);

    [DllImport("winhttp.dll", SetLastError = true)]
    private static extern bool WinHttpSetDefaultProxyConfiguration(ref WinHttpProxyInfo info);

    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr internet, int option, IntPtr buffer, int length);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr memory);
}
