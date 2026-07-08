using System;

namespace AndroidTreeView.App.Services;

/// <summary>Severity of a toast; mapped by the shell to a colored notification (blue/green/amber/red).</summary>
public enum NotifierLevel
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>
/// Lightweight app-wide toast hook. The shell wires <see cref="Show"/> to its own toast layer at startup;
/// view models call <see cref="Notify"/> to give the user
/// visible, color-coded feedback for fire-and-forget actions. Safe before wiring (a null
/// <see cref="Show"/> is simply ignored).
/// </summary>
public static class Notifier
{
    /// <summary>Set by the shell to display a transient toast; null until the main window is ready.</summary>
    public static Action<string, NotifierLevel>? Show { get; set; }

    public static void Notify(string message, NotifierLevel level = NotifierLevel.Info) => Show?.Invoke(message, level);
}
