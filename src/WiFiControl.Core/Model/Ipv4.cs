using System.Globalization;

namespace WiFiControl.Core.Model;

/// <summary>Разбор и запись адресов и масок IPv4. Перенесено из NetSwitch.</summary>
public static class Ipv4
{
    /// <summary>Адрес вида 10.68.12.37. Ведущие нули допустимы и читаются как десятичные, а не восьмеричные.</summary>
    public static bool TryParse(string? text, out uint value)
    {
        value = 0;
        if (text == null) return false;

        string[] parts = text.Trim().Split('.');
        if (parts.Length != 4) return false;

        uint result = 0;
        foreach (string part in parts)
        {
            if (part.Length == 0 || part.Length > 3) return false;

            int octet = 0;
            foreach (char c in part)
            {
                if (c < '0' || c > '9') return false;
                octet = octet * 10 + (c - '0');
            }
            if (octet > 255) return false;

            result = (result << 8) | (uint)octet;
        }

        value = result;
        return true;
    }

    public static string Format(uint value)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}.{1}.{2}.{3}",
            (value >> 24) & 255, (value >> 16) & 255, (value >> 8) & 255, value & 255);
    }

    /// <summary>Маска вида 255.255.255.0 → длина префикса 24. Маски с «дырами» не принимаются.</summary>
    public static bool TryMaskToPrefix(string? mask, out int prefix)
    {
        prefix = 0;
        if (!TryParse(mask, out uint value)) return false;

        uint inverted = ~value;
        if ((inverted & unchecked(inverted + 1)) != 0) return false;

        int count = 0;
        while (value != 0)
        {
            count++;
            value <<= 1;
        }

        prefix = count;
        return true;
    }

    public static string PrefixToMask(int prefix)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(prefix);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(prefix, 32);
        return Format(prefix == 0 ? 0 : uint.MaxValue << (32 - prefix));
    }

    /// <summary>Маска в любой записи — 255.255.255.0, /24 или 24 — приводится к виду 255.255.255.0.</summary>
    public static bool TryParseMask(string? text, out string mask)
    {
        mask = "";
        string value = (text ?? "").Trim();
        if (value.StartsWith('/')) value = value[1..].Trim();

        int prefix;
        if (value.Contains('.'))
        {
            if (!TryMaskToPrefix(value, out prefix)) return false;
        }
        else
        {
            if (value.Length == 0 || value.Length > 2) return false;
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out prefix) || prefix > 32) return false;
        }

        mask = PrefixToMask(prefix);
        return true;
    }

    public static bool InSameSubnet(uint one, uint other, uint mask)
    {
        return (one & mask) == (other & mask);
    }

    /// <summary>Совпадают ли два адреса, даже если записаны по-разному (010.1.1.1 и 10.1.1.1).</summary>
    public static bool Same(string? one, string? other)
    {
        if (TryParse(one, out uint a) && TryParse(other, out uint b)) return a == b;
        return string.Equals((one ?? "").Trim(), (other ?? "").Trim(), StringComparison.Ordinal);
    }
}
