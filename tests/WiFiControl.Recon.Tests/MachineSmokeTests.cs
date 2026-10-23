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

    [Fact]
    public void Builds_a_full_report_for_this_machine()
    {
        var log = new ReconLog(new WindowsSystem(), new SystemClock(), "тест");
        log.Read("при запуске");

        Report report = log.BuildReport();

        // Отчёт остаётся в каталоге сборки тестов, чтобы его можно было просмотреть глазами
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "machine-report.txt"), report.Text);

        foreach (string section in new[] { "[Система]", "[Windows]", "[Процесс]", "[Запуск]" })
        {
            Assert.Contains(report.Lines, line => line.Trim() == section);
        }
        Assert.False(report.HasErrors, report.Text);
    }
}
