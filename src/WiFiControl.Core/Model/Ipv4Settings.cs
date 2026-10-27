namespace WiFiControl.Core.Model;

/// <summary>Настройки IPv4: адрес автоматически или вручную, и независимо от него DNS автоматически или вручную.</summary>
public sealed class Ipv4Settings
{
    public bool AutoAddress { get; set; } = true;
    public string Address { get; set; } = "";
    public string Mask { get; set; } = "";
    public string Gateway { get; set; } = "";
    /// <summary>При адресе, заданном вручную, DNS всегда задаётся вручную (возможно, пустым списком).</summary>
    public bool AutoDns { get; set; } = true;
    public List<string> Dns { get; } = [];

    public Ipv4Settings Clone()
    {
        var copy = new Ipv4Settings { AutoAddress = AutoAddress, Address = Address, Mask = Mask, Gateway = Gateway, AutoDns = AutoDns };
        copy.Dns.AddRange(Dns);
        return copy;
    }
}
