using WiFiControl.Core.Model;
using WiFiControl.Core.Storage;

namespace WiFiControl.Core.Tests;

/// <summary>Хранилище проверяется через настоящую файловую систему во временной папке.</summary>
public sealed class ProfileStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "WiFiControl.Tests." + Guid.NewGuid().ToString("N"));

    public ProfileStoreTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        Directory.Delete(_folder, recursive: true);
    }

    private string FilePath => Path.Combine(_folder, ProfileStore.FileName);

    private ProfileStore Loaded()
    {
        var store = new ProfileStore(_folder);
        store.Load();
        return store;
    }

    [Fact]
    public void Saved_profiles_bindings_and_default_are_read_back_the_same()
    {
        var store = Loaded();
        var office = new Profile { Name = "Офис" };
        office.Ipv4.AutoAddress = false;
        office.Ipv4.Address = "10.68.12.37";
        office.Ipv4.Mask = "255.255.255.0";
        office.Ipv4.Gateway = "10.68.12.1";
        office.Ipv4.Dns.AddRange(["10.0.68.10", "10.0.68.11"]);
        office.Proxy.Enabled = true;
        office.Proxy.Server = "10.0.68.52:3128";
        office.Proxy.Exceptions.AddRange(["*.corp.local", "intranet"]);
        office.Proxy.BypassLocal = true;
        office.Proxy.PrivateNetworksDirect = true;
        var lab = new Profile { Name = "Лаборатория" };
        lab.Ipv4.AutoDns = false;
        lab.Ipv4.Dns.Add("192.168.1.1");
        Assert.Empty(store.Profiles.Add(office));
        Assert.Empty(store.Profiles.Add(lab));
        store.Profiles.Bind("Office", office.Id);
        store.Profiles.Bind("Дом", Profile.AutomaticId);
        store.Profiles.Bind("hex:C0FF", lab.Id);
        store.Profiles.DefaultProfileId = lab.Id;

        store.Save();
        var reread = Loaded();

        Assert.Null(reread.LoadError);
        Assert.Equal(2, reread.Profiles.Profiles.Count);
        Profile readOffice = reread.Profiles.Find(office.Id)!;
        Assert.Equal("Офис", readOffice.Name);
        Assert.False(readOffice.Ipv4.AutoAddress);
        Assert.Equal("10.68.12.37", readOffice.Ipv4.Address);
        Assert.Equal("255.255.255.0", readOffice.Ipv4.Mask);
        Assert.Equal("10.68.12.1", readOffice.Ipv4.Gateway);
        Assert.False(readOffice.Ipv4.AutoDns);
        Assert.Equal(["10.0.68.10", "10.0.68.11"], readOffice.Ipv4.Dns);
        Assert.True(readOffice.Proxy.Enabled);
        Assert.Equal("10.0.68.52:3128", readOffice.Proxy.Server);
        Assert.Equal(["*.corp.local", "intranet"], readOffice.Proxy.Exceptions);
        Assert.True(readOffice.Proxy.BypassLocal);
        Assert.True(readOffice.Proxy.PrivateNetworksDirect);
        Profile readLab = reread.Profiles.Find(lab.Id)!;
        Assert.Equal("Лаборатория", readLab.Name);
        Assert.True(readLab.Ipv4.AutoAddress);
        Assert.False(readLab.Ipv4.AutoDns);
        Assert.Equal(["192.168.1.1"], readLab.Ipv4.Dns);
        Assert.False(readLab.Proxy.Enabled);
        Assert.False(readLab.Proxy.BypassLocal);
        Assert.False(readLab.Proxy.PrivateNetworksDirect);
        Assert.Equal(3, reread.Profiles.Bindings.Count);
        Assert.Equal(office.Id, reread.Profiles.Bindings["Office"]);
        Assert.Equal(Profile.AutomaticId, reread.Profiles.Bindings["Дом"]);
        Assert.Equal(lab.Id, reread.Profiles.Bindings["hex:C0FF"]);
        Assert.Equal(lab.Id, reread.Profiles.DefaultProfileId);
    }

    [Fact]
    public void Missing_file_gives_no_profiles_and_automatic_as_default()
    {
        var store = Loaded();

        Assert.Null(store.LoadError);
        Assert.Empty(store.Profiles.Profiles);
        Assert.Empty(store.Profiles.Bindings);
        Assert.Equal(Profile.AutomaticId, store.Profiles.DefaultProfileId);
    }

    [Fact]
    public void Store_creates_its_folder_on_first_save()
    {
        string folder = Path.Combine(_folder, "WiFiControl");
        var store = new ProfileStore(folder);
        store.Load();

        store.Save();

        Assert.True(File.Exists(Path.Combine(folder, ProfileStore.FileName)));
    }

    [Fact]
    public void Saved_file_is_indented_utf8_with_names_written_as_is()
    {
        var store = Loaded();
        Assert.Empty(store.Profiles.Add(new Profile { Name = "Офис" }));

        store.Save();

        byte[] bytes = File.ReadAllBytes(FilePath);
        Assert.NotEqual(0xEF, bytes[0]);
        string text = File.ReadAllText(FilePath);
        Assert.StartsWith("{\n  \"version\": 1,", text.ReplaceLineEndings("\n"));
        Assert.Contains("\"name\": \"Офис\"", text);
        Assert.DoesNotContain("Автоматически", text);
    }

    [Fact]
    public void No_temporary_file_is_left_after_saving_over_an_existing_file()
    {
        var store = Loaded();
        store.Save();
        Assert.Empty(store.Profiles.Add(new Profile { Name = "Офис" }));

        store.Save();

        Assert.Equal([FilePath], Directory.GetFiles(_folder));
        Assert.Single(Loaded().Profiles.Profiles);
    }

    private const string OfficeId = "6f1c3c1e-2a4b-4d5e-9f00-0000000000a1";
    private const string LabId = "6f1c3c1e-2a4b-4d5e-9f00-0000000000a2";
    private const string AutomaticId = "00000000-0000-0000-0000-000000000001";

    private static string ProfileJson(string id, string name, string ipv4 = """{ "autoAddress": true, "autoDns": true }""")
    {
        return $$"""{ "id": "{{id}}", "name": "{{name}}", "ipv4": {{ipv4}} }""";
    }

    private static string FileJson(string profiles = "", string bindings = "", string defaultProfile = AutomaticId, int version = 1)
    {
        return $$"""
            {
              "version": {{version}},
              "profiles": [ {{profiles}} ],
              "bindings": [ {{bindings}} ],
              "defaultProfile": "{{defaultProfile}}"
            }
            """;
    }

    public static TheoryData<string, string> UnreadableFiles => new()
    {
        { "{ \"version\": 1, \"profiles\": [", "повреждён" },
        { "[]", "повреждён" },
        { FileJson(version: 2), "более новой версией" },
        { """{ "version": 2, "profiles": { "Офис": {} } }""", "более новой версией" },
        { FileJson(version: 0), "неверная версия формата" },
        { FileJson(ProfileJson(OfficeId, "Офис", """{ "autoAddress": true, "autoDns": false, "dns": [ null ] }""")), "повреждён" },
        { FileJson(ProfileJson(OfficeId, "Офис", """{ "autoAddress": false, "address": "10.68.12.300", "mask": "/24" }""")), "Адрес IPv4 задан неверно" },
        { FileJson(ProfileJson(OfficeId, "Офис") + "," + ProfileJson(LabId, "офис")), "Профиль с названием «офис» уже есть" },
        { FileJson(ProfileJson(OfficeId, "автоматически")), "занято встроенным профилем" },
        { FileJson(ProfileJson(AutomaticId, "Мой")), "«Автоматически»" },
        { FileJson(ProfileJson(OfficeId, "Офис") + "," + ProfileJson(OfficeId, "Лаборатория")), "id " + OfficeId },
        { FileJson(bindings: $$"""{ "network": "Office", "profile": "{{LabId}}" }"""), "нет профиля с id " + LabId },
        { FileJson(defaultProfile: LabId), "нет профиля с id " + LabId },
        { FileJson(ProfileJson(OfficeId, "Офис"), $$"""{ "network": "Office", "profile": "{{OfficeId}}" }, { "network": "Office", "profile": "{{AutomaticId}}" }"""), "«Office» привязана дважды" },
    };

    [Theory]
    [MemberData(nameof(UnreadableFiles))]
    public void Unreadable_file_gives_a_reason_and_is_not_overwritten(string content, string reason)
    {
        File.WriteAllText(FilePath, content);

        var store = Loaded();

        Assert.NotNull(store.LoadError);
        Assert.Contains(reason, store.LoadError);
        Assert.Empty(store.Profiles.Profiles);
        Assert.Throws<InvalidOperationException>(store.Save);
        Assert.Equal(content, File.ReadAllText(FilePath));
    }

    [Fact]
    public void Newer_file_is_not_overwritten_even_after_changes()
    {
        string content = FileJson(ProfileJson(OfficeId, "Офис"), version: 2);
        File.WriteAllText(FilePath, content);
        var store = Loaded();
        Assert.Empty(store.Profiles.Add(new Profile { Name = "Дом" }));

        Assert.Throws<InvalidOperationException>(store.Save);

        Assert.Equal(content, File.ReadAllText(FilePath));
        Assert.Equal([FilePath], Directory.GetFiles(_folder));
    }

    [Fact]
    public void Store_that_was_never_loaded_does_not_overwrite_the_file()
    {
        string content = FileJson(ProfileJson(OfficeId, "Офис"));
        File.WriteAllText(FilePath, content);

        Assert.Throws<InvalidOperationException>(new ProfileStore(_folder).Save);

        Assert.Equal(content, File.ReadAllText(FilePath));
    }

    [Fact]
    public void Fixed_file_can_be_saved_after_loading_again()
    {
        File.WriteAllText(FilePath, "{");
        var store = Loaded();
        File.WriteAllText(FilePath, FileJson(ProfileJson(OfficeId, "Офис")));

        store.Load();
        store.Save();

        Assert.Null(store.LoadError);
        Assert.Equal("Офис", Loaded().Profiles.Profiles.Single().Name);
    }

    [Fact]
    public void Hand_written_file_without_optional_fields_is_read_with_defaults()
    {
        File.WriteAllText(FilePath, $$"""{ "version": 1, "profiles": [ { "id": "{{OfficeId}}", "name": " Офис " } ] }""");

        var store = Loaded();

        Assert.Null(store.LoadError);
        Profile office = store.Profiles.Profiles.Single();
        Assert.Equal("Офис", office.Name);
        Assert.True(office.Ipv4.AutoAddress);
        Assert.True(office.Ipv4.AutoDns);
        Assert.False(office.Proxy.Enabled);
        Assert.Empty(store.Profiles.Bindings);
        Assert.Equal(Profile.AutomaticId, store.Profiles.DefaultProfileId);
    }
}
