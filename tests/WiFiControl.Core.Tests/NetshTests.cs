using WiFiControl.Core.Model;
using WiFiControl.Core.Windows;

namespace WiFiControl.Core.Tests;

// Перенесено из NetSwitch

public class NetshTests
{
    private const string Name = "name=\"Беспроводная сеть\"";

    private static IReadOnlyList<string> Commands(Ipv4Settings current, Ipv4Settings target)
    {
        return Netsh.Ipv4Commands("Беспроводная сеть", current, target);
    }

    [Fact]
    public void Nothing_to_do_when_settings_already_match()
    {
        Assert.Empty(Commands(new Ipv4Settings(), new Ipv4Settings()));
        Assert.Empty(Commands(FakeSystem.Static("10.68.12.37", "10.68.12.1", "8.8.8.8"), FakeSystem.Static("10.68.12.37", "10.68.12.1", "8.8.8.8")));
    }

    [Fact]
    public void From_automatic_to_manual_with_two_dns()
    {
        IReadOnlyList<string> commands = Commands(new Ipv4Settings(), FakeSystem.Static("10.68.12.37", "10.68.12.1", "95.167.167.95", "95.167.167.96"));

        Assert.Equal(
        [
            "interface ipv4 set address " + Name + " source=static address=10.68.12.37 mask=255.255.255.0 gateway=10.68.12.1",
            "interface ipv4 set dnsservers " + Name + " source=static address=95.167.167.95 validate=no",
            "interface ipv4 add dnsservers " + Name + " address=95.167.167.96 index=2 validate=no"
        ], commands);
    }

    [Fact]
    public void Recorded_manual_addresses_are_deleted_together_with_gateways()
    {
        var values = new TcpipInterfaceValues { IpAddress = ["0.0.0.0", "192.168.10.50", "192.168.11.50"] };

        IReadOnlyList<string> commands = Netsh.DeleteAddressCommands("Беспроводная сеть", values);

        Assert.Equal(
        [
            "interface ipv4 delete address " + Name + " address=192.168.10.50 gateway=all",
            "interface ipv4 delete address " + Name + " address=192.168.11.50"
        ], commands);
    }

    [Fact]
    public void From_manual_to_fully_automatic_resets_both_address_and_dns()
    {
        IReadOnlyList<string> commands = Commands(FakeSystem.Static("10.68.12.37", "10.68.12.1", "8.8.8.8"), new Ipv4Settings());

        Assert.Equal(
        [
            "interface ipv4 set address " + Name + " source=dhcp",
            "interface ipv4 set dnsservers " + Name + " source=dhcp"
        ], commands);
    }

    [Fact]
    public void Manual_address_without_gateway_and_dns()
    {
        IReadOnlyList<string> commands = Commands(new Ipv4Settings(), FakeSystem.Static("10.68.12.37", ""));

        Assert.Equal(
        [
            "interface ipv4 set address " + Name + " source=static address=10.68.12.37 mask=255.255.255.0 gateway=none",
            "interface ipv4 set dnsservers " + Name + " source=static address=none validate=no"
        ], commands);
    }

    [Fact]
    public void Changing_gateway_goes_through_automatic_to_clear_the_old_one()
    {
        IReadOnlyList<string> commands = Commands(FakeSystem.Static("10.68.12.37", "10.68.12.1"), FakeSystem.Static("10.68.12.37", "10.68.12.254"));

        Assert.Equal(
        [
            "interface ipv4 set address " + Name + " source=dhcp",
            "interface ipv4 set address " + Name + " source=static address=10.68.12.37 mask=255.255.255.0 gateway=10.68.12.254"
        ], commands);
    }

    [Fact]
    public void Changing_only_the_address_keeps_gateway_and_dns_untouched()
    {
        IReadOnlyList<string> commands = Commands(FakeSystem.Static("10.68.12.37", "10.68.12.1", "8.8.8.8"), FakeSystem.Static("10.68.12.99", "10.68.12.1", "8.8.8.8"));

        Assert.Equal(["interface ipv4 set address " + Name + " source=static address=10.68.12.99 mask=255.255.255.0 gateway=10.68.12.1"], commands);
    }

    [Fact]
    public void Changing_only_dns_leaves_address_alone()
    {
        var current = new Ipv4Settings();
        var target = new Ipv4Settings { AutoDns = false, Dns = { "8.8.8.8" } };

        Assert.Equal(["interface ipv4 set dnsservers " + Name + " source=static address=8.8.8.8 validate=no"], Commands(current, target));
        Assert.Equal(["interface ipv4 set dnsservers " + Name + " source=dhcp"], Commands(target, current));
    }

    [Fact]
    public void Automatic_address_with_manual_dns_kept_when_only_address_mode_changes()
    {
        Ipv4Settings current = FakeSystem.Static("10.68.12.37", "10.68.12.1", "8.8.8.8");
        var target = new Ipv4Settings { AutoDns = false, Dns = { "8.8.8.8" } };

        Assert.Equal(["interface ipv4 set address " + Name + " source=dhcp"], Commands(current, target));
    }

    [Fact]
    public void Default_route_is_added_to_active_settings_only()
    {
        Assert.Equal("interface ipv4 add route prefix=0.0.0.0/0 interface=\"Беспроводная сеть\" nexthop=10.68.12.1 store=active",
            Netsh.AddDefaultRouteCommand("Беспроводная сеть", "10.68.12.1"));
    }
}
