using System.Text.Json;
using WiFiControl.Core.Storage;

namespace WiFiControl.Core.Tests;

public sealed class StateStoreTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("WiFiControl.Tests.").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Saved_adapter_is_read_back_from_the_versioned_machine_file()
    {
        var store = new StateStore(_folder);
        store.Save(FakeWifi.WifiId);

        Assert.Equal(FakeWifi.WifiId, new StateStore(_folder).Load());
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(_folder, "State.json")));
        Assert.Equal(1, json.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(FakeWifi.WifiId, json.RootElement.GetProperty("wifiAdapter").GetGuid());
    }

    [Fact]
    public void Missing_file_has_no_saved_adapter()
    {
        Assert.Null(new StateStore(_folder).Load());
        Assert.Empty(Directory.EnumerateFiles(_folder));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("""{"version":2,"wifiAdapter":"00000000-0000-0000-0000-000000000002"}""")]
    [InlineData("""{"version":1,"wifiAdapter":"invalid"}""")]
    [InlineData("""{"version":1}""")]
    public void Unrecognized_file_is_replaced_only_when_an_adapter_is_saved(string contents)
    {
        string path = Path.Combine(_folder, "State.json");
        File.WriteAllText(path, contents);
        var store = new StateStore(_folder);

        Assert.Null(store.Load());
        Assert.Equal(contents, File.ReadAllText(path));

        store.Save(FakeWifi.WifiId);

        Assert.Equal(FakeWifi.WifiId, new StateStore(_folder).Load());
        Assert.Equal([path], Directory.GetFiles(_folder));
    }
}
