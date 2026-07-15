using AndroidTreeView.Adb.Parsers;
using Xunit;

namespace AndroidTreeView.Adb.Tests.Parsers;

public sealed class CpuAbiParserTests
{
    [Theory]
    [InlineData("arm64-v8a\n", "arm64-v8a")]
    [InlineData("x86_64,armeabi-v7a", "x86_64")]
    [InlineData("mips", null)]
    public void Parse_NormalizesSupportedAbi(string output, string? expected)
        => Assert.Equal(expected, CpuAbiParser.Parse(output));
}
