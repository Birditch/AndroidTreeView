using Microsoft.Extensions.Logging;

namespace AndroidTreeView.Infrastructure.Logging;

/// <summary>
/// Small helper for wiring the application's console logging in a consistent way. Kept intentionally
/// minimal: it resets any inherited providers, sets a floor level, and adds a single-line console sink.
/// </summary>
public static class LoggingSetup
{
    /// <summary>The default minimum level applied when none is supplied.</summary>
    public const LogLevel DefaultMinimumLevel = LogLevel.Information;

    /// <summary>
    /// Configures <paramref name="builder"/> with a single-line, timestamped console logger.
    /// </summary>
    /// <param name="builder">The logging builder to configure.</param>
    /// <param name="minimumLevel">The minimum level to emit.</param>
    /// <returns>The same <paramref name="builder"/> to allow chaining.</returns>
    public static ILoggingBuilder AddConsoleLogging(
        this ILoggingBuilder builder,
        LogLevel minimumLevel = DefaultMinimumLevel)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ClearProviders();
        builder.SetMinimumLevel(minimumLevel);
        builder.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        });

        return builder;
    }
}
