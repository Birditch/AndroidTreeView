namespace AndroidTreeView.App.ViewModels;

/// <summary>Responsive layout breakpoints derived from the window width.</summary>
public enum AppLayoutMode
{
    /// <summary>Wide layout (&gt;= 1200): sidebar + card grid + optional status.</summary>
    Wide,

    /// <summary>Medium layout (800..1199): collapsible sidebar + 2-column grid.</summary>
    Medium,

    /// <summary>Narrow layout (&lt; 800): single column with top navigation and Back.</summary>
    Narrow
}

/// <summary>Top-level navigation sections shown in the sidebar.</summary>
public enum NavSection
{
    Devices,
    Settings,
    About
}

/// <summary>Visual kind used to pick a status-badge brush.</summary>
public enum DeviceBadgeKind
{
    Online,
    Offline,
    Unauthorized,
    Other
}

/// <summary>The per-device information categories shown on the detail page.</summary>
public enum DeviceCategory
{
    Overview,
    Hardware,
    Battery,
    System,
    Storage,
    Network,
    Root,
    Logcat,
    RawProperties
}
