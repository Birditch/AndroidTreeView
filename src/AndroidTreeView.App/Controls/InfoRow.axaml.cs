using Avalonia;
using Avalonia.Controls;

namespace AndroidTreeView.App.Controls;

/// <summary>A label/value line. Shows an em dash ("—") when <see cref="Value"/> is null or empty.</summary>
public partial class InfoRow : UserControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<InfoRow, string>(nameof(Label), string.Empty);

    public static readonly StyledProperty<string?> ValueProperty =
        AvaloniaProperty.Register<InfoRow, string?>(nameof(Value));

    public InfoRow()
    {
        InitializeComponent();
    }

    /// <summary>The left-hand label.</summary>
    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>The right-hand value; rendered as an em dash when null or empty.</summary>
    public string? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }
}
