namespace AndroidTreeView.Adb.Commands;

/// <summary>
/// Centralized <c>getprop</c> key names. Keeping these in one place avoids magic strings
/// scattered across parsers, builders and services.
/// </summary>
public static class PropKeys
{
    // Identity / product
    public const string Manufacturer = "ro.product.manufacturer";
    public const string Brand = "ro.product.brand";
    public const string Model = "ro.product.model";
    public const string ProductName = "ro.product.name";
    public const string Device = "ro.product.device";
    public const string SerialNo = "ro.serialno";
    public const string BootSerialNo = "ro.boot.serialno";

    // Build / version
    public const string AndroidVersion = "ro.build.version.release";
    public const string Sdk = "ro.build.version.sdk";
    public const string BuildDisplayId = "ro.build.display.id";
    public const string Fingerprint = "ro.build.fingerprint";
    public const string SecurityPatch = "ro.build.version.security_patch";
    public const string BuildTags = "ro.build.tags";
    public const string BuildType = "ro.build.type";

    // Hardware / board
    public const string BoardPlatform = "ro.board.platform";
    public const string ProductBoard = "ro.product.board";
    public const string AbiList = "ro.product.cpu.abilist";

    // Locale / timezone
    public const string LocalePersist = "persist.sys.locale";
    public const string LocaleProduct = "ro.product.locale";
    public const string Timezone = "persist.sys.timezone";

    // Boot / verified boot
    public const string Bootloader = "ro.bootloader";
    public const string VerifiedBootState = "ro.boot.verifiedbootstate";

    // Root / Magisk hints (best-effort; not always present)
    public const string MagiskVersionProp = "ro.magisk.version";
    public const string MagiskServiceHint = "init.svc.magisk";

    /// <summary>Ordered locale keys to consult (persisted locale wins over the product default).</summary>
    public static readonly string[] LocaleKeys = { LocalePersist, LocaleProduct };

    /// <summary>Ordered serial keys to consult.</summary>
    public static readonly string[] SerialKeys = { SerialNo, BootSerialNo };
}
