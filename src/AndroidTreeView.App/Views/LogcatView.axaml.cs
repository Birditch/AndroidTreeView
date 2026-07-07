using AndroidTreeView.Models.Logs;
using Avalonia.Controls;

namespace AndroidTreeView.App.Views;

public partial class LogcatView : UserControl
{
    /// <summary>
    /// Selectable minimum-priority options for the toolbar selector. Exposed as a static so the View can
    /// source the combo box without adding a non-contract property to the ViewModel.
    /// </summary>
    public static IReadOnlyList<LogPriority> PriorityOptions { get; } = new[]
    {
        LogPriority.Verbose,
        LogPriority.Debug,
        LogPriority.Info,
        LogPriority.Warn,
        LogPriority.Error,
        LogPriority.Fatal,
    };

    public LogcatView()
    {
        InitializeComponent();
    }
}
