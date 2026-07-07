using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AndroidTreeView.Infrastructure.Persistence;

/// <summary>
/// Reads and writes a single JSON document atomically. Reads are tolerant: a missing or corrupt file
/// yields the supplied fallback instead of throwing. Writes are staged to a temporary file which then
/// atomically replaces the destination, so a crash mid-write can never corrupt the live document.
/// </summary>
/// <typeparam name="T">The document root type.</typeparam>
public sealed class JsonFileStore<T>
    where T : class
{
    private const string TempSuffix = ".tmp";

    private readonly string _filePath;
    private readonly JsonSerializerOptions _options;
    private readonly ILogger _logger;

    public JsonFileStore(string filePath, JsonSerializerOptions options, ILogger logger)
    {
        _filePath = filePath;
        _options = options;
        _logger = logger;
    }

    /// <summary>The absolute path of the backing document.</summary>
    public string FilePath => _filePath;

    /// <summary>
    /// Deserializes the document, returning <paramref name="fallbackFactory"/> output when the file is
    /// absent, empty, corrupt, or inaccessible. Never throws except on cooperative cancellation.
    /// </summary>
    public async Task<T> LoadAsync(Func<T> fallbackFactory, CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return fallbackFactory();
            }

            await using var stream = File.OpenRead(_filePath);
            if (stream.Length == 0)
            {
                return fallbackFactory();
            }

            var value = await JsonSerializer.DeserializeAsync<T>(stream, _options, ct).ConfigureAwait(false);
            return value ?? fallbackFactory();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.LogWarning(ex, "Failed to read JSON document {Path}; falling back to defaults.", _filePath);
            return fallbackFactory();
        }
    }

    /// <summary>Serializes <paramref name="value"/> and atomically replaces the backing document.</summary>
    public async Task SaveAsync(T value, CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = _filePath + TempSuffix;
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, value, _options, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }

        File.Move(tempPath, _filePath, overwrite: true);
    }
}
