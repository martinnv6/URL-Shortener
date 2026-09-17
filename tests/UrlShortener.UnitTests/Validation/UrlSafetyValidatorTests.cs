using UrlShortener.Core.Validation;

namespace UrlShortener.UnitTests.Validation;

public class UrlSafetyValidatorTests
{
    // ─── Valid URLs ─────────────────────────────────────────────────

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("https://example.com/path?query=1")]
    [InlineData("http://example.com")]
    [InlineData("https://sub.domain.example.com")]
    public void Validate_ValidPublicUrl_ReturnsValid(string url)
    {
        var result = UrlSafetyValidator.Validate(url);
        Assert.True(result.IsValid);
        Assert.Null(result.ReasonCode);
    }

    [Fact]
    public void Validate_PublicIpAddress_ReturnsValid()
    {
        var result = UrlSafetyValidator.Validate("http://93.184.216.34");
        Assert.True(result.IsValid);
    }

    // ─── Null/Empty/Relative ────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_NullOrEmpty_ReturnsInvalidUrl(string? url)
    {
        var result = UrlSafetyValidator.Validate(url);
        Assert.False(result.IsValid);
        Assert.Equal("INVALID_URL", result.ReasonCode);
    }

    [Fact]
    public void Validate_RelativePath_ReturnsInvalidUrl()
    {
        var result = UrlSafetyValidator.Validate("/relative/path");
        Assert.False(result.IsValid);
        Assert.Equal("INVALID_URL", result.ReasonCode);
    }

    // ─── Scheme Restrictions ────────────────────────────────────────

    [Theory]
    [InlineData("ftp://example.com")]
    [InlineData("file:///etc/passwd")]
    [InlineData("gopher://evil.com")]
    public void Validate_ForbiddenScheme_ReturnsSchemeNotAllowed(string url)
    {
        var result = UrlSafetyValidator.Validate(url);
        Assert.False(result.IsValid);
        Assert.Equal("SCHEME_NOT_ALLOWED", result.ReasonCode);
    }

    // ─── Loopback Addresses ─────────────────────────────────────────

    [Theory]
    [InlineData("http://127.0.0.1")]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("http://127.255.255.255")]
    public void Validate_LoopbackIpv4_ReturnsLoopbackAddress(string url)
    {
        var result = UrlSafetyValidator.Validate(url);
        Assert.False(result.IsValid);
        Assert.Equal("LOOPBACK_ADDRESS", result.ReasonCode);
    }

    [Fact]
    public void Validate_Localhost_ReturnsLoopbackAddress()
    {
        var result = UrlSafetyValidator.Validate("http://localhost");
        Assert.False(result.IsValid);
        Assert.Equal("LOOPBACK_ADDRESS", result.ReasonCode);
    }

    [Fact]
    public void Validate_Ipv6Loopback_ReturnsLoopbackAddress()
    {
        var result = UrlSafetyValidator.Validate("http://[::1]");
        Assert.False(result.IsValid);
        Assert.Equal("LOOPBACK_ADDRESS", result.ReasonCode);
    }

    // ─── Private Networks (RFC 1918) ────────────────────────────────

    [Theory]
    [InlineData("http://10.0.0.1")]
    [InlineData("http://10.255.255.255")]
    public void Validate_PrivateNetwork10_ReturnsPrivateNetwork(string url)
    {
        var result = UrlSafetyValidator.Validate(url);
        Assert.False(result.IsValid);
        Assert.Equal("PRIVATE_NETWORK", result.ReasonCode);
    }

    [Theory]
    [InlineData("http://172.16.0.1")]
    [InlineData("http://172.31.255.255")]
    public void Validate_PrivateNetwork172_ReturnsPrivateNetwork(string url)
    {
        var result = UrlSafetyValidator.Validate(url);
        Assert.False(result.IsValid);
        Assert.Equal("PRIVATE_NETWORK", result.ReasonCode);
    }

    [Theory]
    [InlineData("http://192.168.0.1")]
    [InlineData("http://192.168.1.1")]
    public void Validate_PrivateNetwork192_ReturnsPrivateNetwork(string url)
    {
        var result = UrlSafetyValidator.Validate(url);
        Assert.False(result.IsValid);
        Assert.Equal("PRIVATE_NETWORK", result.ReasonCode);
    }

    // ─── Link-local / Cloud Metadata ────────────────────────────────

    [Theory]
    [InlineData("http://169.254.169.254")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://169.254.1.1")]
    public void Validate_LinkLocal_ReturnsLinkLocalAddress(string url)
    {
        var result = UrlSafetyValidator.Validate(url);
        Assert.False(result.IsValid);
        Assert.Equal("LINK_LOCAL_ADDRESS", result.ReasonCode);
    }

    // ─── IPv6-Mapped IPv4 ───────────────────────────────────────────

    [Fact]
    public void Validate_Ipv6MappedLoopback_ReturnsLoopbackAddress()
    {
        var result = UrlSafetyValidator.Validate("http://[::ffff:127.0.0.1]");
        Assert.False(result.IsValid);
        Assert.Equal("LOOPBACK_ADDRESS", result.ReasonCode);
    }

    [Fact]
    public void Validate_Ipv6MappedPrivate_ReturnsPrivateNetwork()
    {
        var result = UrlSafetyValidator.Validate("http://[::ffff:10.0.0.1]");
        Assert.False(result.IsValid);
        Assert.Equal("PRIVATE_NETWORK", result.ReasonCode);
    }

    // ─── Custom Alias Validation ────────────────────────────────────

    [Fact]
    public void ValidateCustomAlias_Null_ReturnsValid()
    {
        var result = UrlSafetyValidator.ValidateCustomAlias(null);
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("my-alias")]
    [InlineData("my_alias")]
    [InlineData("MyAlias123")]
    [InlineData("a")]
    [InlineData("123456789012345")] // Exactly 15 chars
    public void ValidateCustomAlias_ValidPattern_ReturnsValid(string alias)
    {
        var result = UrlSafetyValidator.ValidateCustomAlias(alias);
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateCustomAlias_EmptyOrWhitespace_ReturnsInvalidAlias(string alias)
    {
        var result = UrlSafetyValidator.ValidateCustomAlias(alias);
        Assert.False(result.IsValid);
        Assert.Equal("INVALID_ALIAS", result.ReasonCode);
    }

    [Fact]
    public void ValidateCustomAlias_TooLong_ReturnsAliasTooLong()
    {
        var result = UrlSafetyValidator.ValidateCustomAlias("1234567890123456"); // 16 chars
        Assert.False(result.IsValid);
        Assert.Equal("ALIAS_TOO_LONG", result.ReasonCode);
    }

    [Theory]
    [InlineData("my alias")]     // spaces
    [InlineData("my.alias")]     // dots
    [InlineData("my@alias")]     // special chars
    [InlineData("my/alias")]     // slashes
    [InlineData("DROP TABLE")]   // SQL injection attempt
    public void ValidateCustomAlias_InvalidCharacters_ReturnsInvalidAliasCharacters(string alias)
    {
        var result = UrlSafetyValidator.ValidateCustomAlias(alias);
        Assert.False(result.IsValid);
        Assert.Equal("INVALID_ALIAS_CHARACTERS", result.ReasonCode);
    }
}
