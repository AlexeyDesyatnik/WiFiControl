using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;
using WiFiControl.Recon.Model;

namespace WiFiControl.Recon.Windows;

/// <summary>Чтение системного прокси, WinHTTP-прокси и политик прокси. Перенесено из NetSwitch.</summary>
internal static class ProxyReader
{
    public const string InternetSettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    public const string InternetSettingsPolicyKey = @"Software\Policies\Microsoft\Windows\CurrentVersion\Internet Settings";
    public const string ControlPanelPolicyKey = @"Software\Policies\Microsoft\Internet Explorer\Control Panel";

    /// <summary>
    /// Когда права администратора получены вводом чужих учётных данных, процесс принадлежит администратору,
    /// но остаётся в сеансе того, кто вошёл в систему. Системный прокси — этого пользователя.
    /// </summary>
    public static InteractiveUser ReadInteractiveUser()
    {
        int session;
        using (Process current = Process.GetCurrentProcess()) session = current.SessionId;
        string user = NativeMethods.SessionUserName(session);
        string domain = NativeMethods.SessionDomainName(session);
        if (user.Length == 0) throw new InvalidOperationException("В сеансе " + session + " нет вошедшего пользователя.");

        string name = domain.Length > 0 ? domain + "\\" + user : user;
        var sid = (SecurityIdentifier)new NTAccount(name).Translate(typeof(SecurityIdentifier));

        using WindowsIdentity owner = WindowsIdentity.GetCurrent();
        return new InteractiveUser(name, sid.Value, sid == owner.User);
    }

    /// <summary>Системный прокси пользователя с данным SID; пользователь должен быть в системе.</summary>
    public static SystemProxyState ReadSystemProxy(string userSid)
    {
        string path = userSid + "\\" + InternetSettingsKey;
        using RegistryKey key = Registry.Users.OpenSubKey(path)
            ?? throw new InvalidOperationException("Не найдены настройки пользователя в реестре: HKEY_USERS\\" + path);

        var settings = ProxySettings.FromRaw(IsSet(key.GetValue("ProxyEnable")), key.GetValue("ProxyServer") as string, key.GetValue("ProxyOverride") as string);

        using RegistryKey? connections = key.OpenSubKey("Connections");
        bool autoDetect = connections?.GetValue("DefaultConnectionSettings") is byte[] blob && InternetSettingsBlob.Parse(blob).AutoDetect;

        return new SystemProxyState(settings, autoDetect, key.GetValue("AutoConfigURL") as string ?? "");
    }

    public static ProxySettings ReadWinHttp()
    {
        (bool named, string? proxy, string? bypass) = NativeMethods.WinHttpDefaultProxy();
        return ProxySettings.FromRaw(named, proxy, bypass);
    }

    public static ProxyPolicy ReadPolicy(string userSid)
    {
        using RegistryKey machine = OpenLocalMachine();

        bool perMachine;
        using (RegistryKey? key = machine.OpenSubKey(InternetSettingsPolicyKey))
        {
            // 0 — прокси один на компьютер; отсутствие значения или 1 — у каждого пользователя свой
            perMachine = key?.GetValue("ProxySettingsPerUser") is 0;
        }

        bool setsValues;
        using (RegistryKey? key = Registry.Users.OpenSubKey(userSid + "\\" + InternetSettingsPolicyKey))
        {
            setsValues = key != null
                && (key.GetValue("ProxyEnable") != null || key.GetValue("ProxyServer") != null || key.GetValue("AutoConfigURL") != null);
        }

        bool changeLocked;
        using (RegistryKey? machineKey = machine.OpenSubKey(ControlPanelPolicyKey))
        using (RegistryKey? userKey = Registry.Users.OpenSubKey(userSid + "\\" + ControlPanelPolicyKey))
        {
            changeLocked = IsSet(machineKey?.GetValue("Proxy")) || IsSet(userKey?.GetValue("Proxy"));
        }

        return new ProxyPolicy(perMachine, setsValues, changeLocked);
    }

    /// <summary>Раздел HKLM без перенаправления в 32-разрядное представление.</summary>
    private static RegistryKey OpenLocalMachine() =>
        RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default);

    private static bool IsSet(object? value) => value is int number && number != 0;
}
