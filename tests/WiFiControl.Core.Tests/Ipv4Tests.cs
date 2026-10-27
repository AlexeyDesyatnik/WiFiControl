using WiFiControl.Core.Model;

namespace WiFiControl.Core.Tests;

public class Ipv4Tests
{
    [Theory]
    [InlineData("10.68.12.37", 0x0A440C25u)]
    [InlineData("0.0.0.0", 0u)]
    [InlineData("255.255.255.255", 0xFFFFFFFFu)]
    [InlineData("  192.168.1.1  ", 0xC0A80101u)]
    [InlineData("010.068.012.037", 0x0A440C25u)]
    public void Parses_dotted_address(string text, uint expected)
    {
        Assert.True(Ipv4.TryParse(text, out uint value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("10.68.12")]
    [InlineData("10.68.12.")]
    [InlineData("10.68.12.256")]
    [InlineData("10.68.12.1.5")]
    [InlineData("10.68.12.-1")]
    [InlineData("10.68.12.1a")]
    [InlineData("10.68. 12.1")]
    [InlineData("10.68.12.0001")]
    public void Rejects_what_is_not_an_address(string? text)
    {
        Assert.False(Ipv4.TryParse(text, out _));
    }

    [Fact]
    public void Formats_address_back()
    {
        Assert.Equal("10.68.12.37", Ipv4.Format(0x0A440C25u));
    }

    [Fact]
    public void Address_with_leading_zeros_is_formatted_without_them()
    {
        Assert.True(Ipv4.TryParse("010.068.012.037", out uint value));
        Assert.Equal("10.68.12.37", Ipv4.Format(value));
    }

    [Theory]
    [InlineData("255.255.255.0", 24)]
    [InlineData("255.255.252.0", 22)]
    [InlineData("255.255.255.255", 32)]
    [InlineData("128.0.0.0", 1)]
    [InlineData("0.0.0.0", 0)]
    public void Converts_mask_to_prefix(string mask, int expected)
    {
        Assert.True(Ipv4.TryMaskToPrefix(mask, out int prefix));
        Assert.Equal(expected, prefix);
    }

    [Theory]
    [InlineData("255.0.255.0")]
    [InlineData("0.255.255.255")]
    [InlineData("255.255.255.1")]
    [InlineData("маска")]
    public void Rejects_mask_with_holes(string mask)
    {
        Assert.False(Ipv4.TryMaskToPrefix(mask, out _));
    }

    [Theory]
    [InlineData(24, "255.255.255.0")]
    [InlineData(22, "255.255.252.0")]
    [InlineData(32, "255.255.255.255")]
    [InlineData(0, "0.0.0.0")]
    public void Converts_prefix_to_mask(int prefix, string expected)
    {
        Assert.Equal(expected, Ipv4.PrefixToMask(prefix));
    }

    [Theory]
    [InlineData("255.255.255.0", "255.255.255.0")]
    [InlineData("/24", "255.255.255.0")]
    [InlineData("24", "255.255.255.0")]
    [InlineData(" /22 ", "255.255.252.0")]
    [InlineData("32", "255.255.255.255")]
    public void Reads_mask_in_either_notation(string text, string expected)
    {
        Assert.True(Ipv4.TryParseMask(text, out string mask));
        Assert.Equal(expected, mask);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("33")]
    [InlineData("-1")]
    [InlineData("2 4")]
    [InlineData("255.255.0.255")]
    [InlineData("255.255.255")]
    public void Rejects_what_is_not_a_mask(string? text)
    {
        Assert.False(Ipv4.TryParseMask(text, out _));
    }

    [Theory]
    [InlineData("10.68.12.37", "10.68.12.1", "255.255.255.0", true)]
    [InlineData("10.68.12.37", "10.68.13.1", "255.255.255.0", false)]
    [InlineData("10.68.12.37", "10.68.13.1", "255.255.252.0", true)]
    public void Checks_whether_addresses_share_a_subnet(string one, string other, string mask, bool expected)
    {
        Assert.True(Ipv4.TryParse(one, out uint a));
        Assert.True(Ipv4.TryParse(other, out uint b));
        Assert.True(Ipv4.TryParse(mask, out uint m));
        Assert.Equal(expected, Ipv4.InSameSubnet(a, b, m));
    }

    [Fact]
    public void Addresses_are_the_same_regardless_of_spelling()
    {
        Assert.True(Ipv4.Same("10.68.12.37", " 010.068.012.037"));
        Assert.True(Ipv4.Same("", null));
        Assert.False(Ipv4.Same("10.68.12.37", "10.68.12.38"));
        Assert.False(Ipv4.Same("10.68.12.37", ""));
    }
}
