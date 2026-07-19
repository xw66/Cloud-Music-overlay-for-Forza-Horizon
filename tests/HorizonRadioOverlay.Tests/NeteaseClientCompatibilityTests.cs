using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class NeteaseClientCompatibilityTests
{
    [Fact]
    public void AcceptsVerifiedVersionAndHash()
    {
        Assert.True(NeteaseClientCompatibility.IsSupported(
            NeteaseClientCompatibility.SupportedProductVersion,
            NeteaseClientCompatibility.SupportedExecutableSha256.ToLowerInvariant()));
    }

    [Theory]
    [InlineData("3.1.37.0", "1B86292DA1056A729226DF9205EB9F69C1E5558BC3EDDBE0639C06A2443BF2D3")]
    [InlineData("3.1.36.205322", "BAD")]
    [InlineData(null, null)]
    public void RejectsUnknownClient(string? version, string? hash)
    {
        Assert.False(NeteaseClientCompatibility.IsSupported(version, hash));
    }
}
