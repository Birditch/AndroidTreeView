using AndroidTreeView.Core.Interfaces;

namespace AndroidTreeView.App.ViewModels;

internal static class UnlockStateFormatter
{
    public static string FormatOemUnlockAllowed(
        bool? oemUnlockAllowed,
        string? bootloaderLockState,
        string? deviceState,
        string? verifiedBootState,
        ILocalizationService localization)
    {
        if (IsUnlocked(bootloaderLockState) || IsUnlocked(deviceState) || IsUnlocked(verifiedBootState))
        {
            return localization.Get("state.unlocked");
        }

        return FormatNullableBool(oemUnlockAllowed, localization);
    }

    public static string FormatBootloaderLock(
        string? bootloaderLockState,
        string? deviceState,
        string? verifiedBootState,
        ILocalizationService localization)
    {
        var state = string.IsNullOrWhiteSpace(bootloaderLockState)
            ? IsUnlocked(verifiedBootState) ? "unlocked" : deviceState
            : bootloaderLockState;

        return FormatState(state, localization);
    }

    public static string FormatNullableBool(bool? value, ILocalizationService localization) => value switch
    {
        true => localization.Get("common.yes"),
        false => localization.Get("common.no"),
        _ => localization.Get("common.unavailable")
    };

    public static string FormatState(string? value, ILocalizationService localization)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return localization.Get("common.unavailable");
        }

        return Normalize(value) switch
        {
            "locked" or "flashing_locked" => localization.Get("state.locked"),
            "unlocked" or "flashing_unlocked" => localization.Get("state.unlocked"),
            "green" => localization.Get("state.green"),
            "yellow" => localization.Get("state.yellow"),
            "orange" => localization.Get("state.orange"),
            "red" => localization.Get("state.red"),
            var state => state
        };
    }

    private static bool IsUnlocked(string? value) =>
        Normalize(value) is "unlocked" or "flashing_unlocked" or "orange";

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
}
