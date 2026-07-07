namespace AndroidTreeView.App.Services;

/// <summary>
/// Wraps the platform file picker and default-browser launch for the shell and settings view models.
/// </summary>
public interface IFilePickerService
{
    /// <summary>Prompts the user to pick the adb executable; returns its full path or null if cancelled.</summary>
    Task<string?> PickAdbExecutableAsync();

    /// <summary>Opens <paramref name="url"/> in the default browser.</summary>
    Task OpenUrlAsync(string url);
}
