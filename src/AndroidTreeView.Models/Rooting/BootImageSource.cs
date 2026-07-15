namespace AndroidTreeView.Models.Rooting;

/// <summary>Physical source from which a boot image was extracted.</summary>
public enum BootImageSource
{
    Unknown = 0,
    PlainZip = 1,
    NestedZip = 2,
    Payload = 3
}

/// <summary>Supported firmware package containers.</summary>
public enum FirmwarePackageType
{
    Unknown = 0,
    PlainZip = 1,
    NestedZip = 2,
    Payload = 3
}
