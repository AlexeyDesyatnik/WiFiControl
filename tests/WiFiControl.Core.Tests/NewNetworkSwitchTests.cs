using System.Text;
using System.Xml.Linq;
using WiFiControl.Core.Model;

namespace WiFiControl.Core.Tests;

/// <summary>Переключение на новую сеть с созданием профиля сети Windows и «Ввести пароль заново» у знакомой.</summary>
public class NewNetworkSwitchTests
{
    private const string Disconnect = "Отключение от сети «Home»";
    private const string Ipv4 = "Настройки IPv4 адаптера «Беспроводная сеть»";
    private const string Create = "Создание профиля сети Windows «Cafe»";
    private const string ConnectCafe = "Подключение к сети «Cafe»";
    private const string Delete = "Удаление профиля сети Windows «Cafe»";
    private const string NewPassword = "Новый пароль в профиле сети Windows «Office»";
    private const string ConnectOffice = "Подключение к сети «Office»";
    private const string Ipv4Rollback = "Откат настроек IPv4";
    private const string Return = "Возврат в сеть «Home»";

    private readonly RunningClock _time = new();
    private readonly FakeSystem _system;
    private readonly FakeWifi _wifi = new();
    private readonly Applier _applier;
    private readonly Adapter _adapter = FakeSystem.WiFi(AdapterState.Connected);
    private readonly NewWindowsProfile _cafe = WindowsProfileXml.Build("Cafe", NewNetworkSecurity.Wpa2Personal, "cafe-1234", autoConnect: true);

    public NewNetworkSwitchTests()
    {
        _system = new FakeSystem(_time);
        _adapter.Active.Addresses.Add("192.168.5.20/24");
        _adapter.Active.Gateways.Add("192.168.5.1");
        _system.Adapters.Add(_adapter);
        _system.DefaultRoutes.Add(new DefaultRoute(FakeWifi.WifiId, "192.168.5.1"));
        _system.Answering.Add("192.168.5.1");

        _wifi.AddWindowsProfile(WindowsProfileXml.Build("Home", NewNetworkSecurity.Wpa2Personal, "home-1234", autoConnect: true));
        _wifi.AddWindowsProfile(WindowsProfileXml.Build("Office", NewNetworkSecurity.Wpa2Personal, "old-office", autoConnect: false));
        _wifi.Current = Encoding.UTF8.GetBytes("Home");
        _applier = new Applier(_system, _wifi, _time);
    }

    private Task<SwitchResult> SwitchToCafeAsync() =>
        _applier.SwitchToNewAsync(_adapter, "Cafe", _cafe, Profile.Automatic, onStep: null, CancellationToken.None);

    private Task<SwitchResult> ReenterOfficeAsync(string password = "new-office") =>
        _applier.SwitchWithPasswordAsync(_adapter, "Office", "Office", password, Profile.Automatic, onStep: null, CancellationToken.None);

    private static StepResult Step(SwitchResult result, string title) => result.Steps.Single(step => step.Title == title);

    private string? KeyMaterial(string windowsProfile)
    {
        XNamespace v1 = "http://www.microsoft.com/networking/WLAN/profile/v1";
        return XDocument.Parse(_wifi.Xml[windowsProfile]).Descendants(v1 + "keyMaterial").Single().Value;
    }

    [Fact]
    public async Task New_network_gets_its_windows_profile_created_before_connecting()
    {
        SwitchResult result = await SwitchToCafeAsync();

        Assert.Equal([Disconnect, Ipv4, Create, ConnectCafe], result.Steps.Take(4).Select(step => step.Title));
        Assert.All(result.Steps, step => Assert.True(step.Ok));
        Assert.Equal(["disconnect", "save Cafe", "connect Cafe"], _wifi.Calls);
        Assert.Contains(_wifi.WindowsProfiles, profile => profile.Name == "Cafe");
        Assert.Equal(new LastApplication("Cafe", Profile.Automatic.Id), _applier.Last);
        Assert.False(result.PasswordRejected);
    }

    [Fact]
    public async Task Wrong_password_deletes_the_created_windows_profile_and_returns()
    {
        _wifi.Failing["Cafe"] = WlanReason.PskMismatchSuspected;

        SwitchResult result = await SwitchToCafeAsync();

        Assert.Equal([Disconnect, Ipv4, Create, ConnectCafe, Ipv4Rollback, Delete, Return], result.Steps.Select(step => step.Title));
        Assert.Equal([ConnectCafe], result.Steps.Where(step => !step.Ok).Select(step => step.Title));
        Assert.DoesNotContain(_wifi.WindowsProfiles, profile => profile.Name == "Cafe");
        Assert.True(result.PasswordRejected);
        Assert.Null(_applier.Last);
    }

    [Fact]
    public async Task No_word_from_windows_deletes_the_created_windows_profile_too()
    {
        _wifi.Silent.Add("Cafe");

        SwitchResult result = await SwitchToCafeAsync();

        Assert.Equal(StepOutcome.Done, Step(result, Delete).Outcome);
        Assert.DoesNotContain(_wifi.WindowsProfiles, profile => profile.Name == "Cafe");
        Assert.False(result.PasswordRejected);
    }

    [Fact]
    public async Task Existing_windows_profile_with_the_same_name_is_not_overwritten_and_not_deleted()
    {
        _wifi.AddWindowsProfile(WindowsProfileXml.Build("Cafe", NewNetworkSecurity.Wpa2Personal, "working-1", autoConnect: true));

        SwitchResult result = await SwitchToCafeAsync();

        Assert.Equal([Disconnect, Ipv4, Create, Ipv4Rollback, Return], result.Steps.Select(step => step.Title));
        Assert.Equal(StepOutcome.Error, Step(result, Create).Outcome);
        Assert.Equal("working-1", KeyMaterial("Cafe"));
        Assert.Equal(["disconnect", "save Cafe", "connect Home"], _wifi.Calls);
    }

    [Fact]
    public async Task Failed_switch_to_a_known_network_keeps_its_windows_profile()
    {
        _wifi.Failing["Office"] = WlanReason.PskMismatchSuspected;

        SwitchResult result = await _applier.SwitchAsync(_adapter, "Office", "Office", Profile.Automatic, null, CancellationToken.None);

        Assert.DoesNotContain(_wifi.Calls, call => call.StartsWith("delete"));
        Assert.Contains(_wifi.WindowsProfiles, profile => profile.Name == "Office");
        Assert.True(result.PasswordRejected);
    }

    [Fact]
    public async Task Other_reasons_of_failure_are_not_a_rejected_password()
    {
        _wifi.Failing["Office"] = WlanReason.NoVisibleAccessPoint;

        SwitchResult result = await _applier.SwitchAsync(_adapter, "Office", "Office", Profile.Automatic, null, CancellationToken.None);

        Assert.False(result.PasswordRejected);
    }

    [Fact]
    public async Task Reentered_password_overwrites_the_existing_windows_profile_and_switches()
    {
        SwitchResult result = await ReenterOfficeAsync();

        Assert.Equal([Disconnect, Ipv4, NewPassword, ConnectOffice], result.Steps.Take(4).Select(step => step.Title));
        Assert.All(result.Steps, step => Assert.True(step.Ok));
        Assert.Equal(["disconnect", "save Office", "connect Office"], _wifi.Calls);
        Assert.Equal("new-office", KeyMaterial("Office"));
        // Режим автоподключения остался прежним
        Assert.Contains("<connectionMode>manual</connectionMode>", _wifi.Xml["Office"]);
    }

    [Fact]
    public async Task Reentered_password_that_fails_too_is_kept_with_its_windows_profile()
    {
        _wifi.Failing["Office"] = WlanReason.PskMismatchSuspected;

        SwitchResult result = await ReenterOfficeAsync();

        Assert.Equal([Disconnect, Ipv4, NewPassword, ConnectOffice, Ipv4Rollback, Return], result.Steps.Select(step => step.Title));
        Assert.Contains(_wifi.WindowsProfiles, profile => profile.Name == "Office");
        Assert.True(result.PasswordRejected);
    }

    [Fact]
    public async Task Windows_profile_that_cannot_take_the_password_means_no_connecting()
    {
        _wifi.AddWindowsProfile(WindowsProfileXml.Build("Office", NewNetworkSecurity.Open, "", autoConnect: true));

        SwitchResult result = await ReenterOfficeAsync();

        Assert.Equal("В профиле сети Windows «Office» нет пароля, который можно заменить.", Step(result, NewPassword).Error);
        Assert.Equal(["disconnect", "connect Home"], _wifi.Calls);
    }
}
