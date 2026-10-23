using System.ComponentModel;
using WiFiControl.Recon.Diagnostics;
using WiFiControl.Recon.Model;

namespace WiFiControl.Recon.Tests;

public class ReconLogTests
{
    private readonly FakeSystem _system = new();
    private readonly FakeClock _clock = new();

    private ReconLog NewLog() => new(_system, _clock, "1.2.3-test");

    [Fact]
    public void Reading_shows_windows_version_and_build()
    {
        Reading reading = NewLog().Read("при запуске");

        Assert.Contains("      Название в реестре: Windows 10 Pro", reading.Lines);
        Assert.Contains("      Издание: Professional", reading.Lines);
        Assert.Contains("      Выпуск: 24H2", reading.Lines);
        Assert.Contains("      Сборка: 26100.2033", reading.Lines);
        Assert.Contains("      Windows 11: да", reading.Lines);
    }

    [Fact]
    public void Reading_shows_process_user_and_administrator_rights()
    {
        _system.Process = new(@"LAPTOP-7\alex", false);

        Reading reading = NewLog().Read("при запуске");

        Assert.Contains(@"      Пользователь: LAPTOP-7\alex", reading.Lines);
        Assert.Contains("      Права администратора: нет", reading.Lines);
    }

    [Fact]
    public void Reading_again_adds_a_new_block_with_its_time_and_keeps_the_earlier_one()
    {
        ReconLog log = NewLog();
        log.Read("при запуске");
        _system.Process = new(@"LAPTOP-7\alex", false);
        _clock.Now = new DateTime(2026, 10, 23, 9, 20, 5, 8);
        log.Read("по кнопке «Прочитать заново»");

        Report report = log.BuildReport();

        int first = report.Lines.ToList().IndexOf("== Чтение 1: при запуске, 2026-10-23 09:15:42.317 ==");
        int second = report.Lines.ToList().IndexOf("== Чтение 2: по кнопке «Прочитать заново», 2026-10-23 09:20:05.008 ==");
        Assert.True(first >= 0 && second > first, report.Text);
        Assert.Equal("      Права администратора: да", report.Lines.Skip(first).First(line => line.Contains("Права администратора")));
        Assert.Equal("      Права администратора: нет", report.Lines.Skip(second).First(line => line.Contains("Права администратора")));
        Assert.Equal(2, log.Readings.Count);
    }

    [Fact]
    public void Report_starts_with_the_tool_the_computer_and_the_time_it_was_made()
    {
        ReconLog log = NewLog();
        log.Read("при запуске");
        _clock.Now = new DateTime(2026, 10, 23, 10, 0, 0, 1);

        Report report = log.BuildReport();

        Assert.Equal(
            ["WiFiControl — отчёт разведочной сборки", "Версия программы: 1.2.3-test", "Создан: 2026-10-23 10:00:00.001", "Компьютер: LAPTOP-7", "Чтений: 1", ""],
            report.Lines.Take(6));
        Assert.False(report.HasErrors);
    }

    [Fact]
    public void Report_has_errors_when_any_reading_had_them()
    {
        ReconLog log = NewLog();
        _system.WindowsFailure = new Win32Exception(2);
        log.Read("при запуске");
        _system.WindowsFailure = null;
        log.Read("по кнопке «Прочитать заново»");

        Assert.True(log.BuildReport().HasErrors);
    }

    [Fact]
    public void Failure_of_one_source_is_recorded_in_its_place_and_the_rest_is_read()
    {
        _system.ProcessFailure = new InvalidOperationException("Не прочитать маркер процесса", new Win32Exception(5, "Отказано в доступе"));

        Reading reading = NewLog().Read("при запуске");

        int process = reading.Lines.ToList().IndexOf("    [Процесс]");
        int launch = reading.Lines.ToList().IndexOf("    [Запуск]");
        Assert.True(process >= 0 && launch > process);
        Assert.Equal("      ОШИБКА: System.InvalidOperationException: Не прочитать маркер процесса", reading.Lines[process + 1]);
        Assert.Contains("      ОШИБКА: System.ComponentModel.Win32Exception (код 5): Отказано в доступе", reading.Lines);
        Assert.Contains("      Название в реестре: Windows 10 Pro", reading.Lines);
        Assert.Contains("      Из Планировщика: нет", reading.Lines);
        Assert.True(reading.HasErrors);
    }

    [Fact]
    public void Launch_by_the_scheduler_service_is_recognised_with_its_basis()
    {
        _system.Launch = new(new ParentProcess(1388, "svchost"), 1388);

        Reading reading = NewLog().Read("при запуске");

        Assert.Contains("      Из Планировщика: да", reading.Lines);
        Assert.Contains("      Основание: родительский процесс svchost (PID 1388) — процесс службы Планировщика (Schedule)", reading.Lines);
    }

    [Fact]
    public void Launch_by_another_parent_is_not_from_the_scheduler()
    {
        _system.Launch = new(new ParentProcess(4120, "explorer"), 1388);

        Reading reading = NewLog().Read("при запуске");

        Assert.Contains("      Из Планировщика: нет", reading.Lines);
        Assert.Contains("      Основание: родительский процесс explorer (PID 4120), а служба Планировщика (Schedule) работает в процессе PID 1388", reading.Lines);
    }

    [Fact]
    public void Launch_cannot_be_judged_when_the_parent_has_already_exited()
    {
        _system.Launch = new(new ParentProcess(4120, null), 1388);

        Reading reading = NewLog().Read("при запуске");

        Assert.Contains("      Из Планировщика: не определить", reading.Lines);
        Assert.Contains("      Основание: родительский процесс PID 4120 уже завершился", reading.Lines);
    }

    [Fact]
    public void Launch_cannot_be_judged_when_the_parent_is_unknown()
    {
        _system.Launch = new(null, 1388);

        Reading reading = NewLog().Read("при запуске");

        Assert.Contains("      Из Планировщика: не определить", reading.Lines);
        Assert.Contains("      Основание: номер родительского процесса неизвестен", reading.Lines);
    }

    [Fact]
    public void Launch_is_not_from_the_scheduler_when_its_service_is_not_running()
    {
        _system.Launch = new(new ParentProcess(4120, "explorer"), null);

        Reading reading = NewLog().Read("при запуске");

        Assert.Contains("      Из Планировщика: нет", reading.Lines);
        Assert.Contains("      Основание: родительский процесс explorer (PID 4120), а служба Планировщика (Schedule) не запущена", reading.Lines);
    }
}

public class ReconLogSaveTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "WiFiControl.Tests." + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    [Fact]
    public void Saved_report_is_named_after_the_computer_and_the_clock_and_holds_every_reading()
    {
        var clock = new FakeClock();
        var log = new ReconLog(new FakeSystem { ComputerName = "LAPTOP-7" }, clock, "1.2.3-test");
        log.Read("при запуске");
        clock.Now = new DateTime(2026, 10, 23, 11, 7, 59);
        log.Read("по кнопке «Прочитать заново»");

        string path = log.Save(_folder);

        Assert.Equal(Path.Combine(_folder, "Отчёты", "LAPTOP-7_2026-10-23_11-07.txt"), path);
        string text = File.ReadAllText(path);
        Assert.Contains("== Чтение 1: при запуске, 2026-10-23 09:15:42.317 ==", text);
        Assert.Contains("== Чтение 2: по кнопке «Прочитать заново», 2026-10-23 11:07:59.000 ==", text);
    }
}
