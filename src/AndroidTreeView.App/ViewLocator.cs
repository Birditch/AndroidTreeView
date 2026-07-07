using System;
using AndroidTreeView.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace AndroidTreeView.App;

/// <summary>
/// Resolves a view for a view model by convention: the type's namespace segment <c>ViewModels</c> maps
/// to <c>Views</c> and the <c>ViewModel</c> suffix maps to <c>View</c> (e.g.
/// <c>...ViewModels.OverviewViewModel</c> → <c>...Views.OverviewView</c>).
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    /// <inheritdoc />
    public Control Build(object? data)
    {
        if (data is null)
        {
            return new TextBlock { Text = "No view model." };
        }

        var viewModelType = data.GetType();
        var viewName = viewModelType.FullName!
            .Replace("ViewModels", "Views", StringComparison.Ordinal)
            .Replace("ViewModel", "View", StringComparison.Ordinal);

        var viewType = viewModelType.Assembly.GetType(viewName);
        if (viewType is null)
        {
            return new TextBlock { Text = $"View not found: {viewName}" };
        }

        return (Control)Activator.CreateInstance(viewType)!;
    }

    /// <inheritdoc />
    public bool Match(object? data) => data is ViewModelBase;
}
