using Avalonia.Controls;
using Avalonia.Layout;

namespace AndroidTreeView.App.Controls;

/// <summary>A shimmering placeholder rectangle used for loading states.
/// Uses the inherited <see cref="Avalonia.Controls.Primitives.TemplatedControl.CornerRadius"/>.</summary>
public partial class SkeletonBlock : UserControl
{
    public SkeletonBlock()
    {
        InitializeComponent();

        // The shimmer is an INFINITE animation. Avalonia keeps such a clock ticking even when this
        // control is only collapsed by a parent (the loading panel toggles IsVisible), which drives
        // the render loop to repaint every frame — the app feels laggy and runs hot while "doing
        // nothing". EffectiveViewportChanged fires when an ancestor's visibility collapses/expands
        // our on-screen viewport; gate the animation on a non-empty viewport via the `:active`
        // pseudo-class so the clock stops whenever the skeleton isn't actually shown.
        EffectiveViewportChanged += OnEffectiveViewportChanged;
    }

    private void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        var visible = e.EffectiveViewport.Width > 0 && e.EffectiveViewport.Height > 0;
        PseudoClasses.Set(":active", visible);
    }
}
