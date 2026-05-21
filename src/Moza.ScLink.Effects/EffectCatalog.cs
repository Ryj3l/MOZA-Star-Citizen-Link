using System.IO;
using System.Text.Json;
using System.Threading;
using Moza.ScLink.Core.Diagnostics;
using Moza.ScLink.Core.Models;

namespace Moza.ScLink.Effects.Catalogs;

/// <summary>
/// Loads a versioned effect catalog (<see cref="EffectCatalogDocument"/>) into an immutable list of
/// validated <see cref="EffectDefinition"/>s and hot-reloads it on file change (FileSystemWatcher +
/// debounce). The current set is swapped atomically; consumers read <see cref="Current"/> and pick up
/// reloads without re-subscribing.
/// <para>
/// Error policy mirrors <c>PatternLibrary</c>: STRUCTURAL failure (missing file / malformed JSON /
/// unsupported schemaVersion) yields an EMPTY set on INITIAL load ("load defaults") and RETAINS the
/// current good set on RELOAD. Per-effect validation failures (bad effectId/category/effectType or
/// out-of-range parameters) drop only the offending effect with a logged message; valid effects load.
/// </para>
/// </summary>
public sealed class EffectCatalog : IDisposable
{
    private static readonly TimeSpan DefaultDebounceWindow = TimeSpan.FromMilliseconds(300);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Catalog effect-type vocabulary. Render types (<c>Periodic</c>, <c>ConstantForce</c>,
    /// <c>PeriodicWithEnvelope</c>) are interpreted by T-14's resolver; <c>Stop</c> is the catalog's
    /// stop-control vocabulary (not a render type). Compared case-insensitively.
    /// </summary>
    private static readonly HashSet<string> EffectTypeVocabulary =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Periodic", "ConstantForce", "PeriodicWithEnvelope", "Stop",
        };

    private readonly string _catalogFilePath;
    private readonly TimeSpan _debounceWindow;
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _debounceTimer;
    private readonly object _gate = new();
    private IReadOnlyList<EffectDefinition> _current;
    private bool _reloadInProgress;
    private bool _disposed;

    public EffectCatalog(string catalogFilePath, TimeSpan? debounceWindow = null)
    {
        ArgumentNullException.ThrowIfNull(catalogFilePath);
        _catalogFilePath = Path.GetFullPath(catalogFilePath);
        _debounceWindow = debounceWindow ?? DefaultDebounceWindow;

        // Initial load: empty set on structural failure (missing/malformed/unsupported version).
        _current = TryLoad(out var effects) ? effects : [];

        _debounceTimer = new Timer(_ => Reload(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        _watcher = new FileSystemWatcher(
            Path.GetDirectoryName(_catalogFilePath)!,
            Path.GetFileName(_catalogFilePath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
        _watcher.Renamed += OnFileChanged;
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>Resolves <c>Catalogs/phase1.json</c> next to the executable with the default debounce.</summary>
    public static EffectCatalog LoadDefault() =>
        new(Path.Combine(AppContext.BaseDirectory, "Catalogs", "phase1.json"));

    /// <summary>Current validated effect set. Thread-safe; swapped atomically on a successful reload.</summary>
    public IReadOnlyList<EffectDefinition> Current => Volatile.Read(ref _current);

    /// <summary>Raised after a successful reload swaps <see cref="Current"/>.</summary>
    public event EventHandler? Changed;

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
                return;
            }

            _reloadInProgress = true;
        }

        try
        {
            if (!TryLoad(out var effects))
            {
                return;  // structural failure -> retain the current good set
            }

            Interlocked.Exchange(ref _current, effects);
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

    private bool TryLoad(out IReadOnlyList<EffectDefinition> effects)
    {
        effects = [];
        try
        {
            if (!File.Exists(_catalogFilePath))
            {
                AppLog.Write($"Effect catalog not found: {_catalogFilePath}.");
                return false;
            }

            var document = JsonSerializer.Deserialize<EffectCatalogDocument>(
                File.ReadAllText(_catalogFilePath), JsonOptions);
            if (document is null)
            {
                AppLog.Write($"Effect catalog deserialized to null: {_catalogFilePath}.");
                return false;
            }

            if (document.SchemaVersion != 1)
            {
                AppLog.Write($"Effect catalog schemaVersion {document.SchemaVersion} is unsupported (expected 1): {_catalogFilePath}.");
                return false;
            }

            var validated = new List<EffectDefinition>(document.Effects.Count);
            foreach (var effect in document.Effects)
            {
                if (TryValidate(effect, out var reason))
                {
                    validated.Add(effect);
                }
                else
                {
                    AppLog.Write($"Effect '{effect.EffectId ?? "(no id)"}' rejected: {reason}");
                }
            }

            effects = validated;
            return true;
        }
        catch (JsonException ex)
        {
            AppLog.Write(ex, $"Effect catalog is malformed JSON: {_catalogFilePath}.");
            return false;
        }
        catch (IOException ex)
        {
            AppLog.Write(ex, $"Effect catalog read failed: {_catalogFilePath}.");
            return false;
        }
    }

    // Per-effect validation (T-13 deliverable #5). Returns false + a human-readable reason for the
    // rejection log; valid effects return true with a null reason.
    private static bool TryValidate(EffectDefinition effect, out string? reason)
    {
        if (string.IsNullOrWhiteSpace(effect.EffectId))
        {
            reason = "missing effectId";
            return false;
        }

        if (!Enum.TryParse<EffectCategory>(effect.Category, ignoreCase: true, out _))
        {
            reason = $"invalid category '{effect.Category}'";
            return false;
        }

        if (effect.EffectType is null || !EffectTypeVocabulary.Contains(effect.EffectType))
        {
            reason = $"invalid effectType '{effect.EffectType}'";
            return false;
        }

        if (effect.BaseIntensity is < 0 or > 1)
        {
            reason = $"baseIntensity {effect.BaseIntensity} out of range [0,1]";
            return false;
        }

        if (effect.FrequencyHz < 0)
        {
            reason = $"frequencyHz {effect.FrequencyHz} is negative";
            return false;
        }

        if (effect.DurationMs < 0)
        {
            reason = $"durationMs {effect.DurationMs} is negative";
            return false;
        }

        reason = null;
        return true;
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
