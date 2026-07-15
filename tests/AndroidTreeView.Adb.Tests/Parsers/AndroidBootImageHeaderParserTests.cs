using System.Buffers.Binary;
using AndroidTreeView.Adb.Parsers;
using Xunit;

namespace AndroidTreeView.Adb.Tests.Parsers;

public sealed class AndroidBootImageHeaderParserTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Parse_StandardHeaderWithRamdisk_ReturnsPresent(uint version)
    {
        var image = CreateImage(version, ramdiskSize: 17);

        var result = AndroidBootImageHeaderParser.Parse(image, image.Length);

        Assert.Equal(BootRamdiskEvidence.Present, result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Parse_StandardHeaderWithoutRamdisk_ReturnsAbsent(uint version)
    {
        var image = CreateImage(version, ramdiskSize: 0);

        var result = AndroidBootImageHeaderParser.Parse(image, image.Length);

        Assert.Equal(BootRamdiskEvidence.Absent, result);
    }

    [Fact]
    public void Parse_InvalidMagic_ReturnsUnknown()
    {
        var image = CreateImage(4, ramdiskSize: 1);
        image[0] = (byte)'X';

        Assert.Equal(BootRamdiskEvidence.Unknown, AndroidBootImageHeaderParser.Parse(image, image.Length));
    }

    [Fact]
    public void Parse_UnknownVersion_ReturnsUnknown()
    {
        var image = CreateImage(4, ramdiskSize: 1);
        Write(image, 40, 5);

        Assert.Equal(BootRamdiskEvidence.Unknown, AndroidBootImageHeaderParser.Parse(image, image.Length));
    }

    [Fact]
    public void Parse_TruncatedHeader_ReturnsUnknown()
    {
        var image = CreateImage(2, ramdiskSize: 1);

        Assert.Equal(BootRamdiskEvidence.Unknown,
            AndroidBootImageHeaderParser.Parse(image.AsSpan(0, 100), image.Length));
    }

    [Fact]
    public void Parse_RamdiskOutsideFile_ReturnsUnknown()
    {
        var image = CreateImage(3, ramdiskSize: 17);

        Assert.Equal(BootRamdiskEvidence.Unknown,
            AndroidBootImageHeaderParser.Parse(image, image.Length - 17));
    }

    internal static byte[] CreateImage(uint version, uint ramdiskSize, uint kernelSize = 1)
    {
        const uint pageSize = 4096;
        var headerSize = version switch
        {
            0 => 1632u,
            1 => 1648u,
            2 => 1660u,
            3 => 1580u,
            4 => 1584u,
            _ => throw new ArgumentOutOfRangeException(nameof(version))
        };
        var alignedKernel = ((ulong)kernelSize + pageSize - 1) / pageSize * pageSize;
        var imageLength = checked((int)((ulong)pageSize + alignedKernel + ramdiskSize));
        var image = new byte[imageLength];
        "ANDROID!"u8.CopyTo(image);
        Write(image, 8, kernelSize);
        Write(image, 40, version);
        if (version <= 2)
        {
            Write(image, 16, ramdiskSize);
            Write(image, 36, pageSize);
            if (version >= 1)
            {
                Write(image, 1644, headerSize);
            }
        }
        else
        {
            Write(image, 12, ramdiskSize);
            Write(image, 20, headerSize);
        }

        return image;
    }

    private static void Write(Span<byte> destination, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(offset, sizeof(uint)), value);
}
