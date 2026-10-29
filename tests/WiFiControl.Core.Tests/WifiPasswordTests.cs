using WiFiControl.Core.Model;

namespace WiFiControl.Core.Tests;

public class WifiPasswordTests
{
    [Theory]
    [InlineData("12345678")]
    [InlineData("pass with spaces ~!")]
    [InlineData("123456789012345678901234567890123456789012345678901234567890123")]
    public void From_8_to_63_characters_is_a_passphrase(string password)
    {
        Assert.True(WifiPassword.IsValid(password));
        Assert.False(WifiPassword.IsKey(password));
    }

    [Fact]
    public void Exactly_64_hex_digits_is_a_key()
    {
        string key = string.Concat(Enumerable.Repeat("0123456789abcDEF", 4));

        Assert.True(WifiPassword.IsValid(key));
        Assert.True(WifiPassword.IsKey(key));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1234567")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdeg")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0")]
    // Парольная фраза WPA — только печатные символы ASCII
    [InlineData("пароль-кириллицей")]
    [InlineData("tab	inside")]
    public void Anything_else_is_not_a_password(string password)
    {
        Assert.False(WifiPassword.IsValid(password));
    }

    [Fact]
    public void Key_is_not_a_password_where_keys_are_not_accepted()
    {
        string key = string.Concat(Enumerable.Repeat("0123456789abcdef", 4));

        Assert.False(WifiPassword.IsValid(key, keyAccepted: false));
        Assert.True(WifiPassword.IsValid("12345678", keyAccepted: false));
    }
}
