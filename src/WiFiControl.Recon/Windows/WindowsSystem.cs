using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;
using WiFiControl.Recon.Model;

namespace WiFiControl.Recon.Windows;

/// <summary>Настоящий компьютер. Только читает.</summary>
internal sealed class WindowsSystem : IReadOnlySystem
{
    private const string CurrentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    public string ComputerName => Environment.MachineName;

    public WindowsVersion ReadWindows()
    {
        // Явно 64-разрядное представление: 32-разрядный процесс иначе прочитал бы копию для WOW64
        using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32);
        using RegistryKey key = machine.OpenSubKey(CurrentVersionKey)
            ?? throw new InvalidOperationException("Нет раздела реестра HKLM\\" + CurrentVersionKey + ".");

        return new WindowsVersion(
            key.GetValue("ProductName") as string ?? "",
            key.GetValue("EditionID") as string,
            key.GetValue("DisplayVersion") as string,
            int.Parse(key.GetValue("CurrentBuildNumber") as string ?? "0", System.Globalization.CultureInfo.InvariantCulture),
            key.GetValue("UBR") is int ubr ? ubr : 0);
    }

    public ProcessIdentity ReadProcess()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        // При включённом UAC роль администратора есть только у повышенного маркера
        return new ProcessIdentity(identity.Name, new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator));
    }

    public LaunchOrigin ReadLaunch()
    {
        return new LaunchOrigin(ReadParent(), ServiceProcessId("Schedule"));
    }

    private static ParentProcess ReadParent()
    {
        using Process current = Process.GetCurrentProcess();
        int parentId = NativeMethods.ParentProcessId(current.Handle);

        try
        {
            using Process parent = Process.GetProcessById(parentId);
            // Номер завершившегося родителя мог достаться процессу, запущенному позже нас
            if (StartedAfter(parent, current)) return new ParentProcess(parentId, null);
            return new ParentProcess(parentId, parent.ProcessName);
        }
        catch (ArgumentException)
        {
            return new ParentProcess(parentId, null);
        }
    }

    private static bool StartedAfter(Process parent, Process current)
    {
        try
        {
            return parent.StartTime > current.StartTime;
        }
        catch (Win32Exception)
        {
            // Время запуска процесса службы без прав администратора не прочитать; тогда верим номеру
            return false;
        }
    }

    /// <returns>null, если служба не запущена.</returns>
    private static int? ServiceProcessId(string service)
    {
        int id = NativeMethods.ServiceProcessId(service);
        return id == 0 ? null : id;
    }
}
