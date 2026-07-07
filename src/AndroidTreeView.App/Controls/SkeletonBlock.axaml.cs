using Avalonia.Controls;

namespace AndroidTreeView.App.Controls;

/// <summary>A shimmering placeholder rectangle used for loading states.
/// Uses the inherited <see cref="Avalonia.Controls.Primitives.TemplatedControl.CornerRadius"/>.</summary>
public partial class SkeletonBlock : UserControl
{
    public SkeletonBlock()
    {
        InitializeComponent();
    }
}
