namespace WiFiControl.Core.Model;

/// <summary>
/// Именованный набор из настроек IPv4 и настроек прокси. Не привязан ни к сети, ни к адаптеру.
/// <see cref="Id"/> не меняется при переименовании и правке: по нему на профиль ссылаются привязки.
/// </summary>
public sealed class Profile
{
    /// <summary><c>id</c> встроенного профиля «Автоматически»; в файле на него ссылаются привязки и профиль по умолчанию.</summary>
    public static readonly Guid AutomaticId = new("00000000-0000-0000-0000-000000000001");

    public const string AutomaticName = "Автоматически";

    /// <summary>
    /// Встроенный профиль «Автоматически»: адрес и DNS по DHCP, прокси выключен.
    /// Каждый раз новый экземпляр, чтобы правка полученного не меняла встроенный.
    /// </summary>
    public static Profile Automatic => new(AutomaticId) { Name = AutomaticName };

    public Profile() : this(Guid.NewGuid())
    {
    }

    public Profile(Guid id)
    {
        Id = id;
    }

    public Guid Id { get; }
    public string Name { get; set; } = "";
    public Ipv4Settings Ipv4 { get; set; } = new();
    public ProxySettings Proxy { get; set; } = new();

    public bool IsAutomatic => Id == AutomaticId;

    public Profile Clone()
    {
        return new Profile(Id) { Name = Name, Ipv4 = Ipv4.Clone(), Proxy = Proxy.Clone() };
    }
}
