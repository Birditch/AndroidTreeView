namespace AndroidTreeView.App.ViewModels;

/// <summary>
/// One entry in the device detail tab strip: a localized title, a glyph, the owning category, and the
/// category view model that supplies the tab's content (rendered via the ViewLocator).
/// </summary>
public sealed class DetailTab
{
    public DetailTab(DeviceCategory category, string title, string glyph, DeviceCategoryViewModelBase viewModel)
    {
        Category = category;
        Title = title;
        Glyph = glyph;
        ViewModel = viewModel;
    }

    /// <summary>The category this tab represents.</summary>
    public DeviceCategory Category { get; }

    /// <summary>Localized display title.</summary>
    public string Title { get; }

    /// <summary>Icon glyph shown alongside the title.</summary>
    public string Glyph { get; }

    /// <summary>The category view model loaded when this tab is selected.</summary>
    public DeviceCategoryViewModelBase ViewModel { get; }
}
