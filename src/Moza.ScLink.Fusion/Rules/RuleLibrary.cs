using System.IO;
using System.Text.Json;
using System.Threading;
using Moza.ScLink.Core.Diagnostics;
using Moza.ScLink.Core.Models;

namespace Moza.ScLink.Fusion.Rules;

/// <summary>
/// Loads a versioned fusion-rules document (<see cref="FusionRuleDocument"/>) into an immutable list of
/// validated <see cref="FusionRule"/>s and hot-reloads it on file change (FileSystemWatcher + debounce).
/// The current set is swapped atomically; consumers read <see cref="Current"/> and pick up reloads
/// without re-subscribing.
/// <para>
/// Error policy mirrors <c>EffectCatalog</c> / <c>PatternLibrary</c>: STRUCTURAL failure (missing file /
/// malformed JSON / unsupported schemaVersion) yields an EMPTY set on INITIAL load ("load defaults") and
/// RETAINS the current good set on RELOAD. Per-rule validation failures (bad ruleId / producesEventType /
/// requirement kind / out-of-range minConfidence / zero requirements) drop only the offending rule with a
/// logged message; valid rules load.
/// </para>
/// </summary>
public sealed class RuleLibrary : IDisposable
{
    private static readonly TimeSpan DefaultDebounceWindow = TimeSpan.FromMilliseconds(300);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _rulesFilePath;
    private readonly TimeSpan _debounceWindow;
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _debounceTimer;
    private readonly object _gate = new();
    private IReadOnlyList<FusionRule> _current;
    private bool _reloadInProgress;
    private bool _disposed;

    public RuleLibrary(string rulesFilePath, TimeSpan? debounceWindow = null)
    {
        ArgumentNullException.ThrowIfNull(rulesFilePath);
        _rulesFilePath = Path.GetFullPath(rulesFilePath);
        _debounceWindow = debounceWindow ?? DefaultDebounceWindow;

        // Initial load: empty set on structural failure (missing/malformed/unsupported version).
        _current = TryLoad(out var rules) ? rules : [];

        _debounceTimer = new Timer(_ => Reload(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        _watcher = new FileSystemWatcher(
            Path.GetDirectoryName(_rulesFilePath)!,
            Path.GetFileName(_rulesFilePath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
        _watcher.Renamed += OnFileChanged;
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>Resolves <c>Rules/phase1-rules.json</c> next to the executable with the default debounce.</summary>
    public static RuleLibrary LoadDefault() =>
        new(Path.Combine(AppContext.BaseDirectory, "Rules", "phase1-rules.json"));

    /// <summary>Current validated rule set. Thread-safe; swapped atomically on a successful reload.</summary>
    public IReadOnlyList<FusionRule> Current => Volatile.Read(ref _current);

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
            if (!TryLoad(out var rules))
            {
                return;  // structural failure -> retain the current good set
            }

            Interlocked.Exchange(ref _current, rules);
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

    private bool TryLoad(out IReadOnlyList<FusionRule> rules)
    {
        rules = [];
        try
        {
            if (!File.Exists(_rulesFilePath))
            {
                AppLog.Write($"Fusion rules not found: {_rulesFilePath}.");
                return false;
            }

            var document = JsonSerializer.Deserialize<FusionRuleDocument>(
                File.ReadAllText(_rulesFilePath), JsonOptions);
            if (document is null)
            {
                AppLog.Write($"Fusion rules deserialized to null: {_rulesFilePath}.");
                return false;
            }

            if (document.SchemaVersion != 1)
            {
                AppLog.Write($"Fusion rules schemaVersion {document.SchemaVersion} is unsupported (expected 1): {_rulesFilePath}.");
                return false;
            }

            var validated = new List<FusionRule>(document.Rules.Count);
            foreach (var dto in document.Rules)
            {
                if (TryValidate(dto, out var rule, out var reason))
                {
                    validated.Add(rule);
                }
                else
                {
                    AppLog.Write($"Fusion rule '{dto.RuleId ?? "(no id)"}' rejected: {reason}");
                }
            }

            rules = validated;
            return true;
        }
        catch (JsonException ex)
        {
            AppLog.Write(ex, $"Fusion rules are malformed JSON: {_rulesFilePath}.");
            return false;
        }
        catch (IOException ex)
        {
            AppLog.Write(ex, $"Fusion rules read failed: {_rulesFilePath}.");
            return false;
        }
    }

    // Per-rule validation (T-12 deliverable #3). Returns false + a human-readable reason for the rejection
    // log; valid rules return true with the built domain FusionRule and a null reason.
    private static bool TryValidate(FusionRuleDto dto, out FusionRule rule, out string? reason)
    {
        rule = null!;

        if (string.IsNullOrWhiteSpace(dto.RuleId))
        {
            reason = "missing ruleId";
            return false;
        }

        if (!Enum.TryParse<GameEventType>(dto.ProducesEventType, ignoreCase: true, out var producesEventType))
        {
            reason = $"invalid producesEventType '{dto.ProducesEventType}'";
            return false;
        }

        if (dto.Requirements.Count == 0)
        {
            reason = "no requirements";
            return false;
        }

        if (dto.MinConfidence is < 0 or > 1)
        {
            reason = $"minConfidence {dto.MinConfidence} out of range [0,1]";
            return false;
        }

        var requirements = new List<EvidenceRequirement>(dto.Requirements.Count);
        foreach (var req in dto.Requirements)
        {
            if (!Enum.TryParse<SensorKind>(req.Kind, ignoreCase: true, out var kind))
            {
                reason = $"requirement has invalid kind '{req.Kind}'";
                return false;
            }

            requirements.Add(new EvidenceRequirement(kind, req.EventType ?? string.Empty, req.Weight, req.Required));
        }

        // A blank suppressionKey defaults to the ruleId so malformed input cannot collapse distinct rules
        // into one debounce bucket. All shipped rules carry an explicit key.
        var suppressionKey = string.IsNullOrWhiteSpace(dto.SuppressionKey) ? dto.RuleId : dto.SuppressionKey;

        rule = new FusionRule(
            dto.RuleId,
            producesEventType,
            dto.Description ?? string.Empty,
            requirements,
            TimeSpan.FromMilliseconds(dto.EvidenceWindowMs),
            dto.MinConfidence,
            suppressionKey);
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
