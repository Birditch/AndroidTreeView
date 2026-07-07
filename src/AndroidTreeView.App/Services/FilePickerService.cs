using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.Logging;

namespace AndroidTreeView.App.Services;

/// <summary>
/// Uses the main window's <see cref="IStorageProvider"/> to pick the adb executable and launches URLs
/// through the OS shell. Picking must run on the UI thread (invoked from view-model commands).
/// </summary>
public sealed class FilePickerService : IFilePickerService
{
    private readonly ILogger<FilePickerService> _logger;

    public FilePickerService(ILogger<FilePickerService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string?> PickAdbExecutableAsync()
    {
        var window = GetMainWindow();
        if (window?.StorageProvider is not { CanOpen: true } provider)
        {
            _logger.LogWarning("No storage provider is available for the file picker.");
            return null;
        }

        var options = new FilePickerOpenOptions
        {
            Title = "Select adb executable",
            AllowMultiple = false,
            FileTypeFilter = BuildFilters()
        };

        var files = await provider.OpenFilePickerAsync(options).ConfigureAwait(true);
        if (files.Count == 0)
        {
            return null;
        }

        return files[0].TryGetLocalPath();
    }

    /// <inheritdoc />
    public Task OpenUrlAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return Task.CompletedTask;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open URL {Url}.", url);
        }

        return Task.CompletedTask;
    }

    private static IReadOnlyList<FilePickerFileType> BuildFilters()
    {
        if (OperatingSystem.IsWindows())
        {
            return new[]
            {
                new FilePickerFileType("adb") { Patterns = new[] { "adb.exe" } },
                new FilePickerFileType("Executables") { Patterns = new[] { "*.exe" } },
                FilePickerFileTypes.All
            };
        }

        return new[]
        {
            new FilePickerFileType("adb") { Patterns = new[] { "adb" } },
            FilePickerFileTypes.All
        };
    }

    private static Window? GetMainWindow() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
