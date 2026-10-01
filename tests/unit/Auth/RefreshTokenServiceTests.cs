using RestaurantOrder.Infrastructure.Auth;
using Xunit;

namespace RestaurantOrder.UnitTests.Auth;

public class RefreshTokenServiceTests
{
    private readonly RefreshTokenService _service = new();

    [Fact]
    public void GenerateOpaqueToken_Produces64HexCharsWithHighEntropy()
    {
        var token1 = _service.GenerateOpaqueToken();
        var token2 = _service.GenerateOpaqueToken();

        Assert.Equal(64, token1.Length);
        Assert.Equal(64, token2.Length);
        Assert.NotEqual(token1, token2);
    }

    [Fact]
    public void HashToken_IsDeterministicAndProduces64HexChars()
    {
        var raw = _service.GenerateOpaqueToken();
        var hash1 = _service.HashToken(raw);
        var hash2 = _service.HashToken(raw);

        Assert.Equal(hash1, hash2);
        Assert.Equal(64, hash1.Length);
        Assert.NotEqual(raw, hash1);
    }

    [Fact]
    public void HashToken_WithEmptyString_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => _service.HashToken(string.Empty));
    }
}
