using System.Text.Json;

namespace WiFiControl.Core.Storage;

/// <summary>Выбор Wi-Fi-адаптера на этом компьютере, в приложении — в %LOCALAPPDATA%\WiFiControl.</summary>
public sealed class StateStore(string folder)
{
    private const int FormatVersion = 1;
    private readonly string _filePath = Path.Combine(folder, "State.json");

    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public Guid? Load()
    {
        try
        {
            var file = JsonSerializer.Deserialize<StoredState>(File.ReadAllText(_filePath), Options);
            return file is { Version: FormatVersion, WifiAdapter: var adapter } && adapter != Guid.Empty ? adapter : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public void Save(Guid wifiAdapter)
    {
        Directory.CreateDirectory(folder);
        string temporary = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            {
                JsonSerializer.Serialize(stream, new StoredState(FormatVersion, wifiAdapter), Options);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _filePath, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private sealed record StoredState(int Version, Guid WifiAdapter);
}
