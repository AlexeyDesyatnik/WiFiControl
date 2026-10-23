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
    public void Reads_adapters_with_their_ipv4_settings()
    {
        IReadOnlyList<Adapter> adapters = new WindowsSystem().ReadAdapters();

        Assert.NotEmpty(adapters);
        Assert.All(adapters, adapter =>
        {
            Assert.NotEqual("", adapter.Name);
            Assert.NotEqual(Guid.Empty, adapter.Id);
            // Действующие настройки читаются только при соединении
            if (adapter.Active != null) Assert.Equal(AdapterState.Connected, adapter.State);
        });
    }

    [Fact]
    public void Wifi_adapters_are_found_among_network_adapters()
    {
        var system = new WindowsSystem();
        IReadOnlyList<WifiAdapter> wifiAdapters;
        try
        {
            wifiAdapters = system.ReadWifiAdapters();
        }
        catch (Exception ex) when (IsWlanRefusal(ex))
        {
            return;
        }

        IReadOnlyList<Adapter> adapters = system.ReadAdapters();
        foreach (WifiAdapter wifi in wifiAdapters)
        {
            Adapter adapter = Assert.Single(adapters, a => a.Id == wifi.Id);
            if (adapter.State != AdapterState.Disabled) Assert.True(adapter.IsWireless, adapter.Name);
        }
    }

    [Fact]
    public void Reads_proxy_of_this_machine()
    {
        var system = new WindowsSystem();

        InteractiveUser user = system.ReadInteractiveUser();
        system.ReadSystemProxy(user.Sid);
        system.ReadProxyPolicy(user.Sid);
        system.ReadWinHttpProxy();

        Assert.StartsWith("S-1-5-", user.Sid);
        Assert.True(user.IsProcessOwner);
    }

    [Fact]
    public void Builds_a_full_report_for_this_machine()
    {
        var log = new ReconLog(new WindowsSystem(), new SystemClock(), "тест");
        log.Read("при запуске");

        Report report = log.BuildReport();

        // Отчёт остаётся в каталоге сборки тестов, чтобы его можно было просмотреть глазами
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "machine-report.txt"), report.Text);

        string[] sections = ["[Система]", "[Wi-Fi-адаптеры]", "[Текущее подключение]", "[Настройки IPv4]", "[Системный прокси]", "[WinHTTP-прокси]"];
        foreach (string section in sections.Concat(["[Windows]", "[Процесс]", "[Запуск]"]))
        {
            Assert.Contains(report.Lines, line => line.Trim() == section);
        }

        // Отказы WLAN API допустимы (см. IsWlanRefusal), остальные разделы должны читаться всегда
        List<string> lines = report.Lines.ToList();
        Assert.DoesNotContain(Section(lines, "[Система]"), line => line.Contains("ОШИБКА"));
        Assert.DoesNotContain(lines.SkipWhile(line => line.Trim() != "[Wi-Fi-адаптеры]").TakeWhile(line => line.Trim() != "[Настройки IPv4]"),
            line => line.Contains("ОШИБКА") && !line.Contains("Win32Exception") && !line.Contains("UnauthorizedAccessException"));
        foreach (string section in new[] { "[Настройки IPv4]", "[Системный прокси]", "[WinHTTP-прокси]" })
        {
            Assert.DoesNotContain(Section(lines, section), line => line.Contains("ОШИБКА"));
        }
    }

    /// <summary>Строки раздела второго уровня — до следующего раздела того же уровня.</summary>
    private static IEnumerable<string> Section(List<string> lines, string title)
    {
        int start = lines.IndexOf("  " + title);
        return lines.Skip(start + 1).TakeWhile(line => line.StartsWith("    ", StringComparison.Ordinal));
    }
}
