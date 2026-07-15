using System.Buffers.Binary;

namespace AndroidTreeView.Adb.Parsers;

/// <summary>Reads ramdisk evidence from standard Android boot image headers (v0-v4).</summary>
public static class AndroidBootImageHeaderParser
{
    public const int MaximumHeaderSize = 1660;

    private const int BootHeaderV0Size = 1632;
    private const int BootHeaderV1Size = 1648;
    private const int BootHeaderV2Size = 1660;
    private const int BootHeaderV3Size = 1580;
    private const int BootHeaderV4Size = 1584;
    private const uint ModernPageSize = 4096;
    private static ReadOnlySpan<byte> BootMagic => "ANDROID!"u8;

    public static BootRamdiskEvidence Parse(ReadOnlySpan<byte> header, long fileLength)
    {
        if (fileLength < 0 || header.Length < 44 || !header[..BootMagic.Length].SequenceEqual(BootMagic))
        {
            return BootRamdiskEvidence.Unknown;
        }

        var headerVersion = ReadUInt32(header, 40);
        return headerVersion switch
        {
            <= 2 => ParseLegacy(header, fileLength, headerVersion),
            3 => ParseModern(header, fileLength, headerVersion, BootHeaderV3Size),
            4 => ParseModern(header, fileLength, headerVersion, BootHeaderV4Size),
            _ => BootRamdiskEvidence.Unknown
        };
    }

    private static BootRamdiskEvidence ParseLegacy(
        ReadOnlySpan<byte> header,
        long fileLength,
        uint headerVersion)
    {
        var requiredHeaderSize = headerVersion switch
        {
            0 => BootHeaderV0Size,
            1 => BootHeaderV1Size,
            _ => BootHeaderV2Size
        };
        if (header.Length < requiredHeaderSize)
        {
            return BootRamdiskEvidence.Unknown;
        }

        if (headerVersion >= 1 && ReadUInt32(header, 1644) != requiredHeaderSize)
        {
            return BootRamdiskEvidence.Unknown;
        }

        var pageSize = ReadUInt32(header, 36);
        if (pageSize is < 2048 or > 65536 || !IsPowerOfTwo(pageSize))
        {
            return BootRamdiskEvidence.Unknown;
        }

        var kernelSize = ReadUInt32(header, 8);
        var ramdiskSize = ReadUInt32(header, 16);
        if (!TryAdd(pageSize, Align(kernelSize, pageSize), out var ramdiskOffset)
            || !TryAdd(ramdiskOffset, ramdiskSize, out var requiredLength)
            || requiredLength > (ulong)fileLength)
        {
            return BootRamdiskEvidence.Unknown;
        }

        return ramdiskSize == 0 ? BootRamdiskEvidence.Absent : BootRamdiskEvidence.Present;
    }

    private static BootRamdiskEvidence ParseModern(
        ReadOnlySpan<byte> header,
        long fileLength,
        uint headerVersion,
        int requiredHeaderSize)
    {
        if (header.Length < requiredHeaderSize
            || ReadUInt32(header, 20) != requiredHeaderSize
            || ReadUInt32(header, 40) != headerVersion)
        {
            return BootRamdiskEvidence.Unknown;
        }

        var kernelSize = ReadUInt32(header, 8);
        var ramdiskSize = ReadUInt32(header, 12);
        if (!TryAdd(ModernPageSize, Align(kernelSize, ModernPageSize), out var ramdiskOffset)
            || !TryAdd(ramdiskOffset, ramdiskSize, out var requiredLength)
            || requiredLength > (ulong)fileLength)
        {
            return BootRamdiskEvidence.Unknown;
        }

        return ramdiskSize == 0 ? BootRamdiskEvidence.Absent : BootRamdiskEvidence.Present;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> value, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(value.Slice(offset, sizeof(uint)));

    private static bool IsPowerOfTwo(uint value) => (value & (value - 1)) == 0;

    private static ulong Align(uint value, uint alignment)
        => ((ulong)value + alignment - 1) / alignment * alignment;

    private static bool TryAdd(ulong left, ulong right, out ulong result)
    {
        result = left + right;
        return result >= left;
    }
}

public enum BootRamdiskEvidence
{
    Unknown = 0,
    Present = 1,
    Absent = 2
}
