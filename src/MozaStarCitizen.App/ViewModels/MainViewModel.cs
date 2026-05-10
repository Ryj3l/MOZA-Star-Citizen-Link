using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using MozaStarCitizen.App.Diagnostics;
using MozaStarCitizen.App.ForceFeedback;
using MozaStarCitizen.App.Log;
using MozaStarCitizen.App.Models;
using MozaStarCitizen.App.Parsing;
using MozaStarCitizen.App.ScreenCapture;
using MozaStarCitizen.App.Settings;

namespace MozaStarCitizen.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly StarCitizenEventParser _parser = StarCitizenEventParser.LoadDefault();
    private readonly ForceFeedbackController _feedback;
    private readonly AppSettingsStore _settingsStore = new();
    private GameLogTailer? _tailer;
    private ScreenVisualEventMonitor? _screenMonitor;
    private string _gameLogPath = string.Empty;
    private string _status = "Ready.";
    private bool _isMonitoring;
    private bool _isStarting;
    private long _logLinesRead;
    private long _logEventsMatched;
    private long _screenEventsMatched;

    public MainViewModel()
    {
        _feedback = new ForceFeedbackController(ForceFeedbackDeviceFactory.Create());

        AutoDetectCommand = new RelayCommand(_ => AutoDetectAsync());
        BrowseCommand = new RelayCommand(_ => BrowseAsync());
        StartCommand = new RelayCommand(_ => StartAsync(), _ => !IsMonitoring && !_isStarting && File.Exists(GameLogPath));
        StopCommand = new RelayCommand(_ => StopAsync(), _ => IsMonitoring);
        TestQuantumCommand = new RelayCommand(_ => TestAsync(ScEventKind.QuantumSpoolStarted));
        TestImpactCommand = new RelayCommand(_ => TestAsync(ScEventKind.LandingImpact));
        TestAtmosphereCommand = new RelayCommand(_ => TestAsync(ScEventKind.AtmosphereEntered));
        RefreshDiagnosticsCommand = new RelayCommand(_ => RefreshDiagnosticsAsync());

        OutputName = _feedback.OutputName;
        OutputStatus = _feedback.OutputStatus;
        LoadInitialPath();
        RefreshDiagnostics(includeExtendedDiagnostics: false);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string GameLogPath
    {
        get => _gameLogPath;
        set
        {
            if (SetField(ref _gameLogPath, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public bool IsMonitoring
    {
        get => _isMonitoring;
        set
        {
            if (SetField(ref _isMonitoring, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string OutputName { get; }

    public string OutputStatus { get; }

    public ObservableCollection<string> Events { get; } = [];

    public ObservableCollection<string> Diagnostics { get; } = [];

    public ICommand AutoDetectCommand { get; }

    public ICommand BrowseCommand { get; }

    public ICommand StartCommand { get; }

    public ICommand StopCommand { get; }

    public ICommand TestQuantumCommand { get; }

    public ICommand TestImpactCommand { get; }

    public ICommand TestAtmosphereCommand { get; }

    public ICommand RefreshDiagnosticsCommand { get; }

    public async Task AutoStartAsync()
    {
        if (IsMonitoring || _isStarting)
        {
            return;
        }

        if (!File.Exists(GameLogPath))
        {
            AppLog.Write("Auto-start skipped because no readable Game.log is selected.");
            return;
        }

        AppLog.Write($"Auto-starting Game.log monitoring for '{GameLogPath}'.");
        await StartAsync();
    }

    private async Task AutoDetectAsync()
    {
        var detected = StarCitizenLogLocator.FindGameLog();
        if (detected is null)
        {
            Status = "Game.log was not auto-detected. Use Browse once if Star Citizen is installed in a custom folder.";
            return;
        }

        GameLogPath = detected;
        _settingsStore.Save(new AppSettings { GameLogPath = detected });
        AppLog.Write($"Auto-detect selected Game.log: {detected}. {GetLogFileSummary(detected)}");
        Status = $"Detected Game.log: {detected}";
        await AutoStartAsync();
    }

    private async Task BrowseAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select Star Citizen Game.log",
            Filter = "Star Citizen Game.log|Game.log|Log files|*.log|All files|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            GameLogPath = dialog.FileName;
            _settingsStore.Save(new AppSettings { GameLogPath = dialog.FileName });
            Status = $"Using Game.log: {dialog.FileName}";
            AppLog.Write($"Browse selected Game.log: {dialog.FileName}. {GetLogFileSummary(dialog.FileName)}");
            await AutoStartAsync();
        }
    }

    private async Task StartAsync()
    {
        if (IsMonitoring || _isStarting)
        {
            Status = BuildMonitoringStatus();
            return;
        }

        if (!File.Exists(GameLogPath))
        {
            Status = "Game.log does not exist.";
            return;
        }

        _isStarting = true;
        RaiseCommandStates();
        Status = "Starting Game.log monitoring.";

        try
        {
            await _feedback.InitializeAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Status = $"Force feedback output failed to initialize: {ex.Message}";
            AppLog.Write(ex, "Force feedback output failed to initialize while starting monitoring");
            return;
        }
        finally
        {
            _isStarting = false;
            RaiseCommandStates();
        }

        _tailer = new GameLogTailer(GameLogPath);
        _tailer.LineRead += OnLineRead;
        _tailer.Faulted += (_, message) =>
        {
            AppLog.Write($"Game.log read warning for '{GameLogPath}': {message}");
            Dispatch(() => Status = $"Log read warning: {message}");
        };
        _logLinesRead = 0;
        _logEventsMatched = 0;
        _screenEventsMatched = 0;
        AppLog.Write($"Starting Game.log monitoring at end of '{GameLogPath}'. Patterns loaded: {_parser.PatternCount}. {GetLogFileSummary(GameLogPath)}");
        _tailer.Start(startAtEnd: true);
        StartScreenMonitorIfEnabled();

        IsMonitoring = true;
        AddEvent("Monitoring started. New Star Citizen log lines will be parsed from this point forward.");
        Status = BuildMonitoringStatus();
    }

    private async Task StopAsync()
    {
        try
        {
            var wasMonitoring = IsMonitoring || _tailer is not null || _screenMonitor is not null;

            if (_tailer is not null)
            {
                _tailer.LineRead -= OnLineRead;
                await _tailer.StopAsync();
                _tailer.Dispose();
                _tailer = null;
            }

            if (_screenMonitor is not null)
            {
                _screenMonitor.EventDetected -= OnScreenEventDetected;
                _screenMonitor.Faulted -= OnScreenCaptureFaulted;
                await _screenMonitor.StopAsync();
                _screenMonitor.Dispose();
                _screenMonitor = null;
            }

            await _feedback.StopAllAsync(CancellationToken.None);
            if (!wasMonitoring)
            {
                IsMonitoring = false;
                Status = "Monitoring is not running.";
                AppLog.Write("Stop requested while Game.log monitoring was not active; stopped all active effects.");
                return;
            }

            IsMonitoring = false;
            Status = "Monitoring stopped.";
            AddEvent($"Monitoring stopped; read {_logLinesRead} line(s), matched {_logEventsMatched} log event(s), {_screenEventsMatched} screen event(s), and stopped all sustained effects.");
            AppLog.Write($"Stopped Game.log monitoring. Lines read: {_logLinesRead}. Events matched: {_logEventsMatched}. {GetLogFileSummary(GameLogPath)}");
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Stop failed");
            IsMonitoring = false;
            Status = $"Monitoring stopped, but force feedback cleanup failed: {ex.Message}";
            AddEvent($"Stop warning: {ex.Message}");
        }
    }

    private async void OnLineRead(object? sender, string line)
    {
        try
        {
            var linesRead = Interlocked.Increment(ref _logLinesRead);
            if (linesRead <= 5 || linesRead % 250 == 0)
            {
                var matched = Interlocked.Read(ref _logEventsMatched);
                AppLog.Write($"Game.log tailer read {linesRead} line(s); matched {matched} event(s).");
                Dispatch(() => Status = BuildMonitoringStatus());
            }

            var gameEvent = _parser.Parse(line);
            if (gameEvent is null)
            {
                return;
            }

            var matches = Interlocked.Increment(ref _logEventsMatched);
            AppLog.Write($"Matched Game.log event #{matches}: {gameEvent.Kind} '{gameEvent.Name}' from {TrimLogLine(line)}");
            await HandleGameEventAsync(gameEvent);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Game.log line handling failed");
            Dispatch(() => Status = $"Game.log event handling failed: {ex.Message}");
        }
    }

    private async void OnScreenEventDetected(object? sender, ScGameEvent gameEvent)
    {
        try
        {
            var matches = Interlocked.Increment(ref _screenEventsMatched);
            AppLog.Write($"Matched screen-capture event #{matches}: {gameEvent.Kind} '{gameEvent.Name}' from {TrimLogLine(gameEvent.SourceLine)}");
            await HandleGameEventAsync(gameEvent);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Screen-capture event handling failed");
            Dispatch(() => Status = $"Screen-capture event handling failed: {ex.Message}");
        }
    }

    private async Task TestAsync(ScEventKind kind)
    {
        var gameEvent = kind switch
        {
            ScEventKind.QuantumSpoolStarted => new ScGameEvent(kind, "Quantum spool test", 0.42, TimeSpan.FromSeconds(8), "test", DateTimeOffset.Now),
            ScEventKind.LandingImpact => new ScGameEvent(kind, "Landing/impact test", 0.75, TimeSpan.FromMilliseconds(260), "test", DateTimeOffset.Now),
            ScEventKind.AtmosphereEntered => new ScGameEvent(kind, "In-atmosphere test", 0.22, TimeSpan.Zero, "test", DateTimeOffset.Now),
            _ => new ScGameEvent(kind, "Effect test", 0.5, TimeSpan.FromMilliseconds(500), "test", DateTimeOffset.Now)
        };

        try
        {
            await _feedback.InitializeAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Status = $"Force feedback output failed to initialize: {ex.Message}";
            return;
        }

        try
        {
            var result = await _feedback.HandleAsync(gameEvent, CancellationToken.None);
            AddEvent($"{gameEvent.Timestamp:HH:mm:ss} {gameEvent.Name}: {result}");
            Status = result;
        }
        catch (Exception ex)
        {
            Status = $"Force feedback output failed: {ex.Message}";
            AddEvent($"{gameEvent.Timestamp:HH:mm:ss} {gameEvent.Name}: failed - {ex.Message}");
        }
    }

    private void AddEvent(string message)
    {
        Events.Insert(0, message);
        while (Events.Count > 200)
        {
            Events.RemoveAt(Events.Count - 1);
        }
    }

    private async Task RefreshDiagnosticsAsync()
    {
        Status = "Refreshing diagnostics.";
        Diagnostics.Clear();
        AddLogDiagnostics();
        Diagnostics.Add("Running extended diagnostics...");

        var diagnosticTask = Task.Run(() =>
            ForceFeedbackDiagnostics.GetLines(_feedback.Device, includeExtendedDiagnostics: true));
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(3));
        var completedTask = await Task.WhenAny(diagnosticTask, timeoutTask);

        if (completedTask != diagnosticTask)
        {
            Diagnostics.Clear();
            AddLogDiagnostics();
            foreach (var line in ForceFeedbackDiagnostics.GetLines(_feedback.Device, includeExtendedDiagnostics: false))
            {
                Diagnostics.Add(line);
            }

            Diagnostics.Add("Extended diagnostics timed out. The MOZA SDK probe may be blocking.");
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
        foreach (var line in ForceFeedbackDiagnostics.GetLines(_feedback.Device, includeExtendedDiagnostics))
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

        Diagnostics.Add($"Event patterns loaded: {_parser.PatternCount}");
        Diagnostics.Add($"Monitoring read/matched: {_logLinesRead}/{_logEventsMatched}");
        Diagnostics.Add($"Screen capture: {GetScreenCaptureSummary()}");
    }

    private string GetScreenCaptureSummary()
    {
        if (!ScreenVisualEventMonitor.IsEnabledByEnvironment())
        {
            return "disabled; set MOZA_SC_SCREEN=1 or use Run-Screen.cmd to test visual impact detection";
        }

        if (_screenMonitor is null)
        {
            return "enabled but not running";
        }

        return $"{_screenMonitor.Status}; frames analyzed: {_screenMonitor.FramesAnalyzed}; events matched: {_screenMonitor.EventsDetected}";
    }

    private void LoadInitialPath()
    {
        var settings = _settingsStore.Load();
        var detected = StarCitizenLogLocator.FindGameLog();
        if (!string.IsNullOrWhiteSpace(settings.GameLogPath) && File.Exists(settings.GameLogPath))
        {
            if (!string.IsNullOrWhiteSpace(detected) &&
                !PathsEqual(settings.GameLogPath, detected) &&
                IsNewerLog(detected, settings.GameLogPath))
            {
                GameLogPath = detected;
                _settingsStore.Save(new AppSettings { GameLogPath = detected });
                AppLog.Write($"Replaced saved Game.log with newer auto-detected log: {detected}. Previous saved log: {settings.GameLogPath}.");
                Status = $"Detected newer Game.log: {detected}";
                return;
            }

            GameLogPath = settings.GameLogPath;
            AppLog.Write($"Using saved Game.log: {settings.GameLogPath}. {GetLogFileSummary(settings.GameLogPath)}");
            Status = $"Using saved Game.log: {settings.GameLogPath}";
            return;
        }

        if (!string.IsNullOrWhiteSpace(detected))
        {
            GameLogPath = detected;
            _settingsStore.Save(new AppSettings { GameLogPath = detected });
            AppLog.Write($"Initial auto-detect selected Game.log: {detected}. {GetLogFileSummary(detected)}");
            Status = $"Detected Game.log: {detected}";
            return;
        }

        Status = "Game.log was not auto-detected. Use Browse once if Star Citizen is installed in a custom folder.";
    }

    private void Dispatch(Action action)
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

    private static Task DispatchAsync(Func<Task> action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            return action();
        }

        return dispatcher.InvokeAsync(action).Task.Unwrap();
    }

    private async Task HandleGameEventAsync(ScGameEvent gameEvent)
    {
        await DispatchAsync(async () =>
        {
            try
            {
                var result = await _feedback.HandleAsync(gameEvent, CancellationToken.None);
                AddEvent($"{gameEvent.Timestamp:HH:mm:ss} {gameEvent.Name}: {result}");
                Status = $"{result} ({BuildMonitoringStatus()})";
            }
            catch (Exception ex)
            {
                AppLog.Write(ex, $"Force feedback output failed for event {gameEvent.Kind}");
                Status = $"Force feedback output failed: {ex.Message}";
                AddEvent($"{gameEvent.Timestamp:HH:mm:ss} {gameEvent.Name}: failed - {ex.Message}");
            }
        });
    }

    private void StartScreenMonitorIfEnabled()
    {
        if (!ScreenVisualEventMonitor.IsEnabledByEnvironment())
        {
            AppLog.Write("Screen capture monitor disabled. Set MOZA_SC_SCREEN=1 to enable experimental visual impact detection.");
            return;
        }

        _screenMonitor = new ScreenVisualEventMonitor();
        _screenMonitor.EventDetected += OnScreenEventDetected;
        _screenMonitor.Faulted += OnScreenCaptureFaulted;
        _screenMonitor.Start();
        AddEvent("Experimental screen-capture impact detection enabled.");
    }

    private void OnScreenCaptureFaulted(object? sender, string message)
    {
        AppLog.Write(message);
        Dispatch(() => Status = message);
    }

    private string BuildMonitoringStatus() =>
        $"Monitoring Game.log. Lines read: {_logLinesRead}; log events matched: {_logEventsMatched}; screen events matched: {_screenEventsMatched}.";

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

    private static string TrimLogLine(string line)
    {
        const int maxLength = 260;
        var trimmed = line.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : string.Concat(trimmed.AsSpan(0, maxLength), "...");
    }

    private static bool PathsEqual(string first, string second)
    {
        try
        {
            return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool IsNewerLog(string candidatePath, string currentPath)
    {
        try
        {
            var candidate = new FileInfo(candidatePath);
            var current = new FileInfo(currentPath);
            return candidate.LastWriteTimeUtc > current.LastWriteTimeUtc.AddSeconds(5);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
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

    private void RaiseCommandStates()
    {
        foreach (var command in new[] { StartCommand, StopCommand })
        {
            if (command is RelayCommand relayCommand)
            {
                relayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public void Dispose()
    {
        _ = StopAsync();
    }
}
