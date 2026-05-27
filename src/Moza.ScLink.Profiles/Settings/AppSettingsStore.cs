using System.IO;
using System.Text.Json;

namespace Moza.ScLink.Profiles.Settings;

public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions SaveOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public AppSettingsStore()
        : this(DefaultSettingsPath())
    {
    }

    // Test seam (internal + InternalsVisibleTo): lets unit tests point the store at a temp file so the
    // §14.2-#4 corrupted-file recovery and the GameLogPathProvider policy can be exercised hermetically,
    // without touching the real user settings file. The Load/Save recovery logic is unchanged — only the
    // path source is injectable. Precedent: DropRateMonitor's test ctor, MainViewModel's internal ctor.
    internal AppSettingsStore(string settingsFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFilePath);
        var directory = Path.GetDirectoryName(settingsFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _settingsPath = settingsFilePath;
    }

    private static string DefaultSettingsPath()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MozaStarCitizen");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "settings.json");
    }

    public AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, SaveOptions);
        File.WriteAllText(_settingsPath, json);
    }

    /// <summary>
    /// Loads the current settings, applies <paramref name="mutate"/>, and saves the result. Use this —
    /// not <see cref="Save"/> with a freshly constructed <see cref="AppSettings"/> — whenever a single
    /// field changes, so sibling fields are preserved. <see cref="Save"/> serializes the whole object,
    /// so <c>Save(new AppSettings { GameLogPath = x })</c> silently resets every other field to its
    /// default (e.g. <see cref="AppSettings.ForcePreviewMode"/> back to <c>false</c>). Load-mutate-save
    /// is correct for value fields, where a field-merging Save could not distinguish "unset" from
    /// "deliberately false".
    /// </summary>
    public void Update(Action<AppSettings> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        var settings = Load();
        mutate(settings);
        Save(settings);
    }
}
