namespace AndroidTreeView.Core.Options;

/// <summary>Requested application theme.</summary>
public enum ThemeMode
{
    System,
    Light,
    Dark
}

/// <summary>How the application behaves on startup.</summary>
public enum StartupBehavior
{
    Normal,
    StartMinimized,
    RememberWindow
}

/// <summary>UI language selection. <see cref="ChineseSimplified"/> is the application default.</summary>
public enum AppLanguage
{
    /// <summary>Follow the operating-system UI culture (falls back to Chinese when unsupported).</summary>
    System,
    ChineseSimplified,
    English
}
