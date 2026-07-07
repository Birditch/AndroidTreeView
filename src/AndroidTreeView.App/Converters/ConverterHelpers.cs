namespace AndroidTreeView.App.Converters;

/// <summary>Shared helpers for the app's value converters.</summary>
internal static class ConverterHelpers
{
    /// <summary>
    /// True when the converter parameter requests inverted output
    /// (<c>invert</c>, <c>inverse</c> or <c>!</c>, case-insensitive).
    /// </summary>
    public static bool IsInvert(object? parameter) =>
        parameter is string s &&
        (s.Equals("invert", StringComparison.OrdinalIgnoreCase) ||
         s.Equals("inverse", StringComparison.OrdinalIgnoreCase) ||
         s == "!");

    /// <summary>Case-insensitive comparison of a value's name against a converter parameter's name.</summary>
    public static bool NameEquals(object? value, object? parameter)
    {
        if (value is null || parameter is null)
        {
            return false;
        }

        return string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
