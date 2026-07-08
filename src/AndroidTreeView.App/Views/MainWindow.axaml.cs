using System;
using AndroidTreeView.App.Services;
using AndroidTreeView.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
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
}
