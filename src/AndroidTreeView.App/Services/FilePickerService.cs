using System.Diagnostics;
using AndroidTreeView.Core.Interfaces;
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
    private readonly ILocalizationService _localization;

    public FilePickerService(ILogger<FilePickerService> logger, ILocalizationService localization)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
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
            Title = _localization.Get("picker.adb.title"),
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
    public Task<IReadOnlyList<string>> PickTransferFilesAsync() =>
        PickFilesAsync(
            _localization.Get("picker.transfer.title"),
            [
                new FilePickerFileType(_localization.Get("picker.type.apk")) { Patterns = ["*.apk"] },
                FilePickerFileTypes.All
            ]);

    /// <inheritdoc />
    public async Task<string?> PickRootPackageAsync()
    {
        var window = GetMainWindow();
        if (window?.StorageProvider is not { CanOpen: true } provider)
        {
            _logger.LogWarning("No storage provider is available for the root package picker.");
            return null;
        }

        var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = _localization.Get("picker.root.title"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(_localization.Get("picker.type.firmware"))
                {
                    Patterns = ["*.zip", "payload.bin"]
                }
            ]
        }).ConfigureAwait(true);
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
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

    private IReadOnlyList<FilePickerFileType> BuildFilters()
    {
        if (OperatingSystem.IsWindows())
        {
            return new[]
            {
                new FilePickerFileType(_localization.Get("picker.type.adb")) { Patterns = new[] { "adb.exe" } },
                new FilePickerFileType(_localization.Get("picker.type.executable")) { Patterns = new[] { "*.exe" } },
                FilePickerFileTypes.All
            };
        }

        return new[]
        {
            new FilePickerFileType(_localization.Get("picker.type.adb")) { Patterns = new[] { "adb" } },
            FilePickerFileTypes.All
        };
    }

    private static Window? GetMainWindow() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    private async Task<IReadOnlyList<string>> PickFilesAsync(
        string title,
        IReadOnlyList<FilePickerFileType> filters)
    {
        var window = GetMainWindow();
        if (window?.StorageProvider is not { CanOpen: true } provider)
        {
            _logger.LogWarning("No storage provider is available for the file picker.");
            return Array.Empty<string>();
        }

        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
            FileTypeFilter = filters
        };

        var files = await provider.OpenFilePickerAsync(options).ConfigureAwait(true);
        return files
            .Select(file => file.TryGetLocalPath())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .ToArray();
    }
}
