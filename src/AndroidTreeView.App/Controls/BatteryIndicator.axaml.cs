using Avalonia;
using Avalonia.Controls;

namespace AndroidTreeView.App.Controls;

/// <summary>
/// A small battery gauge that scales to its allocated size. The coloured fill grows with
/// <see cref="Level"/> via proportional grid columns, and a charging glyph appears when charging.
/// </summary>
public partial class BatteryIndicator : UserControl
{
    public static readonly StyledProperty<int?> LevelProperty =
        AvaloniaProperty.Register<BatteryIndicator, int?>(nameof(Level));

    public static readonly StyledProperty<bool> IsChargingProperty =
        AvaloniaProperty.Register<BatteryIndicator, bool>(nameof(IsCharging));

    public static readonly StyledProperty<bool> IsLowProperty =
        AvaloniaProperty.Register<BatteryIndicator, bool>(nameof(IsLow));

    private readonly Grid? _track;

    public BatteryIndicator()
    {
        InitializeComponent();
        _track = this.FindControl<Grid>("PART_Track");
        UpdateFill();
    }

    /// <summary>Battery level in percent (0-100), or null when unknown.</summary>
    public int? Level
    {
        get => GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    /// <summary>Whether the device is currently charging (shows a charging glyph).</summary>
    public bool IsCharging
    {
        get => GetValue(IsChargingProperty);
        set => SetValue(IsChargingProperty, value);
    }

    /// <summary>Whether the battery is low (tints the outline red).</summary>
    public bool IsLow
    {
        get => GetValue(IsLowProperty);
        set => SetValue(IsLowProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == LevelProperty)
        {
            UpdateFill();
        }
        else if (change.Property == IsLowProperty)
        {
            PseudoClasses.Set(":low", IsLow);
        }
    }

    private void UpdateFill()
    {
        if (_track is null || _track.ColumnDefinitions.Count < 2)
        {
            return;
        }

        var level = Math.Clamp(Level ?? 0, 0, 100);
        _track.ColumnDefinitions[0].Width = new GridLength(level, GridUnitType.Star);
        _track.ColumnDefinitions[1].Width = new GridLength(100 - level, GridUnitType.Star);
    }
}
