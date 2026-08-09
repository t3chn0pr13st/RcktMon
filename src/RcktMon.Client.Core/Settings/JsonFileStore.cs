using System.Text.Json;
using System.Text.Json.Serialization;

namespace RcktMon.Client.Core.Settings;

/// <summary>
/// A small JSON document on disk, written atomically and no more often than it has to be.
/// </summary>
/// <remarks>
/// <para>
/// Two properties matter and the pre-rewrite build had neither. First, <b>debounce</b>:
/// window geometry was written synchronously on every <c>SizeChanged</c>,
/// <c>LocationChanged</c> and <c>StateChanged</c>, so dragging a window across the screen
/// produced hundreds of blocking disk writes. Second, <b>atomicity</b>: the old writes
/// truncated the real file in place, so a crash mid-write left a zero-length settings
/// file and the app started with everything reset.
/// </para>
/// <para>
/// Writes go to a sibling temp file and are then moved over the target, which is atomic on
/// every filesystem this app runs on.
/// </para>
/// </remarks>
public sealed class JsonFileStore<T> : IDisposable
    where T : class, new()
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly TimeSpan _debounce;
    private readonly Lock _gate = new();
    private readonly Timer _timer;

    private T? _pending;
    private bool _disposed;

    public JsonFileStore(string path, TimeSpan? debounce = null)
    {
        _path = path;
        _debounce = debounce ?? TimeSpan.FromMilliseconds(500);
        _timer = new Timer(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Raised when a load or save fails. The caller decides whether to surface it.</summary>
    public event Action<Exception>? Failed;

    /// <summary>
    /// Reads the document, or returns a default one.
    /// </summary>
    /// <remarks>
    /// A corrupt file is renamed aside rather than deleted, and the caller gets defaults.
    /// Losing a settings file silently is bad; refusing to start because of one is worse.
    /// </remarks>
    public T Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new T();
            }

            using FileStream stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize<T>(stream, Options) ?? new T();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Failed?.Invoke(ex);
            QuarantineCorruptFile();
            return new T();
        }
    }

    /// <summary>Queues a write. Repeated calls inside the debounce window collapse into one.</summary>
    public void Save(T value)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _pending = value;
            _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Writes any queued document immediately. Called on exit.</summary>
    public void Flush()
    {
        T? value;

        lock (_gate)
        {
            value = _pending;
            _pending = null;
        }

        if (value is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

            string temp = _path + ".tmp";

            using (FileStream stream = File.Create(temp))
            {
                JsonSerializer.Serialize(stream, value, Options);
            }

            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Failed?.Invoke(ex);
        }
    }

    private void QuarantineCorruptFile()
    {
        try
        {
            File.Move(_path, _path + ".corrupt", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing more to do: the caller already has defaults and the app can run.
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _timer.Dispose();
        Flush();
    }
}
