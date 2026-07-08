using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidTreeView.App.Services;

/// <summary>
/// A single app-wide modal confirmation dialog. View models call <see cref="ConfirmAsync"/> and await the
/// user's choice; the shell binds to the properties to render a centered card over a blurred backdrop.
/// </summary>
public interface IDialogService : INotifyPropertyChanged
{
    /// <summary>Whether the confirmation dialog is currently shown.</summary>
    bool IsOpen { get; }

    string Title { get; }

    string Message { get; }

    string ConfirmText { get; }

    string CancelText { get; }

    ICommand ConfirmCommand { get; }

    ICommand CancelCommand { get; }

    /// <summary>Shows a modal confirmation and completes with <c>true</c> (confirmed) or <c>false</c> (cancelled).</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText);
}

/// <inheritdoc cref="IDialogService"/>
public sealed partial class DialogService : ObservableObject, IDialogService
{
    private TaskCompletionSource<bool>? _pending;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private string _confirmText = "OK";

    [ObservableProperty]
    private string _cancelText = "Cancel";

    // The generated relay commands are IRelayCommand; expose them through the interface as ICommand.
    ICommand IDialogService.ConfirmCommand => ConfirmCommand;

    ICommand IDialogService.CancelCommand => CancelCommand;

    /// <summary>Must be called on the UI thread. Any previously-open dialog resolves as cancelled.</summary>
    public Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText)
    {
        _pending?.TrySetResult(false);

        Title = title;
        Message = message;
        ConfirmText = confirmText;
        CancelText = cancelText;
        _pending = new TaskCompletionSource<bool>();
        IsOpen = true;

        return _pending.Task;
    }

    [RelayCommand]
    private void Confirm() => Close(true);

    [RelayCommand]
    private void Cancel() => Close(false);

    private void Close(bool result)
    {
        IsOpen = false;
        var pending = _pending;
        _pending = null;
        pending?.TrySetResult(result);
    }
}
