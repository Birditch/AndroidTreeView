using System.Collections.Specialized;
using System.Drawing;
using System.Windows.Forms;
using AndroidTreeView.Core;
using AndroidTreeView.Mini.Models;
using AndroidTreeView.Mini.ViewModels;
using Microsoft.Extensions.Logging;

namespace AndroidTreeView.Mini.Views;

/// <summary>
/// Tiny native Windows companion window. Keeping this UI on WinForms avoids shipping Avalonia/Skia in
/// the Mini package while preserving the same always-listening behavior.
/// </summary>
public sealed class MiniForm : Form
{
    private readonly MiniViewModel _viewModel;
    private readonly ILogger<MiniForm>? _logger;
    private readonly RichTextBox _log = new();
    private readonly Label _status = new();
    private bool _stopCompleted;

    public MiniForm(MiniViewModel viewModel, ILogger<MiniForm>? logger)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _logger = logger;

        Text = $"AndroidTreeView Mini v{AppInfo.Version}";
        Width = 520;
        Height = 360;
        MinimumSize = new Size(420, 260);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(18, 22, 28);
        ForeColor = Color.FromArgb(229, 235, 243);
        Font = new Font("Segoe UI", 9F);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        BuildLayout();
        WireViewModel();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);

        try
        {
            await _viewModel.StartAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Mini start failed.");
        }
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_stopCompleted)
        {
            e.Cancel = true;
            _stopCompleted = true;

            try
            {
                await _viewModel.StopAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Mini stop failed.");
            }

            BeginInvoke((Action)Close);
            return;
        }

        _viewModel.Log.CollectionChanged -= OnLogChanged;
        base.OnFormClosing(e);
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(12),
            BackColor = BackColor,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = BackColor,
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = _viewModel.Header,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = ForeColor,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        _status.Text = "0";
        _status.AutoSize = true;
        _status.Margin = new Padding(0, 10, 10, 0);
        _status.ForeColor = Color.FromArgb(135, 206, 250);

        var clear = new Button
        {
            Text = "Clear",
            AutoSize = true,
            Margin = new Padding(0, 6, 0, 6),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(33, 40, 50),
            ForeColor = ForeColor,
        };
        clear.FlatAppearance.BorderColor = Color.FromArgb(70, 78, 92);
        clear.Click += (_, _) => _viewModel.Log.Clear();

        top.Controls.Add(title, 0, 0);
        top.Controls.Add(_status, 1, 0);
        top.Controls.Add(clear, 2, 0);

        _log.Dock = DockStyle.Fill;
        _log.ReadOnly = true;
        _log.BorderStyle = BorderStyle.FixedSingle;
        _log.BackColor = Color.FromArgb(10, 14, 20);
        _log.ForeColor = Color.FromArgb(216, 222, 233);
        _log.Font = new Font("Consolas", 9F);
        _log.DetectUrls = false;
        _log.WordWrap = false;

        root.Controls.Add(top, 0, 0);
        root.Controls.Add(_log, 0, 1);
        Controls.Add(root);
    }

    private void WireViewModel()
    {
        _viewModel.Log.CollectionChanged += OnLogChanged;
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MiniViewModel.DeviceCount)
                || e.PropertyName == nameof(MiniViewModel.AdbAvailable))
            {
                UpdateStatus();
            }
        };
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (InvokeRequired)
        {
            BeginInvoke((Action)UpdateStatus);
            return;
        }

        var adb = _viewModel.AdbAvailable ? "ADB" : "NO ADB";
        _status.Text = $"{adb} | {_viewModel.DeviceCount}";
    }

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke((Action)(() => OnLogChanged(sender, e)));
            return;
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            _log.Clear();
            return;
        }

        if (e.NewItems is null)
        {
            return;
        }

        foreach (MiniLogEntry entry in e.NewItems)
        {
            AppendLog(entry);
        }
    }

    private void AppendLog(MiniLogEntry entry)
    {
        var start = _log.TextLength;
        _log.AppendText($"[{entry.Time}] {entry.Message}{Environment.NewLine}");
        _log.Select(start, _log.TextLength - start);
        _log.SelectionColor = ColorFor(entry.Level);
        _log.SelectionLength = 0;
        _log.ScrollToCaret();
    }

    private static Color ColorFor(MiniLogLevel level) => level switch
    {
        MiniLogLevel.Success => Color.FromArgb(92, 214, 142),
        MiniLogLevel.Warn => Color.FromArgb(245, 196, 93),
        MiniLogLevel.Error => Color.FromArgb(255, 112, 112),
        _ => Color.FromArgb(216, 222, 233),
    };
}
