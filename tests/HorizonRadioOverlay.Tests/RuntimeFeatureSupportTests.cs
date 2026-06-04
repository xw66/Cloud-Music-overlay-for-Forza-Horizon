using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class RuntimeFeatureSupportTests
{
    [Theory]
    [InlineData(true, 17763, true)]
    [InlineData(true, 17762, false)]
    [InlineData(false, 19045, false)]
    public void Evaluates_smtc_support_from_platform_and_build(bool isWindows, int build, bool expected)
    {
        bool result = RuntimeFeatureSupport.EvaluateSmtcSupport(isWindows, build);

        Assert.Equal(expected, result);
    }
}
