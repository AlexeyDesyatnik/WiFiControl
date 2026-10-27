namespace WiFiControl.Core.Model;

/// <summary>
/// Профили, привязки «сеть → профиль» и профиль по умолчанию: по ним определяется, какой профиль применить к сети.
/// Профили хранятся копиями и выдаются копиями, чтобы правка полученного не обходила проверку.
/// «Автоматически» в <see cref="Profiles"/> не входит, но на него можно ссылаться в привязках и умолчании.
/// </summary>
public sealed class ProfileSet
{
    private readonly List<Profile> _profiles = [];
    private readonly Dictionary<string, Guid> _bindings = new(NetworkKey.Comparer);
    private Guid _defaultProfileId = Profile.AutomaticId;

    public IReadOnlyList<Profile> Profiles => _profiles.Select(profile => profile.Clone()).ToList();

    public IReadOnlyDictionary<string, Guid> Bindings => _bindings.AsReadOnly();

    /// <summary>Профиль для сетей без привязки: любой профиль, изначально «Автоматически».</summary>
    public Guid DefaultProfileId
    {
        get => _defaultProfileId;
        set
        {
            RequireProfile(value);
            _defaultProfileId = value;
        }
    }

    /// <summary>Привязывает сеть к профилю, заменяя её прежнюю привязку. Привязка к «Автоматически» допустима.</summary>
    public void Bind(string network, Guid profileId)
    {
        ArgumentException.ThrowIfNullOrEmpty(network);
        RequireProfile(profileId);
        _bindings[network] = profileId;
    }

    /// <summary>Убирает привязку сети: сеть возвращается к профилю по умолчанию. Возвращает, была ли привязка.</summary>
    public bool Unbind(string network)
    {
        return _bindings.Remove(network);
    }

    /// <summary>Профиль для сети: её привязка, а если её нет — профиль по умолчанию.</summary>
    public Profile ProfileFor(string network)
    {
        return Find(_bindings.GetValueOrDefault(network, _defaultProfileId))!;
    }

    public Profile? Find(Guid id)
    {
        if (id == Profile.AutomaticId) return Profile.Automatic;

        return _profiles.Find(profile => profile.Id == id)?.Clone();
    }

    /// <summary>Добавляет копию профиля, если он проходит проверку. Возвращает ошибки по-русски.</summary>
    public List<string> Add(Profile profile)
    {
        if (profile.IsAutomatic) return ["Профиль «Автоматически» встроенный, его нельзя добавить."];
        if (_profiles.Exists(existing => existing.Id == profile.Id))
            throw new ArgumentException("Профиль с id " + profile.Id + " уже есть.", nameof(profile));

        List<string> errors = Check(profile);
        if (errors.Count == 0) _profiles.Add(profile.Clone());
        return errors;
    }

    /// <summary>
    /// Заменяет профиль с тем же <see cref="Profile.Id"/> копией переданного, если он проходит проверку.
    /// Привязки и профиль по умолчанию ссылаются на <c>id</c>, поэтому переименование и правка их не касаются.
    /// </summary>
    public List<string> Update(Profile profile)
    {
        if (profile.IsAutomatic) return ["Профиль «Автоматически» встроенный, его нельзя изменить."];
        RequireProfile(profile.Id);

        List<string> errors = Check(profile);
        if (errors.Count == 0) _profiles[_profiles.FindIndex(existing => existing.Id == profile.Id)] = profile.Clone();
        return errors;
    }

    /// <summary>
    /// Удаляет профиль вместе с его привязками и возвращает ключи сетей, которые были к нему привязаны:
    /// они переходят на профиль по умолчанию. Если удалён профиль по умолчанию, умолчанием становится «Автоматически».
    /// </summary>
    public IReadOnlyList<string> Remove(Guid id)
    {
        if (id == Profile.AutomaticId)
            throw new InvalidOperationException("Профиль «Автоматически» встроенный, его нельзя удалить.");
        RequireProfile(id);

        List<string> affected = _bindings.Where(binding => binding.Value == id).Select(binding => binding.Key).ToList();
        foreach (string network in affected)
        {
            _bindings.Remove(network);
        }
        if (_defaultProfileId == id) _defaultProfileId = Profile.AutomaticId;
        _profiles.RemoveAll(profile => profile.Id == id);
        return affected;
    }

    private void RequireProfile(Guid id)
    {
        if (id != Profile.AutomaticId && !_profiles.Exists(profile => profile.Id == id))
            throw new ArgumentException("Профиля с id " + id + " нет.", nameof(id));
    }

    /// <summary>Правила проверки профиля и то, что зависит от остальных профилей: имена уникальны без учёта регистра.</summary>
    private List<string> Check(Profile profile)
    {
        List<string> errors = ProfileRules.Check(profile);
        if (profile.Name.Length == 0) return errors;

        if (SameName(profile.Name, Profile.AutomaticName))
            errors.Add("Название «" + Profile.AutomaticName + "» занято встроенным профилем.");
        else if (_profiles.Exists(other => other.Id != profile.Id && SameName(other.Name, profile.Name)))
            errors.Add("Профиль с названием «" + profile.Name + "» уже есть.");
        return errors;
    }

    private static bool SameName(string one, string other)
    {
        return string.Equals(one, other, StringComparison.OrdinalIgnoreCase);
    }
}
