using System.ComponentModel;
using WiFiControl.Recon.Diagnostics;
using WiFiControl.Recon.Model;

namespace WiFiControl.Recon.Tests;

public class ReconLogProxyTests
{
    private readonly FakeSystem _system = new();
    private readonly FakeClock _clock = new();

    private Reading Read() => new ReconLog(_system, _clock, "1.2.3-test").Read("при запуске");

    private static List<string> SectionLines(Reading reading, string section, int count)
    {
        int start = reading.Lines.ToList().IndexOf("  [" + section + "]");
        Assert.True(start >= 0, reading.Text);
        return reading.Lines.Skip(start + 1).Take(count).ToList();
    }

    [Fact]
    public void System_proxy_of_the_interactive_user_is_shown_with_auto_detect_script_and_policy()
    {
        Reading reading = Read();

        Assert.Equal(
            [
                @"    Интерактивный пользователь: LAPTOP-7\alex",
                "    SID: S-1-5-21-1004336348-1177238915-682003330-1001",
                "    Он же владелец процесса: да",
                "    Прокси включён: да",
                "    Сервер: 10.0.68.52:3128",
                "    Исключения: *.corp.local, 10.*",
                "    Не через прокси для локальных адресов: да",
                "    Автоопределение: да",
                "    Сценарий настройки: http://wpad.corp.local/proxy.pac",
                "    [Политики]",
                "      Прокси один на компьютер: нет",
                "      Значения задаёт политика: нет",
                "      Изменение запрещено политикой: нет",
            ],
            SectionLines(reading, "Системный прокси", 13));
        Assert.False(reading.HasErrors, reading.Text);
    }

    [Fact]
    public void Winhttp_proxy_is_shown()
    {
        Reading reading = Read();

        Assert.Equal(
            [
                "    Прокси включён: нет",
                "    Сервер: (пусто)",
                "    Исключения: (пусто)",
                "    Не через прокси для локальных адресов: нет",
            ],
            SectionLines(reading, "WinHTTP-прокси", 4));
    }

    [Fact]
    public void Proxy_set_by_policy_is_marked()
    {
        _system.ProxyPolicy = new(PerMachine: true, SetsValues: true, ChangeLocked: true);

        Reading reading = Read();

        Assert.Contains("      Прокси один на компьютер: да", reading.Lines);
        Assert.Contains("      Значения задаёт политика: да", reading.Lines);
        Assert.Contains("      Изменение запрещено политикой: да", reading.Lines);
    }

    [Fact]
    public void Process_running_under_another_account_is_marked()
    {
        _system.InteractiveUser = _system.InteractiveUser with { IsProcessOwner = false };

        Reading reading = Read();

        Assert.Contains("    Он же владелец процесса: нет", reading.Lines);
    }

    [Fact]
    public void Failure_to_find_the_interactive_user_is_recorded_and_winhttp_is_still_read()
    {
        _system.InteractiveUserFailure = new Win32Exception(1008, "WTSQuerySessionInformation: Попытка ссылки на несуществующий токен");

        Reading reading = Read();

        Assert.Equal(
            ["    ОШИБКА: System.ComponentModel.Win32Exception (код 1008): WTSQuerySessionInformation: Попытка ссылки на несуществующий токен"],
            SectionLines(reading, "Системный прокси", 1));
        Assert.Equal(["    Прокси включён: нет"], SectionLines(reading, "WinHTTP-прокси", 1));
        Assert.True(reading.HasErrors);
    }

    [Fact]
    public void Failure_to_read_policy_is_recorded_after_the_proxy_values()
    {
        _system.ProxyPolicyFailure = new UnauthorizedAccessException("Доступ к разделу реестра запрещён.");

        Reading reading = Read();

        List<string> lines = SectionLines(reading, "Системный прокси", 11);
        Assert.Equal("    Сценарий настройки: http://wpad.corp.local/proxy.pac", lines[8]);
        Assert.Equal("    [Политики]", lines[9]);
        Assert.StartsWith("      ОШИБКА: System.UnauthorizedAccessException", lines[10]);
    }

    [Fact]
    public void Failure_to_read_winhttp_proxy_is_recorded_in_its_section()
    {
        _system.WinHttpProxyFailure = new Win32Exception(12004, "WinHttpGetDefaultProxyConfiguration: Внутренняя ошибка");

        Reading reading = Read();

        Assert.Equal(
            ["    ОШИБКА: System.ComponentModel.Win32Exception (код 12004): WinHttpGetDefaultProxyConfiguration: Внутренняя ошибка"],
            SectionLines(reading, "WinHTTP-прокси", 1));
        Assert.Contains("    Прокси включён: да", reading.Lines);
    }
}
