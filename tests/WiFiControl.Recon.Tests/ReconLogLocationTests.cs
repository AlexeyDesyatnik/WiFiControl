using WiFiControl.Recon.Diagnostics;
using WiFiControl.Recon.Model;

namespace WiFiControl.Recon.Tests;

public class ReconLogLocationTests
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
    public void Location_access_of_the_user_and_the_computer_is_shown_with_desktop_apps()
    {
        Reading reading = Read();

        Assert.Equal(
            [
                @"    Раздел реестра: SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location",
                "    [Пользователь]",
                @"      Учётная запись: LAPTOP-7\alex",
                "      Общий переключатель (location): Allow — разрешён, изменён 2026-09-14 18:02:11.504",
                "      Доступ к местоположению (NonPackaged): Deny — запрещён, изменён 2026-10-20 08:40:03.012",
                "      [Классические приложения]",
                @"        C:\Program Files\Google\Chrome\Application\chrome.exe: последнее обращение 2026-10-22 21:05:00.120 – 2026-10-22 21:05:01.980",
                @"        C:\Tools\WiFiControl.Recon.exe: последнее обращение с 2026-10-23 09:15:40.001, конец не записан",
                @"      [Запросы Windows (NonPackaged\Executables)]",
                "        WiFiControl.Recon.exe: GlobalPromptShown = 1",
                "    [Компьютер]",
                "      Общий переключатель (location): Allow — разрешён, изменён 2026-09-14 18:02:11.486",
                "      Доступ к местоположению (NonPackaged): значения Value нет",
                "      [Классические приложения]",
                "        Записей нет",
                @"      [Запросы Windows (NonPackaged\Executables)]",
                "        Записей нет",
            ],
            SectionLines(reading, "Доступ к местоположению", 17));
        Assert.False(reading.HasErrors, reading.Text);
    }

    [Fact]
    public void Missing_registry_keys_are_shown_explicitly()
    {
        _system.MachineLocation = new(null, null, [], []);

        Reading reading = Read();

        Assert.Equal(
            [
                "    [Компьютер]",
                "      Общий переключатель (location): раздела реестра нет",
                "      Доступ к местоположению (NonPackaged): подраздела NonPackaged нет",
                "      [Классические приложения]",
                "        Записей нет",
                @"      [Запросы Windows (NonPackaged\Executables)]",
                "        Записей нет",
            ],
            SectionLines(reading, "Доступ к местоположению", 17).Skip(10));
    }

    [Fact]
    public void Unknown_value_and_missing_times_are_shown_as_recorded()
    {
        _system.UserLocation = new(new("Prompt", null), new("Allow", null), [new(@"C:\app.exe", null, null)], [new("app.exe", null), new("other.exe", 2)]);

        Reading reading = Read();

        Assert.Equal(
            [
                "      Общий переключатель (location): Prompt, время изменения не записано",
                "      Доступ к местоположению (NonPackaged): Allow — разрешён, время изменения не записано",
                "      [Классические приложения]",
                @"        C:\app.exe: обращение не записано",
                @"      [Запросы Windows (NonPackaged\Executables)]",
                "        app.exe: значения GlobalPromptShown нет",
                "        other.exe: GlobalPromptShown = 2",
            ],
            SectionLines(reading, "Доступ к местоположению", 10).Skip(3));
    }

    [Fact]
    public void Failure_to_read_the_user_is_recorded_and_the_computer_is_still_read()
    {
        _system.UserLocationFailure = new UnauthorizedAccessException("Доступ к разделу реестра запрещён.");

        Reading reading = Read();

        List<string> lines = SectionLines(reading, "Доступ к местоположению", 4);
        Assert.Equal("    [Пользователь]", lines[1]);
        Assert.Equal(@"      Учётная запись: LAPTOP-7\alex", lines[2]);
        Assert.StartsWith("      ОШИБКА: System.UnauthorizedAccessException", lines[3]);
        Assert.Contains("      Общий переключатель (location): Allow — разрешён, изменён 2026-09-14 18:02:11.486", reading.Lines);
        Assert.True(reading.HasErrors);
    }

    [Fact]
    public void Failure_to_read_the_computer_is_recorded_in_its_part()
    {
        _system.MachineLocationFailure = new UnauthorizedAccessException("Доступ к разделу реестра запрещён.");

        Reading reading = Read();

        List<string> lines = SectionLines(reading, "Доступ к местоположению", 12);
        Assert.Equal("    [Компьютер]", lines[10]);
        Assert.StartsWith("      ОШИБКА: System.UnauthorizedAccessException", lines[11]);
    }
}
