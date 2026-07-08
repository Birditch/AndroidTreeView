using AndroidTreeView.App.Services;
using AndroidTreeView.Core;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Models;
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
    private readonly IUpdateInstaller _updateInstaller;
    private readonly IFilePickerService _filePicker;
    private readonly ILocalizationService _localization;

    private string? _latestReleaseUrl;
    private UpdateCheckResult? _latestUpdate;

    [ObservableProperty]
    private string? _latestVersion;

    [ObservableProperty]
    private string _updateStatusText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    private bool _canInstallUpdate;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    private bool _isInstallingUpdate;

    public AboutViewModel(
        IUpdateService updateService,
        IUpdateInstaller updateInstaller,
        IFilePickerService filePicker,
        ILocalizationService localization)
    {
        _updateService = updateService;
        _updateInstaller = updateInstaller;
        _filePicker = filePicker;
        _localization = localization;
        _updateStatusText = _localization.Get("update.ready");
    }

    public string AppName => AppInfo.Name;

    public string Version => AppInfo.Version;

    public string ProjectUrl => AppInfo.ProjectUrl;

    public string UpdateSource => AppInfo.UpdateServerBaseUrl;

    public string UpdateKey => AppInfo.AppUpdateKey;

    [RelayCommand]
    private async Task CheckUpdatesAsync(CancellationToken ct)
    {
        UpdateStatusText = _localization.Get("update.checking");
        var result = await _updateService.CheckForUpdatesAsync(userInitiated: true, ct).ConfigureAwait(true);
        LatestVersion = result.LatestVersion;
        _latestReleaseUrl = result.ReleaseUrl;
        _latestUpdate = result.UpdateAvailable ? result : null;
        CanInstallUpdate = result.UpdateAvailable && !string.IsNullOrWhiteSpace(result.DownloadUrl);
        UpdateStatusText = UpdatePresentation.Describe(result, _localization);
    }

    private bool CanInstallUpdateNow() => CanInstallUpdate && !IsInstallingUpdate;

    [RelayCommand(CanExecute = nameof(CanInstallUpdateNow))]
    private async Task InstallUpdateAsync(CancellationToken ct)
    {
        if (_latestUpdate is null)
        {
            return;
        }

        IsInstallingUpdate = true;
        UpdateStatusText = _localization.Get("update.downloading");

        try
        {
            var result = await _updateInstaller.InstallAsync(_latestUpdate, ct).ConfigureAwait(true);
            UpdateStatusText = UpdatePresentation.DescribeInstall(result, _localization);
            CanInstallUpdate = !result.Started;
        }
        finally
        {
            IsInstallingUpdate = false;
        }
    }

    [RelayCommand]
    private Task OpenProjectAsync() => _filePicker.OpenUrlAsync(ProjectUrl);

    [RelayCommand]
    private Task OpenReleasesAsync()
        => _filePicker.OpenUrlAsync(_latestReleaseUrl ?? AppInfo.ReleasesUrl);
}
