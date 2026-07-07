using AndroidTreeView.Core.Exceptions;
using AndroidTreeView.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace AndroidTreeView.App.ViewModels;

/// <summary>
/// Shared base for the per-category detail view models. Owns the loading/error surface and a
/// <see cref="RunAsync"/> helper that toggles <see cref="IsLoading"/> and maps ADB failures to a
/// localized <see cref="ErrorMessage"/> — it never lets an exception escape to the UI.
/// </summary>
/// <remarks>
/// Category view models use the implicit parameterless constructor and only inject the services they
/// need (typically <c>IDeviceService</c> + <c>ILogger&lt;T&gt;</c>). Because they do not forward an
/// <see cref="ILocalizationService"/>, the base resolves one lazily from the application's DI provider
/// (<see cref="App.ServiceProvider"/>) for error localization, falling back to neutral English text when
/// no provider is present (e.g. in unit tests).
/// </remarks>
public abstract partial class DeviceCategoryViewModelBase : ViewModelBase
{
    private ILocalizationService? _localization;
    private bool _localizationResolved;

    /// <summary>True while an asynchronous load is in flight.</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>True when the most recent load failed.</summary>
    [ObservableProperty]
    private bool _hasError;

    /// <summary>Localized description of the most recent failure, when any.</summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>The serial of the device currently bound to this category.</summary>
    public string? Serial { get; protected set; }

    /// <summary>The category this view model represents.</summary>
    public abstract DeviceCategory Category { get; }

    /// <summary>Loads the category's data for the given device.</summary>
    public abstract Task LoadAsync(string serial, CancellationToken ct);

    /// <summary>
    /// Runs <paramref name="body"/> while managing <see cref="IsLoading"/>/<see cref="HasError"/> and
    /// mapping known ADB failures to a localized <see cref="ErrorMessage"/>. Cancellation is not treated
    /// as an error.
    /// </summary>
    protected async Task RunAsync(Func<CancellationToken, Task> body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        IsLoading = true;
        HasError = false;
        ErrorMessage = null;

        try
        {
            await body(ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // A cancelled load is expected (e.g. the user switched device) and is not an error.
        }
        catch (DeviceUnauthorizedException)
        {
            SetError("error.unauthorized", "Device unauthorized — accept the USB debugging prompt on the device.");
        }
        catch (DeviceOfflineException)
        {
            SetError("error.offline", "Device offline.");
        }
        catch (AdbException)
        {
            SetError("error.generic", "Something went wrong.");
        }
        catch (Exception)
        {
            SetError("error.generic", "Something went wrong.");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void SetError(string key, string fallback)
    {
        HasError = true;
        ErrorMessage = Localize(key, fallback);
    }

    private string Localize(string key, string fallback)
    {
        var localization = ResolveLocalization();
        if (localization is null)
        {
            return fallback;
        }

        var text = localization.Get(key);
        return string.IsNullOrEmpty(text) || string.Equals(text, key, StringComparison.Ordinal)
            ? fallback
            : text;
    }

    private ILocalizationService? ResolveLocalization()
    {
        if (_localizationResolved)
        {
            return _localization;
        }

        _localization = App.ServiceProvider?.GetService<ILocalizationService>();
        _localizationResolved = true;
        return _localization;
    }
}
