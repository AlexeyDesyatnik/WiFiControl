using WiFiControl.Core.Model;

namespace WiFiControl.Core.Tests;

public class ProfileTests
{
    [Fact]
    public void Capturing_a_profile_keeps_dhcp_automatic_and_uses_the_network_name()
    {
        var adapter = new Adapter();
        adapter.Active.Addresses.Add("192.168.1.20/24");
        adapter.Active.Gateways.Add("192.168.1.1");
        adapter.Active.Dns.Add("192.168.1.1");

        CapturedProfile captured = CapturedProfile.FromSettings("Домашняя сеть", adapter, new SystemProxyState());

        Assert.Equal("Домашняя сеть", captured.Profile.Name);
        Assert.False(captured.Profile.IsAutomatic);
        Assert.True(captured.Profile.Ipv4.AutoAddress);
        Assert.True(captured.Profile.Ipv4.AutoDns);
        Assert.Empty(captured.Profile.Ipv4.Address);
        Assert.Empty(captured.Profile.Ipv4.Mask);
        Assert.Empty(captured.Profile.Ipv4.Gateway);
        Assert.Empty(captured.Profile.Ipv4.Dns);
    }

    [Fact]
    public void Capturing_a_profile_copies_manual_ipv4_settings_from_the_adapter()
    {
        var adapter = new Adapter
        {
            Configured = new Ipv4Settings
            {
                AutoAddress = false,
                Address = "10.68.12.37",
                Mask = "255.255.255.0",
                Gateway = "10.68.12.1",
                AutoDns = false,
            },
        };
        adapter.Configured.Dns.AddRange(["10.68.0.10", "10.68.0.11"]);
        adapter.Active.Addresses.Add("169.254.1.1/16");

        Profile profile = CapturedProfile.FromSettings("Офис", adapter, new SystemProxyState()).Profile;
        adapter.Configured.Dns.Clear();

        Assert.False(profile.Ipv4.AutoAddress);
        Assert.Equal("10.68.12.37", profile.Ipv4.Address);
        Assert.Equal("255.255.255.0", profile.Ipv4.Mask);
        Assert.Equal("10.68.12.1", profile.Ipv4.Gateway);
        Assert.False(profile.Ipv4.AutoDns);
        Assert.Equal(["10.68.0.10", "10.68.0.11"], profile.Ipv4.Dns);
    }

    [Fact]
    public void Capturing_a_profile_keeps_manual_dns_with_an_automatic_address()
    {
        var adapter = new Adapter { Configured = new Ipv4Settings { AutoDns = false } };
        adapter.Configured.Dns.Add("10.68.0.10");
        adapter.Active.Dns.Add("10.68.0.11");

        Profile profile = CapturedProfile.FromSettings("Офис", adapter, new SystemProxyState()).Profile;

        Assert.True(profile.Ipv4.AutoAddress);
        Assert.False(profile.Ipv4.AutoDns);
        Assert.Equal(["10.68.0.10"], profile.Ipv4.Dns);
    }

    [Theory]
    [InlineData(false, "", false, false)]
    [InlineData(true, "", true, false)]
    [InlineData(false, "https://example.test/proxy.pac", false, true)]
    [InlineData(true, "https://example.test/proxy.pac", true, true)]
    public void Capturing_a_profile_copies_manual_proxy_and_reports_automatic_settings(
        bool autoDetect, string pacUrl, bool expectedAutoDetect, bool expectedScript)
    {
        var proxy = new SystemProxyState
        {
            Settings = new ProxySettings
            {
                Enabled = true,
                Server = "proxy.example.test:3128",
                BypassLocal = true,
                PrivateNetworksDirect = true,
            },
            AutoDetect = autoDetect,
            AutoConfigUrl = pacUrl,
        };
        proxy.Settings.Exceptions.Add("*.corp.example.test");

        CapturedProfile captured = CapturedProfile.FromSettings("Офис", new Adapter(), proxy);
        proxy.Settings.Exceptions.Clear();

        Assert.True(captured.Profile.Proxy.Enabled);
        Assert.Equal("proxy.example.test:3128", captured.Profile.Proxy.Server);
        Assert.True(captured.Profile.Proxy.BypassLocal);
        Assert.True(captured.Profile.Proxy.PrivateNetworksDirect);
        Assert.Equal(["*.corp.example.test"], captured.Profile.Proxy.Exceptions);
        Assert.Equal(expectedAutoDetect, captured.HasProxyAutoDetect);
        Assert.Equal(expectedScript, captured.HasProxyScript);
    }

    [Fact]
    public void Automatic_profile_has_a_known_id_and_dhcp_without_proxy()
    {
        Profile automatic = Profile.Automatic;

        Assert.Equal(new Guid("00000000-0000-0000-0000-000000000001"), automatic.Id);
        Assert.Equal(Profile.AutomaticId, automatic.Id);
        Assert.Equal("Автоматически", automatic.Name);
        Assert.True(automatic.Ipv4.AutoAddress);
        Assert.True(automatic.Ipv4.AutoDns);
        Assert.False(automatic.Proxy.Enabled);
        Assert.True(automatic.IsAutomatic);
    }

    [Fact]
    public void Automatic_profile_passes_the_rules()
    {
        Assert.Empty(ProfileRules.Check(Profile.Automatic));
    }

    [Fact]
    public void Changing_a_copy_of_automatic_profile_does_not_change_the_built_in_one()
    {
        Profile copy = Profile.Automatic;
        copy.Name = "Дом";
        copy.Proxy.Enabled = true;

        Assert.Equal("Автоматически", Profile.Automatic.Name);
        Assert.False(Profile.Automatic.Proxy.Enabled);
    }

    [Fact]
    public void New_profiles_get_their_own_ids()
    {
        var one = new Profile();
        var other = new Profile();

        Assert.NotEqual(one.Id, other.Id);
        Assert.NotEqual(Guid.Empty, one.Id);
        Assert.False(one.IsAutomatic);
    }

    [Fact]
    public void Clone_keeps_the_id_and_is_independent()
    {
        var original = new Profile { Name = "Офис" };
        original.Ipv4.Dns.Add("8.8.8.8");
        original.Proxy.Exceptions.Add("*.corp.local");

        Profile copy = original.Clone();
        copy.Name = "Дом";
        copy.Ipv4.Dns.Add("1.1.1.1");
        copy.Proxy.Exceptions.Clear();

        Assert.Equal(original.Id, copy.Id);
        Assert.Equal("Офис", original.Name);
        Assert.Single(original.Ipv4.Dns);
        Assert.Single(original.Proxy.Exceptions);
    }
}
