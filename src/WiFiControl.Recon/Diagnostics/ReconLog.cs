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
