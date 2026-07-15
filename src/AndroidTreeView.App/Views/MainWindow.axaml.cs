using System;
using System.Linq;
using AndroidTreeView.App.Services;
using AndroidTreeView.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace AndroidTreeView.App.Views;

/// <summary>
/// The single application window. Pushes its client width to the view model so the responsive layout
/// mode can be recomputed as the window resizes, and wires app-wide toast requests to the shell view model.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        Notifier.Show = (message, level) => Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.ShowToast(message, level);
            }
        });
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if ((change.Property == ClientSizeProperty || change.Property == BoundsProperty)
            && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SetWindowWidth(Bounds.Width);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        Notifier.Show = null;
        base.OnClosed(e);
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = sender is MainWindow
        {
            DataContext: MainWindowViewModel { CurrentSection: NavSection.Devices }
        }
            && e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private static async void OnDrop(object? sender, DragEventArgs e)
    {
        if (sender is not MainWindow
            {
                DataContext: MainWindowViewModel { CurrentSection: NavSection.Devices } vm
            })
        {
            return;
        }

        var files = e.DataTransfer.TryGetFiles();
        if (files is null)
        {
            return;
        }

        var paths = files
            .OfType<IStorageFile>()
            .Select(file => file.TryGetLocalPath())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .ToList();

        if (paths.Count > 0)
        {
            await vm.HandleDroppedFilesAsync(paths).ConfigureAwait(true);
        }
    }
}
