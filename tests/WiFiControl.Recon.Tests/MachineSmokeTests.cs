using System.ComponentModel;
using WiFiControl.Recon.Diagnostics;
using WiFiControl.Recon.Model;
using WiFiControl.Recon.Windows;

namespace WiFiControl.Recon.Tests;

/// <summary>
/// Читают компьютер, на котором запущены тесты. Ничего не меняют и прав администратора не требуют.
/// </summary>
[Trait("Category", "Machine")]
public class MachineSmokeTests
{
    [Fact]
    public void Reads_windows_of_this_machine()
    {
        WindowsVersion windows = new WindowsSystem().ReadWindows();

        Assert.StartsWith("Windows", windows.ProductName);
        Assert.Equal(Environment.OSVersion.Version.Build, windows.Build);
    }

    [Fact]
    public void Reads_the_process_owner()
    {
        ProcessIdentity process = new WindowsSystem().ReadProcess();

        Assert.Equal(Environment.UserName, process.User.Split('\\')[^1], ignoreCase: true);
    }

    [Fact]
    public void Test_run_is_not_taken_for_a_launch_from_the_scheduler()
    {
        LaunchOrigin launch = new WindowsSystem().ReadLaunch();

        Assert.NotNull(launch.Parent);
        Assert.NotNull(launch.SchedulerProcessId);
        Assert.False(launch.FromScheduler);
    }

    // Без Wi-Fi-адаптера не запущена служба WLAN, без доступа к местоположению Windows отказывает в данных —
    // такие отказы и должна записать разведка, поэтому они здесь допустимы
    private static bool IsWlanRefusal(Exception ex) => ex is Win32Exception or UnauthorizedAccessException;

    [Fact]
    public void Reads_wifi_adapters_with_their_network_connections()
    {
        var system = new WindowsSystem();
        IReadOnlyList<WifiAdapter> adapters;
        try
        {
            adapters = system.ReadWifiAdapters();
        }
        catch (Exception ex) when (IsWlanRefusal(ex))
        {
            return;
        }

        IReadOnlyList<NetworkConnection> connections = system.ReadNetworkConnections();
        foreach (WifiAdapter adapter in adapters)
        {
            Assert.NotEqual(Guid.Empty, adapter.Id);
            Assert.NotEmpty(adapter.Description);
            Assert.Contains(connections, connection => connection.Id == adapter.Id);
        }
    }

    [Fact]
    public void Reads_current_connection_of_each_wifi_adapter()
    {
        var system = new WindowsSystem();
        try
        {
            foreach (WifiAdapter adapter in system.ReadWifiAdapters())
            {
                WifiConnection? connection = system.ReadCurrentConnection(adapter.Id);
                if (connection == null) continue;

                Assert.Matches("^([0-9A-F]{2}-){5}[0-9A-F]{2}$", connection.Bssid);
                Assert.InRange(connection.SignalQuality, 0, 100);

                ConnectedBss? bss = system.ReadConnectedBss(adapter.Id, connection.Bssid);
                if (bss != null) Assert.InRange(bss.FrequencyKhz, 2_400_000, 7_200_000);
            }
        }
        catch (Exception ex) when (IsWlanRefusal(ex))
        {
        }
    }

    [Fact]
    public void Builds_a_full_report_for_this_machine()
    {
        var log = new ReconLog(new WindowsSystem(), new SystemClock(), "тест");
        log.Read("при запуске");

        Report report = log.BuildReport();

        // Отчёт остаётся в каталоге сборки тестов, чтобы его можно было просмотреть глазами
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "machine-report.txt"), report.Text);

        foreach (string section in new[] { "[Система]", "[Windows]", "[Процесс]", "[Запуск]", "[Wi-Fi-адаптеры]", "[Текущее подключение]" })
        {
            Assert.Contains(report.Lines, line => line.Trim() == section);
        }

        // Отказы WLAN API допустимы (см. IsWlanRefusal), а раздел «Система» должен читаться всегда
        List<string> lines = report.Lines.ToList();
        int systemSection = lines.IndexOf("  [Система]");
        int wifi = lines.IndexOf("  [Wi-Fi-адаптеры]");
        Assert.DoesNotContain(lines.Skip(systemSection).Take(wifi - systemSection), line => line.Contains("ОШИБКА"));
        Assert.DoesNotContain(lines.Skip(wifi), line => line.Contains("ОШИБКА") && !line.Contains("Win32Exception") && !line.Contains("UnauthorizedAccessException"));
    }
}
