using AndroidTreeView.App.ViewModels;
using Avalonia;
using Avalonia.Controls;

namespace AndroidTreeView.App.Views;

/// <summary>
/// The single application window. Pushes its client width to the view model so the responsive layout
/// mode (Wide / Medium / Narrow) can be recomputed as the window resizes.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
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
}
