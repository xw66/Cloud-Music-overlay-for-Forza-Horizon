using System.Reflection;

namespace HorizonRadioOverlay.Tests;

public sealed class AssemblyVersionTests
{
    [Fact]
    public void Main_assembly_version_matches_release_version()
    {
        Version? version = Assembly.Load("HorizonRadioOverlay").GetName().Version;

        Assert.Equal(new Version(3, 0, 1, 0), version);
    }
}
