using Microsoft.Win32;
using WiFiControl.Recon.Model;

namespace WiFiControl.Recon.Windows;

/// <summary>Читает записи о доступе к местоположению из реестра. Ничего не меняет.</summary>
internal static class LocationReader
{
    private const string DesktopAppsKey = "NonPackaged";
    private const string PromptsKey = "Executables";

    public static LocationConsent ReadUser(string userSid)
    {
        using RegistryKey users = OpenBase(RegistryHive.Users);
        return Read(users, userSid + "\\" + LocationConsent.RegistryKey);
    }

    public static LocationConsent ReadMachine()
    {
        using RegistryKey machine = OpenBase(RegistryHive.LocalMachine);
        return Read(machine, LocationConsent.RegistryKey);
    }

    // Явно 64-разрядное представление: 32-разрядный процесс иначе прочитал бы копию для WOW64
    private static RegistryKey OpenBase(RegistryHive hive) =>
        RegistryKey.OpenBaseKey(hive, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default);

    private static LocationConsent Read(RegistryKey root, string path)
    {
        using RegistryKey? location = root.OpenSubKey(path);
        if (location == null) return new LocationConsent(null, null, [], []);

        using RegistryKey? desktopApps = location.OpenSubKey(DesktopAppsKey);
        return new LocationConsent(ReadSwitch(location), desktopApps == null ? null : ReadSwitch(desktopApps), ReadUsages(desktopApps), ReadPrompts(desktopApps));
    }

    private static ConsentSwitch ReadSwitch(RegistryKey key) =>
        new(key.GetValue("Value") as string, ReadTime(key, "LastSetTime"));

    private static List<DesktopAppUsage> ReadUsages(RegistryKey? desktopApps)
    {
        var usages = new List<DesktopAppUsage>();
        if (desktopApps == null) return usages;

        foreach (string name in desktopApps.GetSubKeyNames())
        {
            // Рядом с записями приложений лежит подраздел запросов Windows, он читается отдельно
            if (string.Equals(name, PromptsKey, StringComparison.OrdinalIgnoreCase)) continue;

            using RegistryKey? app = desktopApps.OpenSubKey(name);
            if (app == null) continue;
            // В имени раздела реестра не может быть «\», поэтому Windows пишет путь к exe через «#»;
            // настоящий «#» в пути от него не отличить
            usages.Add(new DesktopAppUsage(name.Replace('#', '\\'), ReadTime(app, "LastUsedTimeStart"), ReadTime(app, "LastUsedTimeStop")));
        }
        return usages;
    }

    private static List<LocationPrompt> ReadPrompts(RegistryKey? desktopApps)
    {
        var prompts = new List<LocationPrompt>();
        using RegistryKey? executables = desktopApps?.OpenSubKey(PromptsKey);
        if (executables == null) return prompts;

        foreach (string name in executables.GetSubKeyNames())
        {
            using RegistryKey? exe = executables.OpenSubKey(name);
            if (exe == null) continue;
            prompts.Add(new LocationPrompt(name, exe.GetValue("GlobalPromptShown") is int shown ? shown : null));
        }
        return prompts;
    }

    /// <returns>null, если значения нет или оно нулевое.</returns>
    private static DateTime? ReadTime(RegistryKey key, string name) =>
        // Время хранится как FILETIME в значении QWORD
        key.GetValue(name) is long fileTime && fileTime > 0 ? DateTime.FromFileTime(fileTime) : null;
}
