using AndroidTreeView.Core;
using Xunit;

namespace AndroidTreeView.Core.Tests;

public sealed class SemanticVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("V10.0.0", 10, 0, 0)]
    [InlineData("2", 2, 0, 0)]
    [InlineData("2.5", 2, 5, 0)]
    [InlineData("1.2.3+build.99", 1, 2, 3)]
    public void TryParse_ValidInputs_ParsesComponents(string input, int major, int minor, int patch)
    {
        Assert.True(SemanticVersion.TryParse(input, out var version));
        Assert.NotNull(version);
        Assert.Equal(major, version!.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(patch, version.Patch);
        Assert.Null(version.PreRelease);
    }

    [Fact]
    public void TryParse_PreRelease_CapturesSuffix()
    {
        Assert.True(SemanticVersion.TryParse("1.2.3-beta.1", out var version));
        Assert.Equal("beta.1", version!.PreRelease);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("1.2.3.4")]
    [InlineData("x.y.z")]
    public void TryParse_InvalidInputs_ReturnsFalse(string? input)
    {
        Assert.False(SemanticVersion.TryParse(input, out var version));
        Assert.Null(version);
    }

    [Fact]
    public void CompareTo_HigherCoreVersion_IsGreater()
    {
        Assert.True(Parse("2.0.0").IsNewerThan(Parse("1.9.9")));
        Assert.True(Parse("1.2.0").IsNewerThan(Parse("1.1.9")));
        Assert.True(Parse("1.0.1").IsNewerThan(Parse("1.0.0")));
    }

    [Fact]
    public void CompareTo_ReleaseOutranksPreRelease()
    {
        Assert.True(Parse("1.0.0").IsNewerThan(Parse("1.0.0-rc.1")));
        Assert.False(Parse("1.0.0-rc.1").IsNewerThan(Parse("1.0.0")));
    }

    [Fact]
    public void CompareTo_PreReleaseOrdering_IsOrdinal()
    {
        Assert.True(Parse("1.0.0-beta").IsNewerThan(Parse("1.0.0-alpha")));
        Assert.False(Parse("1.0.0-alpha").IsNewerThan(Parse("1.0.0-beta")));
    }

    [Fact]
    public void Equals_SameCoreVersion_IgnoresLeadingV()
    {
        Assert.Equal(Parse("1.0.0"), Parse("v1.0.0"));
        Assert.False(Parse("1.0.0").IsNewerThan(Parse("v1.0.0")));
    }

    private static SemanticVersion Parse(string input)
    {
        Assert.True(SemanticVersion.TryParse(input, out var version));
        return version!;
    }
}
