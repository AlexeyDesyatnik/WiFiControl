using WiFiControl.Core.Model;

namespace WiFiControl.Core.Tests;

public class ProfileSetTests
{
    private static Profile Named(string name)
    {
        return new Profile { Name = name };
    }

    private static Profile Added(ProfileSet set, string name)
    {
        Profile profile = Named(name);
        Assert.Empty(set.Add(profile));
        return profile;
    }

    [Fact]
    public void New_set_has_no_profiles_and_automatic_as_default()
    {
        var set = new ProfileSet();

        Assert.Empty(set.Profiles);
        Assert.Empty(set.Bindings);
        Assert.Equal(Profile.AutomaticId, set.DefaultProfileId);
        Assert.Equal(Profile.AutomaticId, set.ProfileFor("Дом").Id);
    }

    [Fact]
    public void Network_without_binding_gets_the_default_profile()
    {
        var set = new ProfileSet();
        Profile office = Added(set, "Офис");

        set.DefaultProfileId = office.Id;

        Assert.Equal(office.Id, set.ProfileFor("Кафе").Id);
        Assert.Equal("Офис", set.ProfileFor("Кафе").Name);
    }

    [Fact]
    public void Bound_network_gets_its_profile_instead_of_the_default()
    {
        var set = new ProfileSet();
        Profile office = Added(set, "Офис");

        set.Bind("Office", office.Id);

        Assert.Equal(office.Id, set.ProfileFor("Office").Id);
        Assert.Equal(Profile.AutomaticId, set.ProfileFor("office").Id);
        Assert.Equal(office.Id, set.Bindings["Office"]);
    }

    [Fact]
    public void Name_typed_by_hand_matches_the_key_read_from_wifi()
    {
        var set = new ProfileSet();
        Profile office = Added(set, "Офис");

        set.Bind("Офис 5G", office.Id);

        Assert.Equal(office.Id, set.ProfileFor(NetworkKey.FromBytes("Офис 5G"u8)).Id);
    }

    [Fact]
    public void Network_explicitly_bound_to_the_old_default_stays_on_it_after_the_default_changes()
    {
        var set = new ProfileSet();
        Profile home = Added(set, "Дом");
        Profile office = Added(set, "Офис");
        set.DefaultProfileId = home.Id;
        set.Bind("Дом", home.Id);

        set.DefaultProfileId = office.Id;

        Assert.Equal(home.Id, set.ProfileFor("Дом").Id);
        Assert.Equal(office.Id, set.ProfileFor("Кафе").Id);
    }

    [Fact]
    public void Binding_to_automatic_is_kept_when_the_default_changes()
    {
        var set = new ProfileSet();
        Profile office = Added(set, "Офис");
        set.Bind("Гостевая", Profile.AutomaticId);

        set.DefaultProfileId = office.Id;

        Assert.Equal(Profile.AutomaticId, set.ProfileFor("Гостевая").Id);
    }

    [Fact]
    public void Network_has_at_most_one_binding()
    {
        var set = new ProfileSet();
        Profile home = Added(set, "Дом");
        Profile office = Added(set, "Офис");

        set.Bind("Office", home.Id);
        set.Bind("Office", office.Id);

        Assert.Single(set.Bindings);
        Assert.Equal(office.Id, set.ProfileFor("Office").Id);
    }

    [Fact]
    public void Unbound_network_returns_to_the_default()
    {
        var set = new ProfileSet();
        Profile office = Added(set, "Офис");
        set.Bind("Office", office.Id);

        Assert.True(set.Unbind("Office"));

        Assert.Empty(set.Bindings);
        Assert.Equal(Profile.AutomaticId, set.ProfileFor("Office").Id);
        Assert.False(set.Unbind("Office"));
    }

    [Fact]
    public void Removing_a_profile_returns_its_networks_and_drops_their_bindings()
    {
        var set = new ProfileSet();
        Profile home = Added(set, "Дом");
        Profile office = Added(set, "Офис");
        set.DefaultProfileId = home.Id;
        set.Bind("Office", office.Id);
        set.Bind("Office 5G", office.Id);
        set.Bind("Дом", home.Id);

        IReadOnlyList<string> affected = set.Remove(office.Id);

        Assert.Equal(["Office", "Office 5G"], affected.Order(StringComparer.Ordinal));
        Assert.Equal(["Дом"], set.Bindings.Keys);
        Assert.Equal(home.Id, set.ProfileFor("Office").Id);
        Assert.Equal(["Дом"], set.Profiles.Select(profile => profile.Name));
        Assert.Null(set.Find(office.Id));
    }

    [Fact]
    public void Removing_an_unused_profile_affects_no_networks()
    {
        var set = new ProfileSet();
        Profile office = Added(set, "Офис");

        Assert.Empty(set.Remove(office.Id));
        Assert.Empty(set.Profiles);
    }

    [Fact]
    public void Removing_the_default_profile_makes_automatic_the_default()
    {
        var set = new ProfileSet();
        Profile office = Added(set, "Офис");
        set.DefaultProfileId = office.Id;

        set.Remove(office.Id);

        Assert.Equal(Profile.AutomaticId, set.DefaultProfileId);
        Assert.Equal(Profile.AutomaticId, set.ProfileFor("Кафе").Id);
    }

    [Fact]
    public void Automatic_profile_cannot_be_removed()
    {
        var set = new ProfileSet();
        set.Bind("Гостевая", Profile.AutomaticId);

        var error = Assert.Throws<InvalidOperationException>(() => set.Remove(Profile.AutomaticId));

        Assert.Equal("Профиль «Автоматически» встроенный, его нельзя удалить.", error.Message);
        Assert.Equal(Profile.AutomaticId, set.Bindings["Гостевая"]);
    }

    [Fact]
    public void Added_profile_is_checked_and_normalized_by_the_rules()
    {
        var set = new ProfileSet();
        var broken = new Profile { Name = "  " };
        var spaced = new Profile { Name = "  Офис  " };

        Assert.Contains("Укажите название профиля.", set.Add(broken));
        Assert.Empty(set.Add(spaced));

        Assert.Equal(["Офис"], set.Profiles.Select(profile => profile.Name));
    }

    [Theory]
    [InlineData("Офис")]
    [InlineData("ОФИС")]
    [InlineData(" офис ")]
    public void Profile_names_are_unique_ignoring_case(string name)
    {
        var set = new ProfileSet();
        Added(set, "Офис");

        List<string> errors = set.Add(Named(name));

        Assert.Equal(["Профиль с названием «" + name.Trim() + "» уже есть."], errors);
        Assert.Single(set.Profiles);
    }

    [Theory]
    [InlineData("Автоматически")]
    [InlineData("автоматически")]
    public void Name_of_automatic_profile_is_reserved(string name)
    {
        var set = new ProfileSet();

        List<string> errors = set.Add(Named(name));

        Assert.Equal(["Название «Автоматически» занято встроенным профилем."], errors);
        Assert.Empty(set.Profiles);
    }

    [Fact]
    public void Automatic_profile_cannot_be_added()
    {
        var set = new ProfileSet();
        Profile automatic = Profile.Automatic;
        automatic.Name = "Мой DHCP";

        Assert.Equal(["Профиль «Автоматически» встроенный, его нельзя добавить."], set.Add(automatic));
        Assert.Empty(set.Profiles);
    }

    [Fact]
    public void Renaming_and_editing_a_profile_keeps_its_bindings()
    {
        var set = new ProfileSet();
        Profile office = Added(set, "Офис");
        set.Bind("Office", office.Id);
        set.DefaultProfileId = office.Id;

        Profile edited = set.Find(office.Id)!;
        edited.Name = "Главный офис";
        edited.Proxy = new ProxySettings { Enabled = true, Server = "10.0.68.52:3128", PrivateNetworksDirect = true };

        Assert.Empty(set.Update(edited));

        Profile bound = set.ProfileFor("Office");
        Assert.Equal("Главный офис", bound.Name);
        Assert.True(bound.Proxy.PrivateNetworksDirect);
        Assert.Equal(office.Id, set.Bindings["Office"]);
        Assert.Equal(office.Id, set.DefaultProfileId);
    }

    [Fact]
    public void Profile_can_change_the_case_of_its_own_name()
    {
        var set = new ProfileSet();
        Profile office = Added(set, "офис");

        office.Name = "Офис";

        Assert.Empty(set.Update(office));
        Assert.Equal("Офис", set.Find(office.Id)!.Name);
    }

    [Fact]
    public void Rejected_edit_leaves_the_profile_as_it_was()
    {
        var set = new ProfileSet();
        Added(set, "Дом");
        Profile office = Added(set, "Офис");

        office.Name = "ДОМ";
        office.Proxy.Enabled = true;

        Assert.Equal(["Профиль с названием «ДОМ» уже есть.", "Укажите прокси-сервер. Пример: 10.0.68.52:3128"], set.Update(office).Order());
        Profile kept = set.Find(office.Id)!;
        Assert.Equal("Офис", kept.Name);
        Assert.False(kept.Proxy.Enabled);
    }

    [Fact]
    public void Profile_cannot_be_renamed_to_automatic()
    {
        var set = new ProfileSet();
        Profile office = Added(set, "Офис");

        office.Name = "АВТОМАТИЧЕСКИ";

        Assert.Equal(["Название «Автоматически» занято встроенным профилем."], set.Update(office));
        Assert.Equal("Офис", set.Find(office.Id)!.Name);
    }

    [Fact]
    public void Automatic_profile_cannot_be_edited()
    {
        var set = new ProfileSet();
        Profile automatic = Profile.Automatic;
        automatic.Proxy.Enabled = true;
        automatic.Proxy.Server = "10.0.68.52:3128";

        Assert.Equal(["Профиль «Автоматически» встроенный, его нельзя изменить."], set.Update(automatic));
        Assert.False(set.Find(Profile.AutomaticId)!.Proxy.Enabled);
    }

    [Fact]
    public void Profiles_given_out_are_copies()
    {
        var set = new ProfileSet();
        Profile office = Added(set, "Офис");

        office.Name = "Дом";
        set.Profiles[0].Name = "Кафе";
        set.Find(office.Id)!.Name = "";

        Assert.Equal("Офис", set.Find(office.Id)!.Name);
    }

    [Fact]
    public void Binding_or_default_must_refer_to_an_existing_profile()
    {
        var set = new ProfileSet();

        Assert.Throws<ArgumentException>(() => set.Bind("Office", Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => set.DefaultProfileId = Guid.NewGuid());
        Assert.Empty(set.Bindings);
        Assert.Equal(Profile.AutomaticId, set.DefaultProfileId);
    }

    [Fact]
    public void Network_with_an_empty_key_cannot_be_bound()
    {
        var set = new ProfileSet();

        Assert.Throws<ArgumentException>(() => set.Bind("", Profile.AutomaticId));
        Assert.Empty(set.Bindings);
    }
}
