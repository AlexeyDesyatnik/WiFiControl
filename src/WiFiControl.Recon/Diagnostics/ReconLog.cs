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
                report.Section("Видимые сети", () => VisibleNetworksSection(report, adapters));
                report.Section("Профили сетей Windows", () => WindowsProfilesSection(report, adapters));
                report.Section("Случайный MAC-адрес", () => RandomMacSection(report, adapters));
                // Адаптеры и метрики нужны нескольким разделам: читаются один раз, отказ записывается в каждом разделе, где понадобились
                var networkAdapters = new Lazy<IReadOnlyList<Adapter>>(system.ReadAdapters);
                var interfaces = new Lazy<IReadOnlyList<IpInterface>>(system.ReadIpInterfaces);
                report.Section("Настройки IPv4", () => Ipv4Section(report, adapters, networkAdapters));
                report.Section("Проводные адаптеры", () => WiredSection(report, networkAdapters, interfaces));
                report.Section("Маршрут в интернет", () => InternetRouteSection(report, networkAdapters, interfaces));
                report.Section("Системный прокси", () => SystemProxySection(report));
                report.Section("WinHTTP-прокси", () => ProxyValues(report, system.ReadWinHttpProxy()));
                report.Section("Доступ к местоположению", () => LocationSection(report));
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

                BssEntry? bss = system.ReadBssList(adapter.Id).FirstOrDefault(entry => entry.Bssid == connection.Bssid);
                if (bss == null)
                {
                    report.Value("Частота", "нет — точки доступа с этим BSSID нет в списке BSS");
                    return;
                }

                report.Value("Частота", Frequency(bss));
                report.Value("RSSI", bss.Rssi + " дБм");
            });
        }
    }

    private void VisibleNetworksSection(Report report, IReadOnlyList<WifiAdapter>? adapters)
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
                // Без свежего сканирования список всё равно читается: Windows отдаст то, что видела в прошлый раз
                report.Section("Сканирование", () => ScanValues(report, adapter));

                // Точки доступа нужны каждой сети: список читается один раз, отказ записывается у каждой сети
                var bssList = new Lazy<IReadOnlyList<BssEntry>>(() => system.ReadBssList(adapter.Id));
                List<IGrouping<string, AvailableNetwork>> networks = system.ReadAvailableNetworks(adapter.Id).GroupBy(n => n.Ssid).ToList();
                report.Value("Сетей", networks.Count);

                foreach (IGrouping<string, AvailableNetwork> network in networks)
                {
                    report.Section(NetworkTitle(network.Key), () =>
                    {
                        foreach (AvailableNetwork entry in network)
                        {
                            report.Value(entry.WindowsProfileName.Length == 0
                                ? "Без профиля сети Windows"
                                : "С профилем сети Windows «" + entry.WindowsProfileName + "»", NetworkText(entry));
                        }
                        report.Section("Точки доступа", () => BssValues(report, bssList.Value.Where(bss => bss.Ssid == network.Key)));
                    });
                }

                // Точки доступа, чьей сети нет в списке, тоже показываются: по ним видно, что список и BSS разошлись
                IReadOnlyList<BssEntry> bssRead;
                try
                {
                    bssRead = bssList.Value;
                }
                catch (Exception) when (networks.Count > 0)
                {
                    // Отказ уже записан у каждой сети; без сетей он записывается в разделе адаптера
                    return;
                }
                List<BssEntry> unlisted = bssRead.Where(bss => networks.All(n => n.Key != bss.Ssid)).ToList();
                if (unlisted.Count > 0) report.Section("Точки доступа без сети в списке", () => BssValues(report, unlisted, withSsid: true));
            });
        }
    }

    private void ScanValues(Report report, WifiAdapter adapter)
    {
        report.Value("Ждём уведомления о конце не дольше", Seconds(ScanTimeout));
        DateTime start = clock.Now;
        bool completed = system.Scan(adapter.Id, ScanTimeout);
        string waited = Seconds(clock.Now - start);
        report.Value("Уведомление о конце", completed ? "пришло через " + waited : "не пришло за " + waited + " — список сетей прочитан без него");
    }

    private static void BssValues(Report report, IEnumerable<BssEntry> entries, bool withSsid = false)
    {
        bool any = false;
        foreach (BssEntry bss in entries)
        {
            any = true;
            report.Value(bss.Bssid, (withSsid ? NetworkTitle(bss.Ssid) + ", " : "") + Frequency(bss) + ", " + bss.Rssi + " дБм");
        }
        if (!any) report.Line("Нет в списке BSS");
    }

    private static string NetworkTitle(string ssid) => ssid.Length == 0 ? "(скрытая сеть — имя не передаётся)" : ssid;

    private static string NetworkText(AvailableNetwork network)
    {
        string text = "сигнал " + network.SignalQuality + " %, "
            + (network.IsSecurityEnabled ? "защита " : "без защиты: ") + network.Authentication + ", " + network.Cipher;
        if (network.BssType != "Infrastructure") text += ", тип " + network.BssType;
        if (!network.IsConnectable) text += ", подключиться нельзя";
        return text;
    }

    private static string Frequency(BssEntry bss) =>
        bss.FrequencyKhz / 1000 + " МГц (" + Report.Format(bss.BandGhz) + " ГГц, канал " + bss.Channel + ")";

    private void WindowsProfilesSection(Report report, IReadOnlyList<WifiAdapter>? adapters)
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
                IReadOnlyList<WindowsProfile> profiles = system.ReadWindowsProfiles(adapter.Id);
                if (profiles.Count == 0) report.Line("Профилей сетей Windows нет");

                foreach (WindowsProfile profile in profiles)
                {
                    report.Section(profile.Name, () =>
                    {
                        report.Value("SSID", profile.Ssid ?? "элемента SSID нет");
                        report.Value("Тип", profile.Type);
                        report.Value("Место в порядке предпочтения", profile.Position + 1);
                        report.Value("Режим подключения", profile.ConnectionMode switch
                        {
                            null => "элемента connectionMode нет",
                            "auto" => "auto — подключаться автоматически",
                            "manual" => "manual — только вручную",
                            string other => other,
                        });
                        report.Value("Случайный MAC-адрес в XML", MacRandomizationText(profile));
                    });
                }
            });
        }
    }

    // Источники признака выводятся рядом и не сводятся в общий вывод: какой из них надёжен, решают прогоны
    private void RandomMacSection(Report report, IReadOnlyList<WifiAdapter>? adapters)
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
                report.Section("Адреса адаптера", () =>
                {
                    MacAddresses addresses = system.ReadMacAddresses(adapter.Id);
                    report.Value("Текущий", addresses.Current);
                    report.Value("Постоянный", addresses.Permanent);
                    report.Value("Текущий отличается от постоянного", addresses.Current != addresses.Permanent);
                    report.Value("Текущий локально администрируемый", MacAddresses.IsLocallyAdministered(addresses.Current)
                        ? "да — так выглядит случайный адрес"
                        : "нет");
                });

                report.Section("Реестр", () =>
                {
                    MacRegistry registry = system.ReadMacRegistry(adapter.Id);
                    report.Value("Раздел службы WLAN", registry.WlanSvcKey ?? "раздела нет");
                    // По сторонним описаниям так записан общий переключатель «Случайные аппаратные адреса»; не проверено
                    if (registry.WlanSvcKey != null) report.Value("RandomMacState", registry.RandomMacState ?? "значения нет");
                    report.Value("Раздел драйвера", registry.DeviceKey ?? "не найден");
                    if (registry.DeviceKey != null) report.Value("NetworkAddress (адрес, заданный вручную)", registry.NetworkAddress ?? "значения нет");
                });

                report.Section("XML профиля текущей сети Windows", () =>
                {
                    WifiConnection? connection = system.ReadCurrentConnection(adapter.Id);
                    if (connection == null)
                    {
                        report.Line("Не прочитано: адаптер не подключён — текущей сети нет");
                        return;
                    }

                    report.Value("Профиль сети Windows", connection.WindowsProfileName);
                    WindowsProfile? profile = system.ReadWindowsProfiles(adapter.Id).FirstOrDefault(p => p.Name == connection.WindowsProfileName);
                    report.Value("MacRandomization", profile == null
                        ? "нет — такого профиля нет в списке профилей сетей Windows"
                        : MacRandomizationText(profile));
                });
            });
        }
    }

    private static string MacRandomizationText(WindowsProfile profile) => profile.MacRandomization == null
        ? "элемента MacRandomization нет"
        : Report.Format(profile.MacRandomization.Select(value => value.Name + " = " + value.Value).ToArray());

    private static void Ipv4Section(Report report, IReadOnlyList<WifiAdapter>? wifiAdapters, Lazy<IReadOnlyList<Adapter>> networkAdapters)
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

        IReadOnlyList<Adapter> adapters = networkAdapters.Value;
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

    private static void WiredSection(Report report, Lazy<IReadOnlyList<Adapter>> networkAdapters, Lazy<IReadOnlyList<IpInterface>> interfaces)
    {
        // У выключенного адаптера признак Wi-Fi неизвестен, поэтому он попадает сюда с пометкой
        List<Adapter> wired = networkAdapters.Value.Where(a => a.IsPhysical && !a.IsWireless).ToList();
        if (wired.Count == 0)
        {
            report.Line("Проводных адаптеров нет");
            return;
        }

        foreach (Adapter adapter in wired)
        {
            report.Section(adapter.Name, () =>
            {
                report.Value("Описание", adapter.Description);
                report.Value("Состояние", adapter.State);
                if (adapter.State is AdapterState.Disabled or AdapterState.Unavailable)
                    report.Value("Тип", "не определить — у выключенного адаптера Windows не сообщает, Wi-Fi он или проводной");
                if (adapter.Active == null) report.Value("Шлюзы", "не прочитаны — нет соединения");
                else report.Value("Шлюзы", adapter.Active.Gateways.ToArray());

                IpInterface? ip = interfaces.Value.FirstOrDefault(i => i.AdapterId == adapter.Id);
                report.Value("Метрика интерфейса", ip == null
                    ? "нет — у адаптера нет интерфейса IPv4"
                    : ip.Metric + (ip.AutomaticMetric ? ", назначена автоматически" : ", задана вручную"));
            });
        }
    }

    private void InternetRouteSection(Report report, Lazy<IReadOnlyList<Adapter>> networkAdapters, Lazy<IReadOnlyList<IpInterface>> interfaces)
    {
        report.Value("Адрес назначения", InternetAddress + " — маршрут выбирает GetBestRoute2 по таблице, трафик не отправляется");

        BestRoute? best = null;
        bool bestRead = false;
        report.Section("Выбранный маршрут", () =>
        {
            best = system.ReadBestRoute(InternetAddress);
            bestRead = true;
            if (best == null)
            {
                report.Line("Маршрута нет");
                return;
            }

            report.Value("Адаптер", AdapterName(networkAdapters, best.Route));
            // Не обязательно маршрут по умолчанию: VPN часто добавляет более узкие маршруты, перекрывающие его
            report.Value("Сеть назначения", best.Route.Destination);
            report.Value("Шлюз", best.Route.NextHop);
            report.Value("Адрес отправителя", best.SourceAddress);
            report.Value("Метрика", RouteMetric(interfaces, best.Route));
        });

        report.Section("Маршруты по умолчанию", () =>
        {
            IReadOnlyList<Route> routes = system.ReadDefaultRoutes();
            if (routes.Count == 0) report.Line("Маршрутов по умолчанию нет");

            foreach (Route route in routes)
            {
                report.Section(AdapterName(networkAdapters, route), () =>
                {
                    report.Value("Шлюз", route.NextHop);
                    report.Value("Выбран", bestRead
                        ? best?.Route == route
                        : "не определить — выбранный маршрут не прочитан");
                    report.Value("Метрика", RouteMetric(interfaces, route));
                });
            }
        });
    }

    private static string AdapterName(Lazy<IReadOnlyList<Adapter>> networkAdapters, Route route)
    {
        string byNumber = "интерфейс " + route.InterfaceIndex + ", " + route.AdapterId.ToString("B");
        IReadOnlyList<Adapter> adapters;
        try
        {
            adapters = networkAdapters.Value;
        }
        catch (Exception)
        {
            // Отказ уже записан в разделах адаптеров; маршрут остаётся в отчёте и без имени адаптера
            return byNumber + " — адаптеры не прочитаны";
        }

        Adapter? adapter = adapters.FirstOrDefault(a => a.Id == route.AdapterId);
        return adapter == null ? byNumber + " — в «Сетевых подключениях» такого адаптера нет" : adapter.Name + " (" + adapter.Description + ")";
    }

    // Windows выбирает маршрут с наименьшей суммой метрик маршрута и интерфейса
    private static string RouteMetric(Lazy<IReadOnlyList<IpInterface>> interfaces, Route route)
    {
        IpInterface? ip = interfaces.Value.FirstOrDefault(i => i.Index == route.InterfaceIndex);
        if (ip == null) return route.Metric + " маршрута, метрики интерфейса нет — нет интерфейса IPv4 с номером " + route.InterfaceIndex;
        return route.Metric + " маршрута + " + ip.Metric + " интерфейса = " + (route.Metric + ip.Metric);
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

    private void LocationSection(Report report)
    {
        report.Value("Раздел реестра", LocationConsent.RegistryKey);

        report.Section("Пользователь", () =>
        {
            // Доступ выдаётся пользователю сеанса, даже если права администратора получены под другой учётной записью
            InteractiveUser user = system.ReadInteractiveUser();
            report.Value("Учётная запись", user.Name);
            LocationConsentValues(report, system.ReadUserLocationConsent(user.Sid));
        });

        report.Section("Компьютер", () => LocationConsentValues(report, system.ReadMachineLocationConsent()));
    }

    private static void LocationConsentValues(Report report, LocationConsent consent)
    {
        report.Value("Общий переключатель (location)", consent.Location == null ? "раздела реестра нет" : SwitchText(consent.Location));
        report.Value("Доступ к местоположению (NonPackaged)", consent.DesktopApps == null ? "подраздела NonPackaged нет" : SwitchText(consent.DesktopApps));

        report.Section("Классические приложения", () =>
        {
            if (consent.DesktopAppUsages.Count == 0) report.Line("Записей нет");
            foreach (DesktopAppUsage app in consent.DesktopAppUsages)
            {
                report.Value(app.Path, UsageText(app));
            }
        });

        // Похоже, отсюда видно, приходил ли запрос Windows при первом обращении (сценарий 6); пока не проверено, значение показывается как есть
        report.Section(@"Запросы Windows (NonPackaged\Executables)", () =>
        {
            if (consent.Prompts.Count == 0) report.Line("Записей нет");
            foreach (LocationPrompt prompt in consent.Prompts)
            {
                report.Value(prompt.Executable, prompt.GlobalPromptShown is int shown ? "GlobalPromptShown = " + shown : "значения GlobalPromptShown нет");
            }
        });
    }

    private static string SwitchText(ConsentSwitch consentSwitch)
    {
        if (consentSwitch.Value == null) return "значения Value нет";

        string text = consentSwitch.Value switch
        {
            "Allow" => "Allow — разрешён",
            "Deny" => "Deny — запрещён",
            _ => consentSwitch.Value,
        };
        return text + (consentSwitch.LastSet is DateTime lastSet ? ", изменён " + Stamp(lastSet) : ", время изменения не записано");
    }

    private static string UsageText(DesktopAppUsage app)
    {
        if (app.LastUsedStart is not DateTime start) return "обращение не записано";
        if (app.LastUsedStop is not DateTime stop) return "последнее обращение с " + Stamp(start) + ", конец не записан";
        return "последнее обращение " + Stamp(start) + " – " + Stamp(stop);
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

    // Любой публичный адрес: важен только маршрут, который Windows выбрала бы для интернета
    private const string InternetAddress = "8.8.8.8";

    // Драйверу на сканирование отводится 4 секунды; запас — на медленные драйверы и выход из сна
    private static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(10);

    private static string Seconds(TimeSpan duration) => duration.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture) + " с";
    private static string Stamp(DateTime when) => when.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
}
