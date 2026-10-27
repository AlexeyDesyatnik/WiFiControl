using System.Text;
using WiFiControl.Core.Model;

namespace WiFiControl.Core.Tests;

public class NetworkKeyTests
{
    [Fact]
    public void Utf8_name_becomes_the_text_itself()
    {
        Assert.Equal("Офис 5G", NetworkKey.FromBytes(Encoding.UTF8.GetBytes("Офис 5G")));
    }

    [Fact]
    public void Name_that_is_not_utf8_becomes_hex_of_its_bytes()
    {
        // «Привет» в cp1251
        byte[] cp1251 = [0xCF, 0xF0, 0xE8, 0xE2, 0xE5, 0xF2];

        Assert.Equal("hex:CFF0E8E2E5F2", NetworkKey.FromBytes(cp1251));
    }

    [Fact]
    public void Keys_differing_only_in_case_are_different()
    {
        string upper = NetworkKey.FromBytes("Office"u8);
        string lower = NetworkKey.FromBytes("office"u8);

        Assert.False(NetworkKey.Comparer.Equals(upper, lower));
    }
}
