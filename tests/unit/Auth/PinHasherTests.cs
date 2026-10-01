using Microsoft.Extensions.Options;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Infrastructure.Auth;
using Xunit;

namespace RestaurantOrder.UnitTests.Auth;

public class PinHasherTests
{
    private const string TestPepper = "test_super_secret_pepper_value_12345";
    private readonly PinHasherOptions _options = new()
    {
        PepperKeyId = "v1",
        PepperValue = TestPepper,
        Environment = "development"
    };

    [Fact]
    public void HashPin_ValidFourDigitPin_ReturnsHashWithMetadata()
    {
        var hasher = new PepperedPinHasher(Options.Create(_options));
        var result = hasher.HashPin("1234");

        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.Hash));
        Assert.Equal(PepperedPinHasher.AlgorithmVersion, result.AlgorithmVersion);
        Assert.Equal("v1", result.PepperKeyId);
        Assert.StartsWith("pbkdf2_sha512_v1$", result.Hash);
    }

    [Fact]
    public void VerifyPin_CorrectPinAndPepper_ReturnsSuccess()
    {
        var hasher = new PepperedPinHasher(Options.Create(_options));
        var hashResult = hasher.HashPin("4321");

        var verification = hasher.VerifyPin("4321", hashResult.Hash, hashResult.PepperKeyId);

        Assert.Equal(PinVerificationResult.Success, verification);
    }

    [Fact]
    public void VerifyPin_WrongPin_ReturnsFailed()
    {
        var hasher = new PepperedPinHasher(Options.Create(_options));
        var hashResult = hasher.HashPin("4321");

        var verification = hasher.VerifyPin("9999", hashResult.Hash, hashResult.PepperKeyId);

        Assert.Equal(PinVerificationResult.Failed, verification);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("12")]
    [InlineData("123")]
    [InlineData("12345")]
    [InlineData("abcd")]
    [InlineData("12a4")]
    [InlineData("")]
    [InlineData("   ")]
    public void HashPin_InvalidPinFormat_ThrowsArgumentException(string invalidPin)
    {
        var hasher = new PepperedPinHasher(Options.Create(_options));
        Assert.Throws<ArgumentException>(() => hasher.HashPin(invalidPin));
    }

    [Fact]
    public void VerifyPin_DifferentPepper_ReturnsFailed()
    {
        var hasher1 = new PepperedPinHasher(Options.Create(_options));
        var hashResult = hasher1.HashPin("1234");

        var differentPepperOptions = new PinHasherOptions
        {
            PepperKeyId = "v1",
            PepperValue = "completely_different_pepper_string_98765",
            Environment = "development"
        };
        var hasher2 = new PepperedPinHasher(Options.Create(differentPepperOptions));

        var verification = hasher2.VerifyPin("1234", hashResult.Hash, hashResult.PepperKeyId);

        Assert.Equal(PinVerificationResult.Failed, verification);
    }

    [Fact]
    public void VerifyPin_DifferentPepperKeyId_ReturnsSuccessRehashNeeded()
    {
        var hasher1 = new PepperedPinHasher(Options.Create(_options));
        var hashResult = hasher1.HashPin("1234");

        // System rotated pepper key id to "v2", but old pepper value still verifies
        var rotatedOptions = new PinHasherOptions
        {
            PepperKeyId = "v2",
            PepperValue = TestPepper,
            Environment = "development"
        };
        var hasher2 = new PepperedPinHasher(Options.Create(rotatedOptions));

        var verification = hasher2.VerifyPin("1234", hashResult.Hash, "v1");

        Assert.Equal(PinVerificationResult.SuccessRehashNeeded, verification);
    }

    [Theory]
    [InlineData("production", "")]
    [InlineData("production", "short_pepper")]
    [InlineData("staging", "")]
    [InlineData("staging", "too_short")]
    public void PinHasherOptions_ProductionOrStagingWithoutPepper_ThrowsInvalidOperationException(
        string environment,
        string pepperValue)
    {
        var options = new PinHasherOptions
        {
            Environment = environment,
            PepperValue = pepperValue
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void PinHasherOptions_ProductionWithSufficientPepper_ValidatesSuccessfully()
    {
        var options = new PinHasherOptions
        {
            Environment = "production",
            PepperValue = "a_very_strong_production_pepper_secret_123"
        };

        options.Validate(); // Does not throw
    }
}
