using AndroidTreeView.App.Services;
using AndroidTreeView.Core;
using AndroidTreeView.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidTreeView.App.ViewModels;

/// <summary>
/// Shows application identity (name, version, project links) and offers a manual update check that
/// reuses <see cref="IUpdateService"/> with the shared <see cref="UpdatePresentation"/> messaging.
/// </summary>
public sealed partial class AboutViewModel : ViewModelBase
{
    private readonly IUpdateService _updateService;
    private readonly IFilePickerService _filePicker;
    private readonly ILocalizationService _localization;

    private string? _latestReleaseUrl;

    [ObservableProperty]
    private string? _latestVersion;

    [ObservableProperty]
    private string _updateStatusText = string.Empty;

    public AboutViewModel(
        IUpdateService updateService,
        IFilePickerService filePicker,
        ILocalizationService localization)
    {
        _updateService = updateService;
        _filePicker = filePicker;
        _localization = localization;
    }

    public string AppName => AppInfo.Name;

    public string Version => AppInfo.Version;

    public string ProjectUrl => AppInfo.ProjectUrl;

    [RelayCommand]
    private async Task CheckUpdatesAsync(CancellationToken ct)
    {
        UpdateStatusText = _localization.Get("update.checking");
        var result = await _updateService.CheckForUpdatesAsync(userInitiated: true, ct).ConfigureAwait(true);
        LatestVersion = result.LatestVersion;
        _latestReleaseUrl = result.ReleaseUrl;
        UpdateStatusText = UpdatePresentation.Describe(result, _localization);
    }

    [RelayCommand]
    private Task OpenProjectAsync() => _filePicker.OpenUrlAsync(ProjectUrl);

    [RelayCommand]
    private Task OpenReleasesAsync()
        => _filePicker.OpenUrlAsync(_latestReleaseUrl ?? AppInfo.ReleasesUrl);
}
