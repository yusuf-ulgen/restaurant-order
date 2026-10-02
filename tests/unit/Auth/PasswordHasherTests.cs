using RestaurantOrder.Application.Auth;
using RestaurantOrder.Infrastructure.Auth;
using Xunit;

namespace RestaurantOrder.UnitTests.Auth;

public class PasswordHasherTests
{
    private readonly IPasswordHasher _hasher = new AspNetCorePasswordHasher();

    [Fact]
    public void HashPassword_ValidPassword_ReturnsValidHashWithAlgorithmVersion()
    {
        var password = "SecureP@ssword123!";
        var result = _hasher.HashPassword(password);

        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.Hash));
        Assert.Equal(AspNetCorePasswordHasher.CurrentAlgorithmVersion, result.AlgorithmVersion);
    }

    [Fact]
    public void VerifyPassword_CorrectPassword_ReturnsSuccess()
    {
        var password = "CorrectHorseBatteryStaple!";
        var result = _hasher.HashPassword(password);

        var verification = _hasher.VerifyPassword(password, result.Hash);

        Assert.Equal(PasswordVerificationResult.Success, verification);
    }

    [Fact]
    public void VerifyPassword_WrongPassword_ReturnsFailed()
    {
        var password = "CorrectPassword123!";
        var result = _hasher.HashPassword(password);

        var verification = _hasher.VerifyPassword("WrongPassword456!", result.Hash);

        Assert.Equal(PasswordVerificationResult.Failed, verification);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void HashPassword_EmptyOrNullPassword_ThrowsArgumentException(string? invalidPassword)
    {
        Assert.Throws<ArgumentException>(() => _hasher.HashPassword(invalidPassword!));
    }

    [Theory]
    [InlineData("", "valid_hash")]
    [InlineData("password", "")]
    [InlineData("password", "   ")]
    [InlineData(null, "valid_hash")]
    [InlineData("password", null)]
    [InlineData("password", "invalid_garbage_hash")]
    public void VerifyPassword_InvalidInputs_ReturnsFailed(string? password, string? hash)
    {
        var verification = _hasher.VerifyPassword(password!, hash!);
        Assert.Equal(PasswordVerificationResult.Failed, verification);
    }
}
