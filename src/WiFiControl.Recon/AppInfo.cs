using System.Reflection;

namespace WiFiControl.Recon;

internal static class AppInfo
{
    // Полная версия с суффиксом: локальную сборку «0.0.0-dev» видно сразу
    public static string Version =>
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
}
