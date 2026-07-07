using System.Globalization;
using AndroidTreeView.Models.Logs;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace AndroidTreeView.App.Converters;

/// <summary>Maps a logcat <see cref="LogPriority"/> to a readable foreground brush.</summary>
public sealed class LogPriorityToBrushConverter : IValueConverter
{
    private static readonly IBrush Muted = new ImmutableSolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));
    private static readonly IBrush Debug = new ImmutableSolidColorBrush(Color.FromRgb(0x60, 0x7D, 0x8B));
    private static readonly IBrush Info = new ImmutableSolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
    private static readonly IBrush Warn = new ImmutableSolidColorBrush(Color.FromRgb(0xEA, 0x58, 0x0C));
    private static readonly IBrush Error = new ImmutableSolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
    private static readonly IBrush Fatal = new ImmutableSolidColorBrush(Color.FromRgb(0x99, 0x1B, 0x1B));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is LogPriority priority
            ? priority switch
            {
                LogPriority.Verbose => Muted,
                LogPriority.Debug => Debug,
                LogPriority.Info => Info,
                LogPriority.Warn => Warn,
                LogPriority.Error => Error,
                LogPriority.Fatal => Fatal,
                _ => Muted
            }
            : Muted;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
