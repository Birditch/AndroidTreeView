using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Models.System;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace AndroidTreeView.App.ViewModels;

/// <summary>
/// System category: kernel, SELinux, uptime, locale and timezone information.
/// </summary>
public sealed partial class SystemInfoViewModel : DeviceCategoryViewModelBase
{
    private readonly IDeviceService _deviceService;
    private readonly ILogger<SystemInfoViewModel> _logger;

    [ObservableProperty]
    private SystemInfo? _info;

    public SystemInfoViewModel(IDeviceService deviceService, ILogger<SystemInfoViewModel> logger)
    {
        _deviceService = deviceService;
        _logger = logger;
    }

    /// <inheritdoc />
    public override DeviceCategory Category => DeviceCategory.System;

    /// <inheritdoc />
    public override Task LoadAsync(string serial, CancellationToken ct)
    {
        Serial = serial;
        return RunAsync(async token =>
        {
            _logger.LogDebug("Loading system info for {Serial}", serial);
            Info = await _deviceService.GetSystemInfoAsync(serial, token);
        }, ct);
    }
}
