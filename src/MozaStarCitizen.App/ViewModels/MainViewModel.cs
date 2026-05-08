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
using MozaStarCitizen.App.Settings;

namespace MozaStarCitizen.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly StarCitizenEventParser _parser = StarCitizenEventParser.LoadDefault();
    private readonly ForceFeedbackController _feedback;
    private readonly AppSettingsStore _settingsStore = new();
    private GameLogTailer? _tailer;
    private string _gameLogPath = string.Empty;
    private string _status = "Ready.";
    private bool _isMonitoring;

    public MainViewModel()
    {
        _feedback = new ForceFeedbackController(ForceFeedbackDeviceFactory.Create());

        AutoDetectCommand = new RelayCommand(_ => AutoDetectAsync());
        BrowseCommand = new RelayCommand(_ => BrowseAsync());
        StartCommand = new RelayCommand(_ => StartAsync(), _ => !IsMonitoring && File.Exists(GameLogPath));
        StopCommand = new RelayCommand(_ => StopAsync(), _ => IsMonitoring);
        TestQuantumCommand = new RelayCommand(_ => TestAsync(ScEventKind.QuantumSpoolStarted));
        TestImpactCommand = new RelayCommand(_ => TestAsync(ScEventKind.LandingImpact));
        TestAtmosphereCommand = new RelayCommand(_ => TestAsync(ScEventKind.AtmosphereEntered));
        RefreshDiagnosticsCommand = new RelayCommand(_ => RefreshDiagnosticsAsync());

        OutputName = _feedback.OutputName;
        OutputStatus = _feedback.OutputStatus;
        RefreshDiagnostics(includeExtendedDiagnostics: false);
        LoadInitialPath();
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

    private Task AutoDetectAsync()
    {
        var detected = StarCitizenLogLocator.FindGameLog();
        if (detected is null)
        {
            Status = "Game.log was not auto-detected. Use Browse once if Star Citizen is installed in a custom folder.";
            return Task.CompletedTask;
        }

        GameLogPath = detected;
        _settingsStore.Save(new AppSettings { GameLogPath = detected });
        Status = $"Detected Game.log: {detected}";
        return Task.CompletedTask;
    }

    private Task BrowseAsync()
    {
        var dialog = new OpenFileDialog
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
        }

        return Task.CompletedTask;
    }

    private async Task StartAsync()
    {
        if (!File.Exists(GameLogPath))
        {
            Status = "Game.log does not exist.";
            return;
        }

        try
        {
            await _feedback.InitializeAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Status = $"Force feedback output failed to initialize: {ex.Message}";
            return;
        }

        _tailer = new GameLogTailer(GameLogPath);
        _tailer.LineRead += OnLineRead;
        _tailer.Faulted += (_, message) => Dispatch(() => Status = $"Log read warning: {message}");
        _tailer.Start(startAtEnd: true);

        IsMonitoring = true;
        AddEvent("Monitoring started. New Star Citizen log lines will be parsed from this point forward.");
        Status = "Monitoring Game.log.";
    }

    private async Task StopAsync()
    {
        try
        {
            if (_tailer is not null)
            {
                _tailer.LineRead -= OnLineRead;
                await _tailer.StopAsync();
                _tailer.Dispose();
                _tailer = null;
            }

            await _feedback.StopAllAsync(CancellationToken.None);
            IsMonitoring = false;
            Status = "Monitoring stopped.";
            AddEvent("Monitoring stopped; all sustained effects were stopped.");
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
        var gameEvent = _parser.Parse(line);
        if (gameEvent is null)
        {
            return;
        }

        try
        {
            var result = await _feedback.HandleAsync(gameEvent, CancellationToken.None);
            Dispatch(() =>
            {
                AddEvent($"{gameEvent.Timestamp:HH:mm:ss} {gameEvent.Name}: {result}");
                Status = result;
            });
        }
        catch (Exception ex)
        {
            Dispatch(() => Status = $"Force feedback output failed: {ex.Message}");
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
        Diagnostics.Add($"Log file: {AppLog.LogPath}");
        Diagnostics.Add("Running extended diagnostics...");

        var diagnosticTask = Task.Run(() =>
            ForceFeedbackDiagnostics.GetLines(_feedback.Device, includeExtendedDiagnostics: true));
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(3));
        var completedTask = await Task.WhenAny(diagnosticTask, timeoutTask);

        if (completedTask != diagnosticTask)
        {
            Diagnostics.Clear();
            Diagnostics.Add($"Log file: {AppLog.LogPath}");
            foreach (var line in ForceFeedbackDiagnostics.GetLines(_feedback.Device, includeExtendedDiagnostics: false))
            {
                Diagnostics.Add(line);
            }

            Diagnostics.Add("Extended diagnostics timed out. The MOZA SDK probe may be blocking.");
            Status = "Diagnostics timed out.";
            return;
        }

        Diagnostics.Clear();
        Diagnostics.Add($"Log file: {AppLog.LogPath}");
        foreach (var line in await diagnosticTask)
        {
            Diagnostics.Add(line);
        }

        Status = "Diagnostics refreshed.";
    }

    private void RefreshDiagnostics(bool includeExtendedDiagnostics)
    {
        Diagnostics.Clear();
        Diagnostics.Add($"Log file: {AppLog.LogPath}");
        foreach (var line in ForceFeedbackDiagnostics.GetLines(_feedback.Device, includeExtendedDiagnostics))
        {
            Diagnostics.Add(line);
        }
    }

    private void LoadInitialPath()
    {
        var settings = _settingsStore.Load();
        if (!string.IsNullOrWhiteSpace(settings.GameLogPath) && File.Exists(settings.GameLogPath))
        {
            GameLogPath = settings.GameLogPath;
            Status = $"Using saved Game.log: {settings.GameLogPath}";
            return;
        }

        _ = AutoDetectAsync();
    }

    private void Dispatch(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
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
