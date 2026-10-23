using System.Globalization;
using WiFiControl.Recon.Model;

namespace WiFiControl.Recon.Diagnostics;

/// <summary>Одно чтение состояния компьютера, уже записанное строками отчёта.</summary>
internal sealed record Reading(int Number, string Occasion, DateTime When, IReadOnlyList<string> Lines, bool HasErrors)
{
    public string Text => string.Join(Environment.NewLine, Lines);
}

/// <summary>
/// Всё, что разведочная сборка прочитала за запуск. Каждое чтение — отдельный блок со временем;
/// повторное чтение добавляет блок, а не заменяет прежние.
/// </summary>
internal sealed class ReconLog(IReadOnlySystem system, IClock clock, string toolVersion)
{
    private readonly List<Reading> _readings = [];
    private readonly Lock _readLock = new();

    public IReadOnlyList<Reading> Readings
    {
        get
        {
            lock (_readings) return _readings.ToList();
        }
    }

    public Report BuildReport() => BuildReport(clock.Now);

    private Report BuildReport(DateTime created)
    {
        var report = new Report();
        report.Line("WiFiControl — отчёт разведочной сборки");
        report.Value("Версия программы", toolVersion);
        report.Value("Создан", Stamp(created));
        report.Value("Компьютер", system.ComputerName);

        IReadOnlyList<Reading> readings = Readings;
        report.Value("Чтений", readings.Count);
        foreach (Reading reading in readings) report.Append(reading.Lines, reading.HasErrors);
        return report;
    }

    /// <summary>Сохраняет отчёт в папку «Отчёты» внутри <paramref name="baseFolder"/> и возвращает путь к файлу.</summary>
    public string Save(string baseFolder)
    {
        DateTime now = clock.Now;
        return ReportFile.Save(baseFolder, system.ComputerName, now, BuildReport(now).Text);
    }

    /// <param name="occasion">Повод чтения, например «при запуске».</param>
    public Reading Read(string occasion)
    {
        DateTime when = clock.Now;
        // Чтения идут по одному, чтобы номера блоков не повторялись
        lock (_readLock)
        {
            int number = Readings.Count + 1;

            var report = new Report();
            report.Section("Чтение " + number + ": " + occasion + ", " + Stamp(when), () =>
            {
                report.Section("Система", () => SystemSection(report));
                // null — список не получен: отказ уже записан в разделе адаптеров
                IReadOnlyList<WifiAdapter>? adapters = null;
                report.Section("Wi-Fi-адаптеры", () => adapters = WifiAdaptersSection(report));
                report.Section("Текущее подключение", () => CurrentConnectionSection(report, adapters));
                report.Section("Настройки IPv4", () => Ipv4Section(report, adapters));
                report.Section("Системный прокси", () => SystemProxySection(report));
                report.Section("WinHTTP-прокси", () => ProxyValues(report, system.ReadWinHttpProxy()));
            });

            var reading = new Reading(number, occasion, when, report.Lines.ToList(), report.HasErrors);
            lock (_readings) _readings.Add(reading);
            return reading;
        }
    }

    private void SystemSection(Report report)
    {
        report.Section("Windows", () =>
        {
            WindowsVersion windows = system.ReadWindows();
            report.Value("Название в реестре", windows.ProductName);
            report.Value("Издание", windows.Edition);
            report.Value("Выпуск", windows.DisplayVersion);
            report.Value("Сборка", windows.Build + "." + windows.Revision);
            report.Value("Windows 11", windows.IsWindows11);
        });

        report.Section("Процесс", () =>
        {
            ProcessIdentity process = system.ReadProcess();
            report.Value("Пользователь", process.User);
            report.Value("Права администратора", process.IsAdministrator);
        });

        report.Section("Запуск", () =>
        {
            LaunchOrigin launch = system.ReadLaunch();
            report.Value("Из Планировщика", launch.FromScheduler is bool fromScheduler ? fromScheduler : "не определить");
            report.Value("Основание", Basis(launch));
        });
    }

    private IReadOnlyList<WifiAdapter> WifiAdaptersSection(Report report)
    {
        IReadOnlyList<WifiAdapter> adapters = system.ReadWifiAdapters();
        if (adapters.Count == 0) report.Line("Wi-Fi-адаптеров нет");

        foreach (WifiAdapter adapter in adapters)
        {
            report.Section(adapter.Description, () =>
            {
                report.Value("GUID", adapter.Id);
                report.Value("Описание", adapter.Description);
                report.Value("Состояние", adapter.State);

                // Читается внутри раздела адаптера, чтобы отказ попал к нему, а GUID и состояние остались в отчёте
                NetworkConnection? connection = system.ReadNetworkConnections().FirstOrDefault(c => c.Id == adapter.Id);
                report.Value("Сетевое подключение", connection == null
                    ? "нет — в «Сетевых подключениях» нет адаптера с таким GUID"
                    : connection.Name + " (" + connection.Description + "), " + connection.Status);
            });
        }
        return adapters;
    }

    private void CurrentConnectionSection(Report report, IReadOnlyList<WifiAdapter>? adapters)
    {
        if (adapters == null)
        {
            report.Line("Не прочитано: нет списка Wi-Fi-адаптеров");
            return;
        }
        if (adapters.Count == 0) report.Line("Wi-Fi-адаптеров нет");

        foreach (WifiAdapter adapter in adapters)
        {
            report.Section(adapter.Description, () =>
            {
                WifiConnection? connection = system.ReadCurrentConnection(adapter.Id);
                if (connection == null)
                {
                    report.Line("Не подключён");
                    return;
                }

                report.Value("SSID", connection.Ssid);
                report.Value("BSSID", connection.Bssid);
                report.Value("Профиль сети Windows", connection.WindowsProfileName);
                report.Value("Режим подключения", connection.Mode);
                report.Value("Защита включена", connection.IsSecurityEnabled);
                report.Value("Тип защиты", connection.Authentication + ", " + connection.Cipher);
                report.Value("Сигнал", connection.SignalQuality + " %");

                ConnectedBss? bss = system.ReadConnectedBss(adapter.Id, connection.Bssid);
                if (bss == null)
                {
                    report.Value("Частота", "нет — точки доступа с этим BSSID нет в списке BSS");
                    return;
                }

                report.Value("Частота", bss.FrequencyKhz / 1000 + " МГц (" + Report.Format(bss.BandGhz) + " ГГц, канал " + bss.Channel + ")");
                report.Value("RSSI", bss.Rssi + " дБм");
            });
        }
    }

    private void Ipv4Section(Report report, IReadOnlyList<WifiAdapter>? wifiAdapters)
    {
        if (wifiAdapters == null)
        {
            report.Line("Не прочитано: нет списка Wi-Fi-адаптеров");
            return;
        }
        if (wifiAdapters.Count == 0)
        {
            report.Line("Wi-Fi-адаптеров нет");
            return;
        }

        IReadOnlyList<Adapter> adapters = system.ReadAdapters();
        foreach (WifiAdapter wifi in wifiAdapters)
        {
            report.Section(wifi.Description, () =>
            {
                Adapter? adapter = adapters.FirstOrDefault(a => a.Id == wifi.Id);
                if (adapter == null)
                {
                    report.Line("Не прочитано: в «Сетевых подключениях» (Win32_NetworkAdapter) нет адаптера с таким GUID");
                    return;
                }

                report.Value("Адаптер в «Сетевых подключениях»", adapter.Name + ", " + adapter.State);
                report.Section("Настроено", () => ConfiguredIpv4(report, adapter.Configured));
                report.Section("Действует", () => ActiveIpv4(report, adapter.Active));
            });
        }
    }

    private static void ConfiguredIpv4(Report report, Ipv4Settings settings)
    {
        report.Value("Получение адреса", settings.AutoAddress ? "автоматически" : "вручную");
        if (!settings.AutoAddress)
        {
            report.Value("Адрес", settings.Address);
            report.Value("Маска", settings.Mask);
            report.Value("Шлюз", settings.Gateway);
        }

        report.Value("Получение DNS", settings.AutoDns ? "автоматически" : "вручную");
        if (!settings.AutoDns) report.Value("DNS", settings.Dns.ToArray());
    }

    private static void ActiveIpv4(Report report, ActiveIpv4? active)
    {
        if (active == null)
        {
            report.Line("Не прочитано: нет соединения — сетевой стек отдал бы адрес 169.254.x.x и прежние DNS");
            return;
        }

        report.Value("Адреса", active.Addresses.ToArray());
        report.Value("Шлюзы", active.Gateways.ToArray());
        report.Value("DNS", active.Dns.ToArray());
        report.Value("Конфликт адресов", active.HasConflict);
    }

    private void SystemProxySection(Report report)
    {
        InteractiveUser user = system.ReadInteractiveUser();
        report.Value("Интерактивный пользователь", user.Name);
        report.Value("SID", user.Sid);
        report.Value("Он же владелец процесса", user.IsProcessOwner);

        SystemProxyState proxy = system.ReadSystemProxy(user.Sid);
        ProxyValues(report, proxy.Settings);
        report.Value("Автоопределение", proxy.AutoDetect);
        report.Value("Сценарий настройки", proxy.AutoConfigUrl);

        report.Section("Политики", () =>
        {
            ProxyPolicy policy = system.ReadProxyPolicy(user.Sid);
            report.Value("Прокси один на компьютер", policy.PerMachine);
            report.Value("Значения задаёт политика", policy.SetsValues);
            report.Value("Изменение запрещено политикой", policy.ChangeLocked);
        });
    }

    private static void ProxyValues(Report report, ProxySettings proxy)
    {
        report.Value("Прокси включён", proxy.Enabled);
        report.Value("Сервер", proxy.Server);
        report.Value("Исключения", proxy.Exceptions.ToArray());
        report.Value("Не через прокси для локальных адресов", proxy.BypassLocal);
    }

    private static string Basis(LaunchOrigin launch)
    {
        ParentProcess? parent = launch.Parent;
        if (parent == null) return "номер родительского процесса неизвестен";
        if (parent.Name == null) return "родительский процесс PID " + parent.Id + " уже завершился";

        string about = "родительский процесс " + parent.Name + " (PID " + parent.Id + ")";
        if (launch.FromScheduler == true) return about + " — процесс службы Планировщика (Schedule)";
        if (launch.SchedulerProcessId == null) return about + ", а служба Планировщика (Schedule) не запущена";
        return about + ", а служба Планировщика (Schedule) работает в процессе PID " + launch.SchedulerProcessId;
    }

    private static string Stamp(DateTime when) => when.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
}
