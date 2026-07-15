using AndroidTreeView.Models.Rooting;

namespace AndroidTreeView.Adb.Parsers;

/// <summary>Classifies supported firmware containers from file headers and ZIP entry names.</summary>
public static class PackageTypeDetector
{
    private static readonly byte[] ZipHeader = { 0x50, 0x4b };
    private static readonly byte[] PayloadHeader = { 0x43, 0x72, 0x41, 0x55 };

    public static bool IsZipHeader(ReadOnlySpan<byte> header)
        => header.StartsWith(ZipHeader);

    public static bool IsPayloadHeader(ReadOnlySpan<byte> header)
        => header.StartsWith(PayloadHeader);

    public static FirmwarePackageType DetectZipEntries(IEnumerable<string> entryNames)
    {
        ArgumentNullException.ThrowIfNull(entryNames);
        var names = entryNames
            .Select(Normalize)
            .Where(static name => name.Length > 0)
            .ToArray();

        if (names.Any(IsTopLevelBootImage))
        {
            return FirmwarePackageType.PlainZip;
        }

        if (names.Any(IsTopLevelPixelImageZip))
        {
            return FirmwarePackageType.NestedZip;
        }

        if (names.Any(static name => FileName(name).Equals("payload.bin", StringComparison.OrdinalIgnoreCase)))
        {
            return FirmwarePackageType.Payload;
        }

        return FirmwarePackageType.Unknown;
    }

    public static bool IsBootImage(string entryName)
    {
        var fileName = FileName(Normalize(entryName));
        return fileName.Equals("boot.img", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("init_boot.img", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsTopLevelBootImage(string entryName)
    {
        var normalized = Normalize(entryName);
        return !normalized.Contains('/') && IsBootImage(normalized);
    }

    public static bool IsTopLevelPixelImageZip(string entryName)
    {
        var normalized = Normalize(entryName);
        var fileName = FileName(normalized);
        return !normalized.Contains('/')
            && fileName.StartsWith("image-", StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string value) => value.Replace('\\', '/').Trim('/');

    private static string FileName(string value)
    {
        var slash = value.LastIndexOf('/');
        return slash < 0 ? value : value[(slash + 1)..];
    }
}
