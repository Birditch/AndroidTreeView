using AndroidTreeView.App.ViewModels;
using Avalonia;
using Avalonia.Controls;

namespace AndroidTreeView.App.Controls;

/// <summary>A small colored status pill. Colour is chosen from <see cref="Kind"/>.</summary>
public partial class StatusBadge : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<StatusBadge, string>(nameof(Text), string.Empty);

    public static readonly StyledProperty<DeviceBadgeKind> KindProperty =
        AvaloniaProperty.Register<StatusBadge, DeviceBadgeKind>(nameof(Kind));

    public StatusBadge()
    {
        InitializeComponent();
    }

    /// <summary>The text shown inside the pill.</summary>
    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>The badge kind used to pick the pill colour.</summary>
    public DeviceBadgeKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }
}
