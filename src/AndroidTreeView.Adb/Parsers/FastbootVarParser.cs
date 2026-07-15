using AndroidTreeView.Models.Rooting;

namespace AndroidTreeView.Adb.Parsers;

/// <summary>Pure parsers for fastboot variable and long device-list output.</summary>
public static class FastbootVarParser
{
    public static IReadOnlyDictionary<string, string> ParseVariables(string? output)
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in (output ?? string.Empty).Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("(bootloader)", StringComparison.OrdinalIgnoreCase))
            {
                line = line["(bootloader)".Length..].Trim();
            }

            var separator = line.LastIndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (key.Length == 0
                || key.Equals("all", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("finished", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("total time", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            variables[key] = value;
        }

        return variables;
    }

    public static string? ParseValue(string? output, string variable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variable);
        var variables = ParseVariables(output);
        return variables.TryGetValue(variable, out var value) ? value : null;
    }

    public static bool? ParseBoolean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "yes" or "true" or "1" => true,
            "no" or "false" or "0" => false,
            _ => null
        };
    }

    public static IReadOnlyList<FastbootDeviceIdentity> ParseDevices(string? output)
    {
        var devices = new List<FastbootDeviceIdentity>();
        var seenSerials = new HashSet<string>(StringComparer.Ordinal);

        foreach (var raw in (output ?? string.Empty).Split('\n'))
        {
            var fields = raw.Trim().Split(
                new[] { ' ', '\t' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (fields.Length < 2
                || !fields[1].Equals("fastboot", StringComparison.OrdinalIgnoreCase)
                || !seenSerials.Add(fields[0]))
            {
                continue;
            }

            var descriptors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 2; i < fields.Length; i++)
            {
                var separator = fields[i].IndexOf(':');
                if (separator > 0 && separator < fields[i].Length - 1)
                {
                    descriptors[fields[i][..separator]] = fields[i][(separator + 1)..];
                }
            }

            descriptors.TryGetValue("usb", out var usbPath);
            descriptors.TryGetValue("product", out var product);
            devices.Add(new FastbootDeviceIdentity
            {
                Serial = fields[0],
                UsbPath = usbPath,
                Product = product,
                Descriptors = descriptors
            });
        }

        return devices;
    }

    public static FastbootIdentityMatch MatchIdentity(
        RootDeviceIdentity target,
        IReadOnlyList<FastbootDeviceIdentity> candidates)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(candidates);

        var matches = new List<FastbootIdentityMatch>();
        var conflictFound = false;
        foreach (var candidate in candidates)
        {
            var serialMatches = EqualsValue(target.Serial, candidate.Serial);
            var usbMatches = UsbPathEquals(target.UsbPath, candidate.UsbPath);
            var productMatches = EqualsValue(target.Product, candidate.Product)
                || EqualsValue(target.Device, candidate.Product);
            var usbConflicts = UsbPathConflicts(target.UsbPath, candidate.UsbPath);
            var productConflicts = HasProductIdentity(target) && !string.IsNullOrWhiteSpace(candidate.Product)
                && !productMatches;

            // ADB and bootloader product names are not guaranteed to use the same namespace. A stable
            // serial remains primary evidence, while a conflicting physical USB path still blocks it.
            if ((serialMatches && usbConflicts)
                || (!serialMatches && usbMatches && productConflicts))
            {
                conflictFound = true;
                continue;
            }

            var evidence = FastbootIdentityEvidence.None;
            if (serialMatches)
            {
                evidence |= FastbootIdentityEvidence.Serial;
            }

            if (usbMatches)
            {
                evidence |= FastbootIdentityEvidence.UsbPath;
            }

            if (productMatches)
            {
                evidence |= FastbootIdentityEvidence.Product;
            }

            // A stable serial is sufficient if no available field conflicts. When fastboot changes the
            // serial, require independent USB and product evidence together.
            if (serialMatches || (usbMatches && productMatches))
            {
                matches.Add(new FastbootIdentityMatch
                {
                    Status = FastbootIdentityMatchStatus.Verified,
                    Device = candidate,
                    Evidence = evidence
                });
            }
        }

        if (matches.Count == 1)
        {
            return matches[0];
        }

        if (matches.Count > 1)
        {
            return new FastbootIdentityMatch { Status = FastbootIdentityMatchStatus.Ambiguous };
        }

        return new FastbootIdentityMatch
        {
            Status = conflictFound
                ? FastbootIdentityMatchStatus.ConflictingEvidence
                : FastbootIdentityMatchStatus.Unverified
        };
    }

    private static bool HasProductIdentity(RootDeviceIdentity target)
        => !string.IsNullOrWhiteSpace(target.Product) || !string.IsNullOrWhiteSpace(target.Device);

    private static bool UsbPathConflicts(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left)
            && !string.IsNullOrWhiteSpace(right)
            && !UsbPathEquals(left, right);

    private static bool UsbPathEquals(string? left, string? right)
    {
        if (EqualsValue(left, right))
        {
            return true;
        }

        return TryParseMacUsbLocation(left, out var leftLocation)
            && TryParseMacUsbLocation(right, out var rightLocation)
            && leftLocation == rightLocation;
    }

    private static bool TryParseMacUsbLocation(string? value, out uint location)
    {
        location = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        if (text.EndsWith('X')
            && uint.TryParse(text[..^1], out location))
        {
            return true;
        }

        var separator = text.IndexOf('-');
        if (separator <= 0
            || !byte.TryParse(text[..separator], out var bus))
        {
            return false;
        }

        var ports = text[(separator + 1)..].Split('.');
        if (ports.Length is 0 or > 6)
        {
            return false;
        }

        location = (uint)bus << 24;
        for (var i = 0; i < ports.Length; i++)
        {
            if (!byte.TryParse(ports[i], out var port) || port is 0 or > 15)
            {
                location = 0;
                return false;
            }

            location |= (uint)port << (20 - (i * 4));
        }

        return true;
    }

    private static bool EqualsValue(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left)
            && !string.IsNullOrWhiteSpace(right)
            && left.Trim().Equals(right.Trim(), StringComparison.OrdinalIgnoreCase);
}
