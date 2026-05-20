using System.IO;
using System.Text.Json;
using System.Threading;
using Moza.ScLink.Core.Diagnostics;

namespace Moza.ScLink.Logs.Parsing;

/// <summary>
/// Loads a versioned pattern file (<see cref="PatternFile"/>) into a compiled
/// <see cref="StarCitizenEventParser"/> and hot-reloads it on file change (FileSystemWatcher + debounce).
/// The current parser is swapped atomically; consumers read <see cref="Current"/> and pick up reloads
/// without re-subscribing.
/// <para>
/// Error policy: a valid schemaVersion-1 file is applied. On INITIAL load, schema mismatch / malformed
/// JSON / missing file yields an EMPTY set + warning ("load defaults"). On RELOAD, any bad file RETAINS
/// the current good set rather than degrading a running session on a transient bad edit.
/// </para>
/// <para>
/// KNOWN LIMITATION (T-11): an empty initial load is operationally a SILENT failure — the app starts,
/// produces no events, and the only signal is a file-log warning. A user-visible diagnostic of this state
/// is deferred to the T-18 diagnostics panel. Embedded-defaults fallback was considered and rejected for
/// T-11 (duplicate pattern data to keep in sync).
/// </para>
/// </summary>
public sealed class PatternLibrary : IDisposable
{
    private static readonly TimeSpan DefaultDebounceWindow = TimeSpan.FromMilliseconds(300);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _patternFilePath;
    private readonly TimeSpan? _matchTimeout;
    private readonly TimeSpan _debounceWindow;
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _debounceTimer;
    private readonly object _gate = new();
    private StarCitizenEventParser _current;
    private bool _reloadInProgress;
    private bool _disposed;

    public PatternLibrary(string patternFilePath, TimeSpan? matchTimeout = null, TimeSpan? debounceWindow = null)
    {
        ArgumentNullException.ThrowIfNull(patternFilePath);
        _patternFilePath = Path.GetFullPath(patternFilePath);
        _matchTimeout = matchTimeout;
        _debounceWindow = debounceWindow ?? DefaultDebounceWindow;

        // Initial load: empty set on schema-mismatch/malformed/missing (KNOWN LIMITATION above).
        _current = TryLoad(out var parser) ? parser : new StarCitizenEventParser([], _matchTimeout);

        // Long-lived debounce timer, created stopped; each FSW event resets its due time via Change().
        _debounceTimer = new Timer(_ => Reload(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        _watcher = new FileSystemWatcher(
            Path.GetDirectoryName(_patternFilePath)!,
            Path.GetFileName(_patternFilePath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
        _watcher.Renamed += OnFileChanged;
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>Resolves <c>Patterns/v0.json</c> next to the executable with default timeout/debounce.</summary>
    public static PatternLibrary LoadDefault() =>
        new(Path.Combine(AppContext.BaseDirectory, "Patterns", "v0.json"));

    /// <summary>Current compiled parser. Thread-safe; swapped atomically on a successful reload.</summary>
    public StarCitizenEventParser Current => Volatile.Read(ref _current);

    /// <summary>Raised after a successful reload swaps <see cref="Current"/>.</summary>
    public event EventHandler? Changed;

    // FileSystemWatcher fires multiple events per save; the debounce timer collapses a burst into one
    // reload — each event resets the timer, which fires only after the window elapses quietly.
    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _debounceTimer.Change(_debounceWindow, Timeout.InfiniteTimeSpan);
        }
    }

    private void Reload()
    {
        lock (_gate)
        {
            if (_disposed || _reloadInProgress)
            {
                return;  // serialize reloads; a concurrent fire re-returns (a later FSW event reschedules)
            }

            _reloadInProgress = true;
        }

        try
        {
            // Compile off the FSW event thread and BEFORE the swap, so consumers never see a half-built
            // parser; bad file -> retain the current good set (don't degrade a running session).
            if (!TryLoad(out var parser))
            {
                return;
            }

            Interlocked.Exchange(ref _current, parser);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            lock (_gate)
            {
                _reloadInProgress = false;
            }
        }
    }

    private bool TryLoad(out StarCitizenEventParser parser)
    {
        parser = null!;
        try
        {
            if (!File.Exists(_patternFilePath))
            {
                AppLog.Write($"Pattern file not found: {_patternFilePath}.");
                return false;
            }

            var document = JsonSerializer.Deserialize<PatternFile>(File.ReadAllText(_patternFilePath), JsonOptions);
            if (document is null)
            {
                AppLog.Write($"Pattern file deserialized to null: {_patternFilePath}.");
                return false;
            }

            if (document.SchemaVersion != 1)
            {
                AppLog.Write($"Pattern file schemaVersion {document.SchemaVersion} is unsupported (expected 1): {_patternFilePath}.");
                return false;
            }

            parser = new StarCitizenEventParser(document.Patterns, _matchTimeout);
            return true;
        }
        catch (JsonException ex)
        {
            AppLog.Write(ex, $"Pattern file is malformed JSON: {_patternFilePath}.");
            return false;
        }
        catch (IOException ex)
        {
            AppLog.Write(ex, $"Pattern file read failed: {_patternFilePath}.");
            return false;
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
            _debounceTimer.Dispose();
        }

        _watcher.EnableRaisingEvents = false;
        _watcher.Changed -= OnFileChanged;
        _watcher.Created -= OnFileChanged;
        _watcher.Renamed -= OnFileChanged;
        _watcher.Dispose();
    }
}
