using WiFiControl.Core.Model;

namespace WiFiControl.Core.Tests;

public class ProfileTests
{
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
