using AndroidTreeView.App.Services;
using AndroidTreeView.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidTreeView.App.ViewModels;

/// <summary>
/// Shown when adb cannot be located at startup. Lets the user download platform-tools, pick the adb
/// executable manually, and retry detection. On a successful retry it updates the shared
/// <see cref="IAdbEnvironment"/>, persists the resolved path, and raises <see cref="AdbReady"/> so the
/// shell can leave the setup screen.
/// </summary>
public sealed partial class SetupViewModel : ViewModelBase
{
    private const string PlatformToolsDownloadUrl = "https://developer.android.com/tools/releases/platform-tools";

    private readonly IAdbLocator _adbLocator;
    private readonly IAdbEnvironment _adbEnvironment;
    private readonly ISettingsService _settingsService;
    private readonly IFilePickerService _filePicker;
    private readonly ILocalizationService _localization;

    [ObservableProperty]
    private string? _adbPath;

    [ObservableProperty]
    private bool _isChecking;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string _instructionsText;

    public SetupViewModel(
        IAdbLocator adbLocator,
        IAdbEnvironment adbEnvironment,
        ISettingsService settingsService,
        IFilePickerService filePicker,
        ILocalizationService localization)
    {
        _adbLocator = adbLocator;
        _adbEnvironment = adbEnvironment;
        _settingsService = settingsService;
        _filePicker = filePicker;
        _localization = localization;

        _instructionsText = localization.Get("setup.body");
        _adbPath = settingsService.Current.AdbPath;
    }

    /// <summary>Raised once a usable adb executable has been located and persisted.</summary>
    public event EventHandler? AdbReady;

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var path = await _filePicker.PickAdbExecutableAsync().ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(path))
        {
            AdbPath = path;
        }
    }

    [RelayCommand]
    private async Task RetryAsync(CancellationToken ct)
    {
        if (IsChecking)
        {
            return;
        }

        IsChecking = true;
        StatusMessage = _localization.Get("common.loading");
        try
        {
            var location = await _adbLocator.LocateAsync(AdbPath, ct).ConfigureAwait(true);
            if (location is not null)
            {
                _adbEnvironment.Set(location);

                var settings = _settingsService.Current.Clone();
                settings.AdbPath = location.ExecutablePath;
                await _settingsService.SaveAsync(settings, ct).ConfigureAwait(true);

                AdbPath = location.ExecutablePath;
                StatusMessage = _localization.Get("adb.status.ready");
                AdbReady?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                StatusMessage = _localization.Get("adb.status.missing");
            }
        }
        finally
        {
            IsChecking = false;
        }
    }

    [RelayCommand]
    private Task OpenDownloadPageAsync() => _filePicker.OpenUrlAsync(PlatformToolsDownloadUrl);
}
