using WiFiControl.Core.Model;

namespace WiFiControl.Core.Tests;

public class ProxySettingsTests
{
    private const string PrivateNetworks = "10.*;192.168.*;172.16.*;172.17.*;172.18.*;172.19.*;172.20.*;172.21.*;172.22.*;172.23.*;"
                                           + "172.24.*;172.25.*;172.26.*;172.27.*;172.28.*;172.29.*;172.30.*;172.31.*";

    [Fact]
    public void Splits_bypass_list_into_exceptions_and_local_flag()
    {
        ProxySettings settings = ProxySettings.FromRaw(true, " 10.0.68.52:3128 ", "*.corp.local; 10.*;<LOCAL>");

        Assert.True(settings.Enabled);
        Assert.Equal("10.0.68.52:3128", settings.Server);
        Assert.Equal(["*.corp.local", "10.*"], settings.Exceptions);
        Assert.True(settings.BypassLocal);
        Assert.False(settings.PrivateNetworksDirect);
    }

    [Fact]
    public void Missing_values_give_disabled_proxy_without_exceptions()
    {
        ProxySettings settings = ProxySettings.FromRaw(false, null, null);

        Assert.False(settings.Enabled);
        Assert.Equal("", settings.Server);
        Assert.Empty(settings.Exceptions);
        Assert.False(settings.BypassLocal);
        Assert.False(settings.PrivateNetworksDirect);
    }

    [Fact]
    public void Bypass_list_survives_a_round_trip()
    {
        ProxySettings settings = ProxySettings.FromRaw(true, "proxy:3128", "*.corp.local;10.*;<local>");

        Assert.Equal("*.corp.local;10.*;<local>", settings.ToBypassList());
    }

    [Fact]
    public void Full_set_of_private_networks_reads_as_the_flag()
    {
        // Порядок и регистр — как получится у человека, правившего исключения в Windows
        ProxySettings settings = ProxySettings.FromRaw(true, "proxy:3128", "*.corp.local;" + PrivateNetworks.ToUpperInvariant().Replace("10.*;", "") + ";10.*;<local>");

        Assert.True(settings.PrivateNetworksDirect);
        Assert.Equal(["*.corp.local"], settings.Exceptions);
        Assert.True(settings.BypassLocal);
    }

    [Fact]
    public void Incomplete_set_of_private_networks_stays_ordinary_exceptions()
    {
        string withoutOne = PrivateNetworks.Replace(";172.31.*", "");

        ProxySettings settings = ProxySettings.FromRaw(true, "proxy:3128", withoutOne);

        Assert.False(settings.PrivateNetworksDirect);
        Assert.Equal(withoutOne.Split(';'), settings.Exceptions);
    }

    [Fact]
    public void Private_networks_flag_is_written_as_the_full_set_of_exceptions()
    {
        var settings = new ProxySettings { Enabled = true, Server = "proxy:3128", BypassLocal = true, PrivateNetworksDirect = true };
        settings.Exceptions.Add("*.corp.local");

        Assert.Equal("*.corp.local;" + PrivateNetworks + ";<local>", settings.ToBypassList());
    }

    [Fact]
    public void Private_network_already_among_exceptions_is_not_written_twice()
    {
        var settings = new ProxySettings { Enabled = true, Server = "proxy:3128", PrivateNetworksDirect = true };
        settings.Exceptions.Add("10.*");

        Assert.Equal("10.*;" + PrivateNetworks.Replace("10.*;", ""), settings.ToBypassList());
    }

    [Fact]
    public void Written_flag_reads_back_as_the_flag()
    {
        var settings = new ProxySettings { Enabled = true, Server = "proxy:3128", PrivateNetworksDirect = true };

        ProxySettings read = ProxySettings.FromRaw(true, "proxy:3128", settings.ToBypassList());

        Assert.True(read.PrivateNetworksDirect);
        Assert.Empty(read.Exceptions);
    }

    [Fact]
    public void Disabled_proxies_are_the_same_whatever_is_left_in_their_fields()
    {
        ProxySettings one = ProxySettings.FromRaw(false, "old:8080", "a;b");
        ProxySettings other = ProxySettings.FromRaw(false, "", "");

        Assert.True(one.SameAs(other));
    }

    [Fact]
    public void Enabled_proxies_are_compared_ignoring_case_and_exception_order()
    {
        ProxySettings one = ProxySettings.FromRaw(true, "Proxy:3128", "a.local;B.local;<local>");
        ProxySettings other = ProxySettings.FromRaw(true, "proxy:3128", "<local>;b.local;A.LOCAL");

        Assert.True(one.SameAs(other));
    }

    [Theory]
    [InlineData(true, "proxy:3128", "", false, "proxy:3128", "")]
    [InlineData(true, "proxy:3128", "", true, "proxy:8080", "")]
    [InlineData(true, "proxy:3128", "a", true, "proxy:3128", "a;b")]
    [InlineData(true, "proxy:3128", "", true, "proxy:3128", "<local>")]
    [InlineData(true, "proxy:3128", "", true, "proxy:3128", PrivateNetworks)]
    public void Different_proxies_are_not_the_same(bool on1, string server1, string bypass1, bool on2, string server2, string bypass2)
    {
        Assert.False(ProxySettings.FromRaw(on1, server1, bypass1).SameAs(ProxySettings.FromRaw(on2, server2, bypass2)));
    }
}
