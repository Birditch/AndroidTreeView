using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace AndroidTreeView.Mini.Mac;

public sealed class MainWindow : Window
{
    private readonly MiniAgent _agent;
    private readonly TextBlock _status = new();
    private readonly ListBox _log = new();
    private bool _stopCompleted;

    public MainWindow(MiniAgent agent)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));

        Title = "AndroidTreeView Mini";
        Width = 560;
        Height = 380;
        MinWidth = 440;
        MinHeight = 280;
        Background = BrushForHex("#12161C");
        Foreground = BrushForHex("#E5EBF3");
        FontFamily = FontFamily.Parse("Inter, -apple-system, BlinkMacSystemFont, sans-serif");

        BuildLayout();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        WireAgent();
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        await _agent.StartAsync();
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (!_stopCompleted)
        {
            e.Cancel = true;
            _stopCompleted = true;
            await _agent.StopAsync();
            Close();
            return;
        }

        _agent.Log.CollectionChanged -= OnLogChanged;
        _agent.StatusChanged -= OnStatusChanged;
        base.OnClosing(e);
    }

    private void BuildLayout()
    {
        var root = new Grid
        {
            Margin = new Thickness(14),
            RowDefinitions = new RowDefinitions("44,*"),
        };

        var top = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
        };

        var title = new TextBlock
        {
            Text = "Listening",
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeight.SemiBold,
            FontSize = 15,
            Foreground = Foreground,
        };

        _status.VerticalAlignment = VerticalAlignment.Center;
        _status.Margin = new Thickness(0, 0, 12, 0);
        _status.Foreground = BrushForHex("#87CEFA");

        var clear = new Button
        {
            Content = "Clear",
            Padding = new Thickness(12, 6),
            VerticalAlignment = VerticalAlignment.Center,
            Background = BrushForHex("#212832"),
            Foreground = Foreground,
        };
        clear.Click += (_, _) => _agent.ClearLog();

        Grid.SetColumn(title, 0);
        Grid.SetColumn(_status, 1);
        Grid.SetColumn(clear, 2);
        top.Children.Add(title);
        top.Children.Add(_status);
        top.Children.Add(clear);

        _log.Background = BrushForHex("#0A0E14");
        _log.BorderBrush = BrushForHex("#46505C");
        _log.BorderThickness = new Thickness(1);
        _log.FontFamily = FontFamily.Parse("Menlo, Consolas, monospace");
        _log.ItemTemplate = new FuncDataTemplate<MiniLogEntry>((entry, _) =>
            new TextBlock
            {
                Text = entry?.Display ?? string.Empty,
                Foreground = BrushFor(entry?.Level ?? MiniLogLevel.Info),
                TextWrapping = TextWrapping.NoWrap,
                Margin = new Thickness(6, 2),
            });
        _log.ItemsSource = _agent.Log;

        Grid.SetRow(top, 0);
        Grid.SetRow(_log, 1);
        root.Children.Add(top);
        root.Children.Add(_log);
        Content = root;
    }

    private void WireAgent()
    {
        _agent.Log.CollectionChanged += OnLogChanged;
        _agent.StatusChanged += OnStatusChanged;
        UpdateStatus();
    }

    private void OnStatusChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(UpdateStatus);
    }

    private void UpdateStatus()
    {
        _status.Text = $"{(_agent.AdbAvailable ? "ADB" : "NO ADB")} | {_agent.DeviceCount}";
    }

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is null || e.NewItems.Count == 0)
        {
            return;
        }

        var last = e.NewItems[^1];
        if (last is not null)
        {
            Dispatcher.UIThread.Post(() => _log.ScrollIntoView(last));
        }
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private static async void OnDrop(object? sender, DragEventArgs e)
    {
        if (sender is not MainWindow window)
        {
            return;
        }

        var files = e.DataTransfer.TryGetFiles();
        if (files is null)
        {
            return;
        }

        var paths = files
            .OfType<IStorageFile>()
            .Select(file => file.TryGetLocalPath())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .ToList();

        if (paths.Count > 0)
        {
            await window._agent.HandleDroppedFilesAsync(paths);
        }
    }

    private static IBrush BrushFor(MiniLogLevel level) => level switch
    {
        MiniLogLevel.Success => BrushForHex("#5CD68E"),
        MiniLogLevel.Warn => BrushForHex("#F5C45D"),
        MiniLogLevel.Error => BrushForHex("#FF7070"),
        _ => BrushForHex("#D8DEE9"),
    };

    private static IBrush BrushForHex(string value) => new SolidColorBrush(Color.Parse(value));
}
