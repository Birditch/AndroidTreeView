using CommunityToolkit.Mvvm.ComponentModel;

namespace AndroidTreeView.App.ViewModels;

/// <summary>
/// Base class for all view models. Provides <see cref="ObservableObject"/> change notification and
/// acts as the marker type the <c>ViewLocator</c> matches on.
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
}
