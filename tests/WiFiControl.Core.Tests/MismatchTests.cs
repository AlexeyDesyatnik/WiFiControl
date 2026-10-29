using WiFiControl.Core.Model;

namespace WiFiControl.Core.Tests;

/// <summary>Правила расхождения: что сравнивается с ожидаемым профилем и какой профиль ожидается.</summary>
public class MismatchTests
{
    private static ProxyState NoProxy() => new(new SystemProxyState(), new ProxySettings());

    private static (ProfileSet Profiles, Profile Office, Profile Home) BoundOffice()
    {
        var profiles = new ProfileSet();
        var office = new Profile { Name = "Офис" };
        var home = new Profile { Name = "Дом" };
        Assert.Empty(profiles.Add(office));
        Assert.Empty(profiles.Add(home));
        profiles.Bind("Office", office.Id);
        return (profiles, office, home);
    }

    [Fact]
    public void Without_last_application_the_expected_profile_is_the_current_networks()
    {
        (ProfileSet profiles, Profile office, _) = BoundOffice();

        Assert.Equal(office.Id, Mismatch.ExpectedProfile(null, "Office", profiles).Id);
        Assert.Equal(Profile.AutomaticId, Mismatch.ExpectedProfile(null, "Cafe", profiles).Id);
    }

    [Fact]
    public void Last_application_in_the_same_network_gives_the_expected_profile()
    {
        // Например, «Применить профиль…» другого профиля к текущей сети
        (ProfileSet profiles, _, Profile home) = BoundOffice();

        Assert.Equal(home.Id, Mismatch.ExpectedProfile(new LastApplication("Office", home), "Office", profiles).Id);
    }

    [Fact]
    public void Last_application_in_another_network_is_ignored()
    {
        (ProfileSet profiles, Profile office, Profile home) = BoundOffice();

        Assert.Equal(office.Id, Mismatch.ExpectedProfile(new LastApplication("Home", home), "Office", profiles).Id);
        // Ключи сетей сравниваются с учётом регистра (ADR 0007)
        Assert.Equal(office.Id, Mismatch.ExpectedProfile(new LastApplication("office", home), "Office", profiles).Id);
    }

    [Fact]
    public void Adapter_on_dhcp_matches_automatic_profile()
    {
        Assert.Empty(Mismatch.Find(Profile.Automatic, new Ipv4Settings(), NoProxy()));
    }

    [Fact]
    public void Static_address_on_adapter_differs_from_automatic_profile_in_address_and_dns()
    {
        Ipv4Settings configured = FakeSystem.Static("10.68.12.37", "10.68.12.1", "10.0.68.10");

        Assert.Equal([Difference.Address, Difference.Dns], Mismatch.Find(Profile.Automatic, configured, NoProxy()));
    }

    [Fact]
    public void Static_address_written_differently_matches_profile()
    {
        var profile = new Profile { Name = "Офис", Ipv4 = FakeSystem.Static("10.68.12.37", "10.68.12.1", "10.0.68.10") };
        Ipv4Settings configured = FakeSystem.Static("010.068.012.037", "10.68.12.1", "10.0.68.10");

        Assert.Empty(Mismatch.Find(profile, configured, NoProxy()));
    }

    [Fact]
    public void Manual_dns_with_automatic_address_differs_only_in_dns()
    {
        var configured = new Ipv4Settings { AutoDns = false };
        configured.Dns.Add("1.1.1.1");

        Assert.Equal([Difference.Dns], Mismatch.Find(Profile.Automatic, configured, NoProxy()));
    }

    [Fact]
    public void Server_and_exceptions_left_in_disabled_system_proxy_are_not_a_mismatch_with_disabled_proxy()
    {
        // Так выключенный прокси оставляет сам инструмент: человек включит его галочкой в Windows
        var left = ProxySettings.FromRaw(false, "10.0.68.52:3128", "*.corp;<local>");
        var proxy = new ProxyState(new SystemProxyState { Settings = left }, new ProxySettings());

        Assert.Empty(Mismatch.Find(Profile.Automatic, new Ipv4Settings(), proxy));
    }

    [Fact]
    public void Proxy_is_compared_in_both_system_and_winhttp()
    {
        var profile = new Profile { Name = "Офис", Proxy = ProxySettings.FromRaw(true, "10.0.68.52:3128", "<local>") };
        var system = new SystemProxyState { Settings = ProxySettings.FromRaw(true, "10.0.68.52:3128", "<local>") };

        Assert.Equal([Difference.WinHttpProxy],
            Mismatch.Find(profile, new Ipv4Settings(), new ProxyState(system, new ProxySettings())));
        Assert.Equal([Difference.SystemProxy],
            Mismatch.Find(profile, new Ipv4Settings(), new ProxyState(new SystemProxyState(), profile.Proxy.Clone())));
    }

    [Fact]
    public void Enabled_proxy_differs_from_profile_with_disabled_proxy()
    {
        var enabled = ProxySettings.FromRaw(true, "10.0.68.52:3128", "");
        var proxy = new ProxyState(new SystemProxyState { Settings = enabled }, enabled.Clone());

        Assert.Equal([Difference.SystemProxy, Difference.WinHttpProxy], Mismatch.Find(Profile.Automatic, new Ipv4Settings(), proxy));
    }

    [Fact]
    public void Exceptions_differing_in_enabled_proxy_are_a_mismatch()
    {
        var profile = new Profile { Name = "Офис", Proxy = ProxySettings.FromRaw(true, "10.0.68.52:3128", "") };
        profile.Proxy.PrivateNetworksDirect = true;
        var proxy = new ProxyState(new SystemProxyState { Settings = ProxySettings.FromRaw(true, "10.0.68.52:3128", "10.*") },
            ProxySettings.FromRaw(true, "10.0.68.52:3128", profile.Proxy.ToBypassList()));

        Assert.Equal([Difference.SystemProxy], Mismatch.Find(profile, new Ipv4Settings(), proxy));
    }

    [Fact]
    public void Enabled_auto_detect_and_script_are_a_mismatch()
    {
        // Профиль их всегда выключает (ADR 0003)
        var system = new SystemProxyState { AutoDetect = true, AutoConfigUrl = "http://wpad/wpad.dat" };

        Assert.Equal([Difference.ProxyAutoDetect, Difference.ProxyScript],
            Mismatch.Find(Profile.Automatic, new Ipv4Settings(), new ProxyState(system, new ProxySettings())));
    }
}
