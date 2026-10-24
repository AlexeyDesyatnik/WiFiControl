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

                BssEntry? bss = system.ReadBssList(adapter.Id).FirstOrDefault(entry => entry.Bssid == connection.Bssid);
                if (bss != null) Assert.InRange(bss.FrequencyKhz, 2_400_000, 7_200_000);
            }
        }
        catch (Exception ex) when (IsWlanRefusal(ex))
        {
        }
    }

    [Fact]
    public void Scans_and_reads_visible_networks_of_each_wifi_adapter()
    {
        var system = new WindowsSystem();
        try
        {
            foreach (WifiAdapter adapter in system.ReadWifiAdapters())
            {
                // Время вышло — тоже ответ разведки; список читается и без уведомления
                system.Scan(adapter.Id, TimeSpan.FromSeconds(10));
                IReadOnlyList<AvailableNetwork> networks = system.ReadAvailableNetworks(adapter.Id);
                IReadOnlyList<BssEntry> bssList = system.ReadBssList(adapter.Id);

                Assert.All(networks, network => Assert.InRange(network.SignalQuality, 0, 100));
                Assert.All(bssList, bss =>
                {
                    Assert.Matches("^([0-9A-F]{2}-){5}[0-9A-F]{2}$", bss.Bssid);
                    Assert.InRange(bss.FrequencyKhz, 2_400_000, 7_200_000);
                });
            }
        }
        catch (Exception ex) when (IsWlanRefusal(ex))
        {
        }
    }

    [Fact]
    public void Reads_windows_profiles_of_each_wifi_adapter()
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

        foreach (WifiAdapter adapter in adapters)
        {
            IReadOnlyList<WindowsProfile> profiles = system.ReadWindowsProfiles(adapter.Id);

            Assert.Equal(Enumerable.Range(0, profiles.Count), profiles.Select(profile => profile.Position));
            Assert.All(profiles, profile =>
            {
                Assert.NotEqual("", profile.Name);
                if (profile.ConnectionMode != null) Assert.Contains(profile.ConnectionMode, new[] { "auto", "manual" });
            });
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
    public void Reads_ip_interfaces_of_network_adapters()
    {
        var system = new WindowsSystem();
        IReadOnlyList<IpInterface> interfaces = system.ReadIpInterfaces();
        IReadOnlyList<Adapter> adapters = system.ReadAdapters();

        Assert.NotEmpty(interfaces);
        Assert.All(interfaces, ip => Assert.InRange(ip.Metric, 1, 9999));
        Assert.Equal(interfaces.Count, interfaces.Select(ip => ip.Index).Distinct().Count());
        // У включённого адаптера «Сетевых подключений» есть интерфейс IPv4, если IPv4 на нём не отключён
        Assert.Contains(adapters, adapter => interfaces.Any(ip => ip.AdapterId == adapter.Id));
    }

    [Fact]
    public void Reads_the_route_to_the_internet()
    {
        var system = new WindowsSystem();
        IReadOnlyList<Route> routes = system.ReadDefaultRoutes();
        IReadOnlyList<IpInterface> interfaces = system.ReadIpInterfaces();

        BestRoute? best = system.ReadBestRoute("8.8.8.8");

        // Без сети маршрута нет, тогда нет и маршрутов по умолчанию
        if (best == null)
        {
            Assert.Empty(routes);
            return;
        }
        Assert.Contains(interfaces, ip => ip.Index == best.Route.InterfaceIndex && ip.AdapterId == best.Route.AdapterId);
        // Маршрут по умолчанию может перекрываться более узким маршрутом VPN, тогда выбран не он
        if (best.Route.Destination == "0.0.0.0/0") Assert.Contains(best.Route, routes);
        Assert.All(routes, route => Assert.Equal("0.0.0.0/0", route.Destination));
        Assert.Matches(@"^\d+\.\d+\.\d+\.\d+$", best.SourceAddress);
        Assert.NotEqual("0.0.0.0", best.SourceAddress);
    }

    [Fact]
    public void Reads_location_access_of_the_user_and_the_computer()
    {
        var system = new WindowsSystem();
        InteractiveUser user = system.ReadInteractiveUser();

        LocationConsent[] consents = [system.ReadUserLocationConsent(user.Sid), system.ReadMachineLocationConsent()];

        foreach (LocationConsent consent in consents)
        {
            // Записи классических приложений лежат в подразделе NonPackaged
            if (consent.DesktopApps == null) Assert.Empty(consent.DesktopAppUsages);
            Assert.All(consent.DesktopAppUsages, app =>
            {
                Assert.True(Path.IsPathFullyQualified(app.Path), app.Path);
                Assert.DoesNotContain('#', app.Path);
                if (app.LastUsedStart is DateTime start) Assert.InRange(start, new DateTime(2015, 1, 1), DateTime.Now.AddDays(1));
            });
            // Запросы Windows записаны по имени exe, без пути
            Assert.All(consent.Prompts, prompt => Assert.DoesNotContain('\\', prompt.Executable));
            if (consent.Location?.LastSet is DateTime lastSet) Assert.InRange(lastSet, new DateTime(2015, 1, 1), DateTime.Now.AddDays(1));
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

        string[] sections = ["[Система]", "[Wi-Fi-адаптеры]", "[Текущее подключение]", "[Видимые сети]", "[Профили сетей Windows]", "[Настройки IPv4]", "[Проводные адаптеры]", "[Маршрут в интернет]", "[Системный прокси]", "[WinHTTP-прокси]", "[Доступ к местоположению]"];
        foreach (string section in sections.Concat(["[Windows]", "[Процесс]", "[Запуск]"]))
        {
            Assert.Contains(report.Lines, line => line.Trim() == section);
        }

        // Отказы WLAN API допустимы (см. IsWlanRefusal), остальные разделы должны читаться всегда
        List<string> lines = report.Lines.ToList();
        Assert.DoesNotContain(Section(lines, "[Система]"), line => line.Contains("ОШИБКА"));
        Assert.DoesNotContain(lines.SkipWhile(line => line.Trim() != "[Wi-Fi-адаптеры]").TakeWhile(line => line.Trim() != "[Настройки IPv4]"),
            line => line.Contains("ОШИБКА") && !line.Contains("Win32Exception") && !line.Contains("UnauthorizedAccessException"));
        // Ключ сети из профиля сети Windows в отчёт не попадает ни в каком виде
        Assert.DoesNotContain(report.Lines, line => line.Contains("keyMaterial", StringComparison.OrdinalIgnoreCase));
        foreach (string section in new[] { "[Настройки IPv4]", "[Проводные адаптеры]", "[Маршрут в интернет]", "[Системный прокси]", "[WinHTTP-прокси]", "[Доступ к местоположению]" })
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
