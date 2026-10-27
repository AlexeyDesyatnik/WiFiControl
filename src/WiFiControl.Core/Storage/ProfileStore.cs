using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using WiFiControl.Core.Model;

namespace WiFiControl.Core.Storage;

/// <summary>
/// Профили, привязки и профиль по умолчанию в файле <c>WiFiControl.json</c>. Папку передают снаружи,
/// в приложении это <c>%APPDATA%\WiFiControl</c>. Файл читается и правится руками.
/// </summary>
public sealed class ProfileStore
{
    public const string FileName = "WiFiControl.json";

    private const int FormatVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // Русские названия пишутся как есть, а не \u-последовательностями, чтобы файл можно было читать
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _folder;
    private bool _loaded;

    public ProfileStore(string folder)
    {
        _folder = folder;
        FilePath = Path.Combine(folder, FileName);
    }

    public string FilePath { get; }

    public ProfileSet Profiles { get; private set; } = new();

    /// <summary>
    /// Почему файл не удалось прочитать, по-русски. Пока ошибка не устранена, сохранение запрещено:
    /// иначе пустой набор затёр бы файл, который человек ещё может починить.
    /// </summary>
    public string? LoadError { get; private set; }

    /// <summary>Читает файл. Отсутствующий файл — начальное состояние: профилей нет, умолчание — «Автоматически».</summary>
    public void Load()
    {
        Profiles = new ProfileSet();
        LoadError = null;
        _loaded = true;
        if (!File.Exists(FilePath)) return;

        try
        {
            Profiles = Read(File.ReadAllText(FilePath, Encoding.UTF8));
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            LoadError = "Не удалось прочитать " + FilePath + ". " + ex.Message;
        }
    }

    /// <summary>Записывает во временный файл рядом, затем перемещает его поверх прежнего.</summary>
    public void Save()
    {
        if (!_loaded) throw new InvalidOperationException("Файл ещё не прочитан: без Load сохранение затёрло бы его пустым набором.");
        if (LoadError != null)
            throw new InvalidOperationException("Файл не был прочитан, поэтому не будет перезаписан. " + LoadError);

        var file = new StoredFile
        {
            Version = FormatVersion,
            Profiles = Profiles.Profiles.Select(FromProfile).ToList<StoredProfile?>(),
            Bindings = Profiles.Bindings.Select(binding => new StoredBinding { Network = binding.Key, Profile = binding.Value }).ToList<StoredBinding?>(),
            DefaultProfile = Profiles.DefaultProfileId,
        };

        Directory.CreateDirectory(_folder);
        string temporary = FilePath + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write))
            {
                stream.Write(new UTF8Encoding(false).GetBytes(JsonSerializer.Serialize(file, Options)));
                // На диск до перемещения: иначе после отключения питания на месте прежнего файла мог бы оказаться пустой
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }
    }

    /// <summary>Файл могли править руками: он проверяется так же строго, как ввод в окне.</summary>
    private static ProfileSet Read(string text)
    {
        // Версия читается отдельно: в файле более новой версии остальное может быть устроено иначе
        StoredVersion? stamp;
        StoredFile? file;
        try
        {
            stamp = JsonSerializer.Deserialize<StoredVersion>(text, Options);
            if (stamp?.Version > FormatVersion)
                throw new FormatException("Файл записан более новой версией WiFiControl (формат " + stamp.Version
                    + ", эта версия понимает формат " + FormatVersion + "). Обновите WiFiControl.");
            file = JsonSerializer.Deserialize<StoredFile>(text, Options);
        }
        catch (JsonException ex)
        {
            throw Corrupt(ex.LineNumber is long line ? "ошибка в строке " + (line + 1) + "." : "ошибка в записи JSON.");
        }
        if (file == null) throw Corrupt("ожидался объект с полем «version».");
        if (file.Version == null) throw Corrupt("не указана версия формата.");
        if (file.Version < 1) throw Corrupt("неверная версия формата " + file.Version + ".");

        var set = new ProfileSet();
        foreach (StoredProfile? stored in file.Profiles ?? [])
        {
            if (stored == null) throw Corrupt("профиль должен быть объектом.");
            string name = stored.Name ?? "";
            if (stored.Id is not Guid id) throw Corrupt("у профиля «" + name + "» нет id.");
            if (id == Profile.AutomaticId)
                throw new FormatException("У профиля «" + name + "» id встроенного профиля «" + Profile.AutomaticName + "».");
            if (set.Find(id) != null) throw new FormatException("Два профиля с id " + id + ".");

            List<string> errors = set.Add(ToProfile(id, stored));
            if (errors.Count > 0) throw new FormatException("Профиль «" + name.Trim() + "» задан неверно. " + string.Join(" ", errors));
        }

        foreach (StoredBinding? binding in file.Bindings ?? [])
        {
            if (binding == null) throw Corrupt("привязка должна быть объектом.");
            if (string.IsNullOrEmpty(binding.Network)) throw Corrupt("у привязки не указана сеть.");
            if (binding.Profile is not Guid profileId) throw Corrupt("у привязки сети «" + binding.Network + "» не указан профиль.");
            if (set.Bindings.ContainsKey(binding.Network)) throw new FormatException("Сеть «" + binding.Network + "» привязана дважды.");
            if (set.Find(profileId) == null) throw new FormatException("Привязка сети «" + binding.Network + "»: нет профиля с id " + profileId + ".");

            set.Bind(binding.Network, profileId);
        }

        Guid defaultId = file.DefaultProfile ?? Profile.AutomaticId;
        if (set.Find(defaultId) == null) throw new FormatException("Профиль по умолчанию: нет профиля с id " + defaultId + ".");
        set.DefaultProfileId = defaultId;
        return set;
    }

    private static FormatException Corrupt(string detail)
    {
        return new FormatException("Файл повреждён: " + detail);
    }

    private static Profile ToProfile(Guid id, StoredProfile stored)
    {
        var profile = new Profile(id) { Name = stored.Name ?? "" };

        StoredIpv4 ipv4 = stored.Ipv4 ?? new();
        profile.Ipv4.AutoAddress = ipv4.AutoAddress ?? true;
        profile.Ipv4.Address = ipv4.Address ?? "";
        profile.Ipv4.Mask = ipv4.Mask ?? "";
        profile.Ipv4.Gateway = ipv4.Gateway ?? "";
        profile.Ipv4.AutoDns = ipv4.AutoDns ?? true;
        profile.Ipv4.Dns.AddRange(Entries(ipv4.Dns, "DNS-серверов", profile.Name));

        StoredProxy proxy = stored.Proxy ?? new();
        profile.Proxy.Enabled = proxy.Enabled ?? false;
        profile.Proxy.Server = proxy.Server ?? "";
        profile.Proxy.BypassLocal = proxy.BypassLocal ?? false;
        profile.Proxy.PrivateNetworksDirect = proxy.PrivateNetworksDirect ?? false;
        profile.Proxy.Exceptions.AddRange(Entries(proxy.Exceptions, "исключений прокси", profile.Name));
        return profile;
    }

    private static List<string> Entries(List<string?>? list, string what, string profileName)
    {
        if (list == null) return [];
        if (list.Contains(null)) throw Corrupt("в списке " + what + " профиля «" + profileName.Trim() + "» есть null.");
        return list!;
    }

    private static StoredProfile FromProfile(Profile profile)
    {
        return new StoredProfile
        {
            Id = profile.Id,
            Name = profile.Name,
            Ipv4 = new StoredIpv4
            {
                AutoAddress = profile.Ipv4.AutoAddress,
                Address = profile.Ipv4.Address,
                Mask = profile.Ipv4.Mask,
                Gateway = profile.Ipv4.Gateway,
                AutoDns = profile.Ipv4.AutoDns,
                Dns = [.. profile.Ipv4.Dns],
            },
            Proxy = new StoredProxy
            {
                Enabled = profile.Proxy.Enabled,
                Server = profile.Proxy.Server,
                BypassLocal = profile.Proxy.BypassLocal,
                PrivateNetworksDirect = profile.Proxy.PrivateNetworksDirect,
                Exceptions = [.. profile.Proxy.Exceptions],
            },
        };
    }

    // Вид файла. Поля допускают null: файл правят руками, и пропущенное поле не должно ронять чтение
    private sealed class StoredVersion
    {
        public int? Version { get; set; }
    }

    private sealed class StoredFile
    {
        public int? Version { get; set; }
        public List<StoredProfile?>? Profiles { get; set; }
        public List<StoredBinding?>? Bindings { get; set; }
        public Guid? DefaultProfile { get; set; }
    }

    private sealed class StoredProfile
    {
        public Guid? Id { get; set; }
        public string? Name { get; set; }
        public StoredIpv4? Ipv4 { get; set; }
        public StoredProxy? Proxy { get; set; }
    }

    private sealed class StoredIpv4
    {
        public bool? AutoAddress { get; set; }
        public string? Address { get; set; }
        public string? Mask { get; set; }
        public string? Gateway { get; set; }
        public bool? AutoDns { get; set; }
        public List<string?>? Dns { get; set; }
    }

    private sealed class StoredProxy
    {
        public bool? Enabled { get; set; }
        public string? Server { get; set; }
        public bool? BypassLocal { get; set; }
        public bool? PrivateNetworksDirect { get; set; }
        public List<string?>? Exceptions { get; set; }
    }

    private sealed class StoredBinding
    {
        public string? Network { get; set; }
        public Guid? Profile { get; set; }
    }
}
