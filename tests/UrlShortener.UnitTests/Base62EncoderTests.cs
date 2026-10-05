using UrlShortener.Core.Encoding;

namespace UrlShortener.UnitTests;

/// <summary>
/// Unit tests for <see cref="Base62Encoder"/>.
/// Covers: zero value, small known values, maximum ulong, determinism,
/// and boundary conditions at Base62 digit boundaries.
/// </summary>
public class Base62EncoderTests
{
    /// <summary>
    /// ID 0 must map to the first character in the alphabet ("0").
    /// </summary>
    [Fact]
    public void Encode_Zero_Returns_Zero()
    {
        var result = Base62Encoder.Encode(0);

        Assert.Equal("0", result);
    }

    /// <summary>
    /// Validates that single-digit Base62 values (0–61) map to the
    /// correct character in the alphabet 0-9a-zA-Z.
    /// </summary>
    [Theory]
    [InlineData(0UL,  "0")]
    [InlineData(1UL,  "1")]
    [InlineData(9UL,  "9")]
    [InlineData(10UL, "a")]   // First lowercase letter
    [InlineData(35UL, "z")]   // Last lowercase letter
    [InlineData(36UL, "A")]   // First uppercase letter
    [InlineData(61UL, "Z")]   // Last single-digit Base62 character
    public void Encode_SingleDigitValues_ReturnsExpectedCharacter(ulong input, string expected)
    {
        var result = Base62Encoder.Encode(input);

        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Validates multi-digit known values at Base62 digit boundaries.
    /// 62 in Base62 = "10" (1×62 + 0).
    /// 3844 in Base62 = "100" (1×62² + 0×62 + 0).
    /// </summary>
    [Theory]
    [InlineData(62UL,   "10")]   // First two-digit number
    [InlineData(63UL,   "11")]
    [InlineData(3844UL, "100")]  // First three-digit number (62²)
    public void Encode_MultiDigitValues_ReturnsExpectedString(ulong input, string expected)
    {
        var result = Base62Encoder.Encode(input);

        Assert.Equal(expected, result);
    }

    /// <summary>
    /// ulong.MaxValue (18446744073709551615) requires exactly 11 Base62 characters.
    /// This validates the stackalloc char[11] buffer sizing is correct and sufficient.
    /// </summary>
    [Fact]
    public void Encode_MaxUlong_Returns11Characters()
    {
        var result = Base62Encoder.Encode(ulong.MaxValue);

        Assert.Equal(11, result.Length);
        // Every character must be within the Base62 alphabet
        Assert.All(result, c =>
            Assert.True(
                char.IsAsciiDigit(c) || char.IsAsciiLetterLower(c) || char.IsAsciiLetterUpper(c),
                $"Character '{c}' is not in the Base62 alphabet."));
    }

    /// <summary>
    /// The encoder must be deterministic: the same input must always produce
    /// the same output. This is critical for URL resolution.
    /// </summary>
    [Fact]
    public void Encode_IsDeterministic_SameInputProducesSameOutput()
    {
        const ulong testValue = 123456789UL;

        var result1 = Base62Encoder.Encode(testValue);
        var result2 = Base62Encoder.Encode(testValue);

        Assert.Equal(result1, result2);
    }

    /// <summary>
    /// Different inputs must produce different outputs (injectivity).
    /// Base62 encoding of a unique sequence is collision-free by design.
    /// </summary>
    [Fact]
    public void Encode_DifferentInputs_ProduceDifferentOutputs()
    {
        var result1 = Base62Encoder.Encode(1);
        var result2 = Base62Encoder.Encode(2);

        Assert.NotEqual(result1, result2);
    }

    /// <summary>
    /// Validates a known computed value to guard against regression.
    /// Verified against the encoder: Encode(123456789) = "8m0Kx".
    /// </summary>
    [Fact]
    public void Encode_KnownValue_MatchesExpectedBase62String()
    {
        var result = Base62Encoder.Encode(123456789UL);

        Assert.Equal("8m0Kx", result);
    }
}
