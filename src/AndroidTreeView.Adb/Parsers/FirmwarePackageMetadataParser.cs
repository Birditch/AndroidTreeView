using AndroidTreeView.Models.Rooting;

namespace AndroidTreeView.Adb.Parsers;

/// <summary>Parses Android OTA and Pixel factory package identity metadata.</summary>
public static class FirmwarePackageMetadataParser
{
    public static FirmwarePackageMetadata Parse(
        string packagePath,
        FirmwarePackageType packageType,
        RootDeviceIdentity device,
        string? otaMetadata,
        string? androidInfo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentNullException.ThrowIfNull(device);

        var products = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var devices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ParseOtaMetadata(otaMetadata, products, devices);
        ParseAndroidInfo(androidInfo, products, devices);

        var declared = products.Concat(devices).ToArray();
        var targetValues = new[] { device.Product, device.Device }
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var matching = declared
            .Where(value => targetValues.Contains(value, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var status = declared.Length == 0 || targetValues.Length == 0
            ? FirmwarePackageMatchStatus.Unverified
            : matching.Length > 0
                ? FirmwarePackageMatchStatus.Matched
                : FirmwarePackageMatchStatus.Mismatched;

        return new FirmwarePackageMetadata
        {
            PackagePath = Path.GetFullPath(packagePath),
            OriginalPackageName = Path.GetFileName(packagePath),
            PackageType = packageType,
            DeclaredProducts = products.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            DeclaredDevices = devices.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            MatchStatus = status,
            MatchingValues = matching
        };
    }

    private static void ParseOtaMetadata(
        string? text,
        ISet<string> products,
        ISet<string> devices)
    {
        foreach (var (key, value) in ParseKeyValues(text))
        {
            if (key.Equals("pre-device", StringComparison.OrdinalIgnoreCase))
            {
                AddAlternatives(value, devices);
                continue;
            }

            if (key.Equals("post-build", StringComparison.OrdinalIgnoreCase))
            {
                var fingerprintHead = value.Split(':', 2)[0];
                var parts = fingerprintHead.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length >= 3)
                {
                    products.Add(parts[1]);
                    devices.Add(parts[2]);
                }
            }
        }
    }

    private static void ParseAndroidInfo(
        string? text,
        ISet<string> products,
        ISet<string> devices)
    {
        foreach (var raw in (text ?? string.Empty).Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("require ", StringComparison.OrdinalIgnoreCase))
            {
                line = line["require ".Length..].Trim();
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            // "board" is a hardware platform (for example slider) and is not comparable with
            // ro.product.device/product (for example oriole), so it is intentionally ignored.
            if (key.Equals("product", StringComparison.OrdinalIgnoreCase))
            {
                AddAlternatives(value, products);
            }
            else if (key.Equals("device", StringComparison.OrdinalIgnoreCase))
            {
                AddAlternatives(value, devices);
            }
        }
    }

    private static IEnumerable<(string Key, string Value)> ParseKeyValues(string? text)
    {
        foreach (var raw in (text ?? string.Empty).Split('\n'))
        {
            var separator = raw.IndexOf('=');
            if (separator > 0)
            {
                yield return (raw[..separator].Trim(), raw[(separator + 1)..].Trim());
            }
        }
    }

    private static void AddAlternatives(string value, ISet<string> destination)
    {
        foreach (var item in value.Split(new[] { '|', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            destination.Add(item);
        }
    }
}
