using WiFiControl.Core.Model;

namespace WiFiControl.Core.Tests;

public class ProfileRulesTests
{
    private static Ipv4Settings Static(string address, string gateway = "10.68.12.1", params string[] dns)
    {
        var settings = new Ipv4Settings { AutoAddress = false, Address = address, Mask = "255.255.255.0", Gateway = gateway, AutoDns = false };
        settings.Dns.AddRange(dns);
        return settings;
    }

    private static ProxySettings Proxy(string server)
    {
        return new ProxySettings { Enabled = true, Server = server };
    }

    private static Profile Valid()
    {
        return new Profile
        {
            Name = "Подсеть 10.68.12.0/24",
            Ipv4 = Static("10.68.12.37", "10.68.12.1", "95.167.167.95", "95.167.167.96"),
            Proxy = new ProxySettings { Enabled = true, Server = "10.0.68.52:3128", BypassLocal = true }
        };
    }

    [Fact]
    public void Complete_profile_passes()
    {
        Assert.Empty(ProfileRules.Check(Valid()));
    }

    [Fact]
    public void Profile_needs_a_name()
    {
        Profile profile = Valid();
        profile.Name = "   ";

        Assert.Contains("Укажите название профиля.", ProfileRules.Check(profile));
    }

    [Fact]
    public void Name_is_trimmed()
    {
        Profile profile = Valid();
        profile.Name = "  Офис  ";

        ProfileRules.Check(profile);

        Assert.Equal("Офис", profile.Name);
    }

    [Theory]
    [InlineData("/24", "255.255.255.0")]
    [InlineData("24", "255.255.255.0")]
    [InlineData(" 255.255.252.0 ", "255.255.252.0")]
    [InlineData("/ 16", "255.255.0.0")]
    public void Mask_is_accepted_in_either_notation_and_stored_dotted(string entered, string stored)
    {
        Ipv4Settings settings = Static("10.68.12.37");
        settings.Mask = entered;

        Assert.Empty(ProfileRules.CheckIpv4(settings));
        Assert.Equal(stored, settings.Mask);
    }

    [Theory]
    [InlineData("")]
    [InlineData("255.0.255.0")]
    [InlineData("/33")]
    [InlineData("/0")]
    [InlineData("abc")]
    public void Bad_mask_is_rejected(string mask)
    {
        Ipv4Settings settings = Static("10.68.12.37");
        settings.Mask = mask;

        Assert.Contains(ProfileRules.CheckIpv4(settings), error => error.StartsWith("Маска задана неверно", StringComparison.Ordinal));
    }

    [Fact]
    public void Leading_zeros_are_removed_so_windows_does_not_read_them_as_octal()
    {
        Ipv4Settings settings = Static("010.068.012.037", "010.068.012.001", "010.000.068.010");

        Assert.Empty(ProfileRules.CheckIpv4(settings));
        Assert.Equal("10.68.12.37", settings.Address);
        Assert.Equal("10.68.12.1", settings.Gateway);
        Assert.Equal(["10.0.68.10"], settings.Dns);
    }

    [Fact]
    public void Gateway_is_optional_and_manual_dns_may_be_empty_with_manual_address()
    {
        Assert.Empty(ProfileRules.CheckIpv4(Static("10.68.12.37", "")));
    }

    [Fact]
    public void Bad_gateway_is_rejected()
    {
        Assert.Contains(ProfileRules.CheckIpv4(Static("10.68.12.37", "10.68.12")), error => error.StartsWith("Шлюз задан неверно", StringComparison.Ordinal));
    }

    [Fact]
    public void Manual_address_forces_manual_dns()
    {
        Ipv4Settings settings = Static("10.68.12.37");
        settings.AutoDns = true;

        ProfileRules.CheckIpv4(settings);

        Assert.False(settings.AutoDns);
    }

    [Fact]
    public void Automatic_address_drops_leftover_manual_values()
    {
        Ipv4Settings settings = Static("10.68.12.37", "10.68.12.1", "8.8.8.8");
        settings.AutoAddress = true;
        settings.AutoDns = true;

        Assert.Empty(ProfileRules.CheckIpv4(settings));
        Assert.Equal("", settings.Address);
        Assert.Equal("", settings.Mask);
        Assert.Equal("", settings.Gateway);
        Assert.Empty(settings.Dns);
    }

    [Fact]
    public void Automatic_address_with_manual_dns_needs_a_server()
    {
        var settings = new Ipv4Settings { AutoAddress = true, AutoDns = false };

        Assert.Contains("Укажите DNS-сервер или выберите автоматическое получение DNS.", ProfileRules.CheckIpv4(settings));
    }

    [Fact]
    public void Blank_dns_entries_are_dropped()
    {
        var settings = new Ipv4Settings { AutoAddress = true, AutoDns = false, Dns = { " ", "8.8.8.8", "" } };

        Assert.Empty(ProfileRules.CheckIpv4(settings));
        Assert.Equal(["8.8.8.8"], settings.Dns);
    }

    [Fact]
    public void Bad_dns_server_is_named_in_the_message()
    {
        Ipv4Settings settings = Static("10.68.12.37", "", " 8.8.8 ");

        Assert.Contains("DNS-сервер «8.8.8» задан неверно. Пример: 10.0.68.10", ProfileRules.CheckIpv4(settings));
    }

    [Fact]
    public void No_more_than_two_dns_servers()
    {
        Ipv4Settings settings = Static("10.68.12.37", "", "1.1.1.1", "8.8.8.8", "9.9.9.9");

        Assert.Contains("DNS-серверов может быть не больше двух.", ProfileRules.CheckIpv4(settings));
    }

    [Theory]
    [InlineData("10.68.12")]
    [InlineData("10.68.12.")]
    [InlineData("10.68.12.300")]
    [InlineData("")]
    public void Manual_address_must_be_complete(string address)
    {
        Assert.Contains(ProfileRules.CheckIpv4(Static(address)), error => error.StartsWith("Адрес IPv4 задан неверно", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("10.0.68.52:3128")]
    [InlineData("proxy.corp.local:8080")]
    [InlineData("http://127.0.0.1:10809")]
    public void Proxy_server_with_port_is_accepted(string server)
    {
        Assert.Empty(ProfileRules.CheckProxy(Proxy(server)));
    }

    [Theory]
    [InlineData("", "Укажите прокси-сервер")]
    [InlineData("10.0.68.52", "Укажите порт")]
    [InlineData("10.0.68.52:", "Укажите порт")]
    [InlineData("10.0.68.52:0", "Укажите порт")]
    [InlineData("10.0.68.52:70000", "Укажите порт")]
    [InlineData("10.0.68 .52:3128", "В адресе прокси-сервера не должно быть пробелов")]
    public void Enabled_proxy_needs_server_and_port(string server, string expected)
    {
        Assert.Contains(ProfileRules.CheckProxy(Proxy(server)), error => error.StartsWith(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Proxy_server_is_trimmed()
    {
        ProxySettings settings = Proxy("  10.0.68.52:3128  ");

        Assert.Empty(ProfileRules.CheckProxy(settings));
        Assert.Equal("10.0.68.52:3128", settings.Server);
    }

    [Fact]
    public void Disabled_proxy_needs_nothing()
    {
        Assert.Empty(ProfileRules.CheckProxy(new ProxySettings()));
    }

    [Fact]
    public void Local_and_private_network_flags_stay_flags_after_the_check()
    {
        ProxySettings settings = Proxy("10.0.68.52:3128");
        settings.BypassLocal = true;
        settings.PrivateNetworksDirect = true;
        settings.Exceptions.Add("*.corp.local");

        Assert.Empty(ProfileRules.CheckProxy(settings));
        Assert.True(settings.BypassLocal);
        Assert.True(settings.PrivateNetworksDirect);
        Assert.Equal(["*.corp.local"], settings.Exceptions);
    }

    [Fact]
    public void Proxy_errors_reach_the_profile_check()
    {
        Profile profile = Valid();
        profile.Proxy.Server = "10.0.68.52";

        Assert.Contains(ProfileRules.Check(profile), error => error.StartsWith("Укажите порт", StringComparison.Ordinal));
    }

    [Fact]
    public void Ipv4_errors_reach_the_profile_check()
    {
        Profile profile = Valid();
        profile.Ipv4.Address = "10.68.12";

        Assert.Contains(ProfileRules.Check(profile), error => error.StartsWith("Адрес IPv4 задан неверно", StringComparison.Ordinal));
    }
}
