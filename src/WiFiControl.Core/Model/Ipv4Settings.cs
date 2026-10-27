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

    /// <summary>Одинаковы ли настройки по смыслу: при автоматическом адресе или DNS заданное вручную не сравнивается. Перенесено из NetSwitch.</summary>
    public bool SameAs(Ipv4Settings other)
    {
        if (AutoAddress != other.AutoAddress || AutoDns != other.AutoDns) return false;

        if (!AutoAddress)
        {
            if (!Ipv4.Same(Address, other.Address) || !Ipv4.Same(Mask, other.Mask) || !Ipv4.Same(Gateway, other.Gateway)) return false;
        }
        if (!AutoDns)
        {
            if (Dns.Count != other.Dns.Count) return false;
            for (int i = 0; i < Dns.Count; i++)
            {
                if (!Ipv4.Same(Dns[i], other.Dns[i])) return false;
            }
        }
        return true;
    }
}
