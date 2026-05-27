using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Moza.ScLink.App.ForceFeedback;
using Moza.ScLink.App.GameLog;
using Moza.ScLink.Core.Bus;
using Moza.ScLink.Core.Devices;
using Moza.ScLink.Core.Diagnostics;
using Moza.ScLink.Core.Models;
using Moza.ScLink.Core.Sensors;
using Moza.ScLink.Diagnostics;
using Moza.ScLink.Profiles.Settings;

namespace Moza.ScLink.App.ViewModels;

/// <summary>
/// Bus-publishing UI shell (T-27). After the migration convergence the view model no longer drives the
/// device directly: the generic host owns the sensor → fusion → resolver → safety → output-worker
/// pipeline. The view model resolves the Game.log path (via <see cref="IGameLogPathProvider"/>), surfaces
/// the canonical device's identity/state, renders diagnostics, and publishes synthetic
/// <see cref="SensorEvent"/>s onto the bus for the manual Test buttons — establishing the "UI publishes
/// to the bus" pattern T-16 PR2 inherits. The legacy <c>ForceFeedbackController</c>/<c>GameLogTailer</c>
/// direct-drive path is removed (#45/#15).
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    // EventType strings mirror LogSensor.MapEventType so the synthetic Test events route through the same
    // fusion rules as real Game.log lines (phase1-rules.json). SensorKind.Log is the routing key; the
    // SensorId is deliberately distinct so diagnostics can tell test injections from real log evidence.
    private const string TestInjectionSensorId = "ui.test-injection";
    private const string QuantumSpoolStartEventType = "log.quantum_spool_start";
    private const string LandingImpactEventType = "log.landing_impact_candidate";
    private const string AtmosphereEnteredEventType = "log.atmosphere_entered";

    private readonly IEventBus _bus;
    private readonly IGameLogPathProvider _pathProvider;
    private readonly IForceFeedbackDevice _device;
    private readonly AppSettingsStore _settingsStore;

    // Non-null only when the canonical device is the no-hardware previewer (T-17). The cast — not a separate
    // DI registration — is the seam: a real VorticeDirectInputDevice does not implement IPreviewCommandSource,
    // so this is null for real hardware and IsPreviewMode is false (banner hidden).
    private readonly IPreviewCommandSource? _previewSource;
    private readonly IDisposable? _previewSubscription;

    private string _gameLogPath = string.Empty;
    private string _status = "Ready.";
    private string _outputName = string.Empty;
    private string _outputStatus = string.Empty;
    private bool _forcePreviewMode;

    public MainViewModel(
        IEventBus bus,
        IGameLogPathProvider pathProvider,
        IForceFeedbackDevice device,
        AppSettingsStore settingsStore)
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(pathProvider);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(settingsStore);
        _bus = bus;
        _pathProvider = pathProvider;
        _device = device;
        _settingsStore = settingsStore;
        _forcePreviewMode = settingsStore.Load().ForcePreviewMode;

        AutoDetectCommand = new RelayCommand(_ => AutoDetect());
        BrowseCommand = new RelayCommand(_ => Browse());
        TestQuantumCommand = new RelayCommand(_ =>
            PublishTestEvent(QuantumSpoolStartEventType, intensity: 0.42, duration: TimeSpan.FromSeconds(8)));
        TestImpactCommand = new RelayCommand(_ =>
            PublishTestEvent(LandingImpactEventType, intensity: 0.75, duration: TimeSpan.FromMilliseconds(260),
                features: ImmutableDictionary<string, double>.Empty.Add("relativeVelocityMagnitude", 13.0)));
        TestAtmosphereCommand = new RelayCommand(_ =>
            PublishTestEvent(AtmosphereEnteredEventType, intensity: 0.22, duration: TimeSpan.Zero));
        RefreshDiagnosticsCommand = new RelayCommand(_ => RefreshDiagnosticsAsync());

        OutputName = _device.DisplayName;
        OutputStatus = _device.State.ToUserFacingString();
        _device.StateChanged += OnDeviceStateChanged;

        // Preview seam: subscribe to the live command stream when the canonical device is the previewer.
        // The subject publishes on the pipeline's background thread, so AddPreviewCommand marshals onto the
        // UI thread via Dispatch. Subscription is disposed in Dispose.
        _previewSource = device as IPreviewCommandSource;
        _previewSubscription = _previewSource?.Commands.Subscribe(new PreviewObserver(this));

        ApplyResolution(_pathProvider.ResolveAtStartup());
        RefreshDiagnostics(includeExtendedDiagnostics: false);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string GameLogPath
    {
        get => _gameLogPath;
        set => SetField(ref _gameLogPath, value);
    }

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public string OutputName
    {
        get => _outputName;
        private set => SetField(ref _outputName, value);
    }

    public string OutputStatus
    {
        get => _outputStatus;
        private set => SetField(ref _outputStatus, value);
    }

    public ObservableCollection<string> Events { get; } = [];

    public ObservableCollection<string> Diagnostics { get; } = [];

    /// <summary>Last-50 preview command stream (T-17); populated only when <see cref="IsPreviewMode"/> is true.</summary>
    public ObservableCollection<PreviewedCommand> PreviewCommands { get; } = [];

    /// <summary>
    /// True when the canonical device is the no-hardware previewer — drives the amber preview banner.
    /// A real hardware device does not implement <see cref="IPreviewCommandSource"/>, so this is false.
    /// </summary>
    public bool IsPreviewMode => _previewSource is not null;

    /// <summary>
    /// User toggle to force preview mode on the next launch even with hardware present (T-17). Persisted to
    /// <see cref="AppSettings.ForcePreviewMode"/> via <see cref="AppSettingsStore.Update"/> (so a sibling
    /// GameLogPath is preserved). Applied once at startup — toggling it does not hot-swap the live device.
    /// </summary>
    public bool ForcePreviewMode
    {
        get => _forcePreviewMode;
        set
        {
            if (SetField(ref _forcePreviewMode, value))
            {
                _settingsStore.Update(s => s.ForcePreviewMode = value);
                AppLog.Write($"Force-preview-mode set to {value}; applies on next launch.");
            }
        }
    }

    public ICommand AutoDetectCommand { get; }

    public ICommand BrowseCommand { get; }

    public ICommand TestQuantumCommand { get; }

    public ICommand TestImpactCommand { get; }

    public ICommand TestAtmosphereCommand { get; }

    public ICommand RefreshDiagnosticsCommand { get; }

    // Commands are surfaced through RelayCommand (Func<object?, Task>); these affordances are synchronous,
    // so they complete the returned Task immediately.
    private Task AutoDetect()
    {
        var resolution = _pathProvider.AutoDetect();
        ApplyResolution(resolution);
        if (resolution.Origin == GameLogPathOrigin.None)
        {
            AppLog.Write("Auto-detect found no readable Game.log.");
        }
        else
        {
            AppLog.Write($"Auto-detect selected Game.log: {resolution.Path}. {GetLogFileSummary(resolution.Path!)}");
        }

        return Task.CompletedTask;
    }

    private Task Browse()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select Star Citizen Game.log",
            Filter = "Star Citizen Game.log|Game.log|Log files|*.log|All files|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            ApplyResolution(_pathProvider.UseExplicitPath(dialog.FileName));
            AppLog.Write($"Browse selected Game.log: {dialog.FileName}. {GetLogFileSummary(dialog.FileName)}");
        }

        return Task.CompletedTask;
    }

    // Publishes a synthetic SensorEvent straight onto the bus, exercising the live pipeline exactly as a
    // real Game.log line would (fusion → resolver → safety → output worker → device). Mirrors
    // LogSensor.ToSensorEvent so the manual Test affordance and the real sensor produce identical evidence.
    private Task PublishTestEvent(
        string eventType,
        double intensity,
        TimeSpan duration,
        ImmutableDictionary<string, double>? features = null)
    {
        var sensorEvent = new SensorEvent
        {
            EventId = Guid.NewGuid().ToString(),
            SensorId = TestInjectionSensorId,
            SensorKind = SensorKind.Log,
            EventType = eventType,
            Timestamp = DateTimeOffset.Now,
            Intensity = intensity,
            Duration = duration,
            Features = features ?? ImmutableDictionary<string, double>.Empty,
        };

        if (_bus.SensorEvents.TryWrite(sensorEvent))
        {
            AddEvent($"{sensorEvent.Timestamp:HH:mm:ss} Published test event {eventType} (intensity {intensity:0.##}).");
            Status = $"Published test event {eventType}.";
            AppLog.Write($"Published synthetic test SensorEvent {eventType} (intensity {intensity:0.##}).");
        }
        else
        {
            AddEvent($"{sensorEvent.Timestamp:HH:mm:ss} Test event dropped (bus full): {eventType}.");
            Status = $"Test event dropped (bus full): {eventType}.";
        }

        return Task.CompletedTask;
    }

    private void AddEvent(string message)
    {
        Events.Insert(0, message);
        while (Events.Count > 200)
        {
            Events.RemoveAt(Events.Count - 1);
        }
    }

    // Mirrors AddEvent's Insert(0)/RemoveAt cap idiom (capped at 50 per the acceptance criterion). Marshalled
    // onto the UI thread because the previewer's subject publishes on the pipeline's background thread.
    private void AddPreviewCommand(PreviewedCommand command)
    {
        Dispatch(() =>
        {
            PreviewCommands.Insert(0, command);
            while (PreviewCommands.Count > 50)
            {
                PreviewCommands.RemoveAt(PreviewCommands.Count - 1);
            }
        });
    }

    private async Task RefreshDiagnosticsAsync()
    {
        Status = "Refreshing diagnostics.";
        Diagnostics.Clear();
        AddLogDiagnostics();
        Diagnostics.Add("Running extended diagnostics...");

        var diagnosticTask = Task.Run(() =>
            ForceFeedbackDiagnostics.GetLines(_device, includeExtendedDiagnostics: true));
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(3));
        var completedTask = await Task.WhenAny(diagnosticTask, timeoutTask);

        if (completedTask != diagnosticTask)
        {
            Diagnostics.Clear();
            AddLogDiagnostics();
            foreach (var line in ForceFeedbackDiagnostics.GetLines(_device, includeExtendedDiagnostics: false))
            {
                Diagnostics.Add(line);
            }

            Diagnostics.Add("Extended diagnostics timed out.");
            Status = "Diagnostics timed out.";
            return;
        }

        Diagnostics.Clear();
        AddLogDiagnostics();
        foreach (var line in await diagnosticTask)
        {
            Diagnostics.Add(line);
        }

        Status = "Diagnostics refreshed.";
    }

    private void RefreshDiagnostics(bool includeExtendedDiagnostics)
    {
        Diagnostics.Clear();
        AddLogDiagnostics();
        foreach (var line in ForceFeedbackDiagnostics.GetLines(_device, includeExtendedDiagnostics))
        {
            Diagnostics.Add(line);
        }
    }

    private void AddLogDiagnostics()
    {
        Diagnostics.Add($"App log file: {AppLog.LogPath}");
        Diagnostics.Add($"Selected Game.log: {(string.IsNullOrWhiteSpace(GameLogPath) ? "(none)" : GameLogPath)}");
        if (!string.IsNullOrWhiteSpace(GameLogPath) && File.Exists(GameLogPath))
        {
            Diagnostics.Add($"Selected Game.log summary: {GetLogFileSummary(GameLogPath)}");
        }
    }

    // Renders a GameLogPathResolution onto the path textbox + status line. The host resolves the sensor's
    // path once at startup, so Auto-detect/Browse here persist the choice for the next launch (live
    // re-point is deferred to T-17).
    private void ApplyResolution(GameLogPathResolution resolution)
    {
        GameLogPath = resolution.Path ?? string.Empty;
        Status = resolution.Origin switch
        {
            GameLogPathOrigin.Saved => $"Using saved Game.log: {resolution.Path}",
            GameLogPathOrigin.ReplacedWithNewer => $"Detected newer Game.log: {resolution.Path}",
            GameLogPathOrigin.Detected => $"Detected Game.log: {resolution.Path}",
            GameLogPathOrigin.Explicit => $"Using Game.log: {resolution.Path} (applies on next launch).",
            _ => "Game.log was not auto-detected. Use Browse once if Star Citizen is installed in a custom folder.",
        };
    }

    private void OnDeviceStateChanged(object? sender, DeviceStateChangedEventArgs e)
    {
        Dispatch(() =>
        {
            OutputStatus = e.Current.ToUserFacingString();
            AppLog.Write($"Device state changed: {e.Previous} -> {e.Current} ({_device.DisplayName}).");
        });
    }

    private static void Dispatch(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }

    private static string GetLogFileSummary(string path)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            return $"Length: {fileInfo.Length} bytes. Last write: {fileInfo.LastWriteTime:O}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"File summary unavailable: {ex.Message}";
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    public void Dispose()
    {
        _device.StateChanged -= OnDeviceStateChanged;
        _previewSubscription?.Dispose();
    }

    // Forwards the previewer's command stream to the capped PreviewCommands collection. A small private
    // observer rather than making the public view model an IObserver — keeps the contract off the surface.
    private sealed class PreviewObserver(MainViewModel owner) : IObserver<PreviewedCommand>
    {
        public void OnNext(PreviewedCommand value) => owner.AddPreviewCommand(value);

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }
    }
}
