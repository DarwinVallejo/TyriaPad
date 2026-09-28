using System.Text.Json;

using TyriaPad.Core.Diagnostics;
using TyriaPad.Core.Mapping;

namespace TyriaPad.Core.Config;

/// <summary>Ready-to-use configuration: the settings plus the profile they name.</summary>
public sealed record LoadedConfig(TyriaPadSettings Settings, Profile Profile, string Source);

/// <summary>
/// Reads <c>config.json</c> and <c>profiles/&lt;profile&gt;.json</c> from the program folder, creates
/// the default files if they are missing and watches for changes to hot-reload. If a file has
/// errors they are written to the log and the last valid configuration is kept (at startup, the
/// built-in one). Changes are announced through <see cref="Changed"/> from a pool thread.
/// </summary>
public sealed class ConfigStore : IDisposable
{
    public const string ConfigFileName = "config.json";
    public const string ProfilesDirectoryName = "profiles";

    // Editors save in several steps; wait for them to finish before reading.
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

    private readonly Lock _lock = new();
    private readonly Timer _reloadTimer;
    private FileSystemWatcher? _configWatcher;
    private FileSystemWatcher? _profileWatcher;
    private bool _disposed;

    public ConfigStore(string directory)
    {
        Directory = directory;
        ConfigPath = Path.Combine(directory, ConfigFileName);
        ProfilesDirectory = Path.Combine(directory, ProfilesDirectoryName);
        Current = Defaults.Load();
        _reloadTimer = new Timer(_ => Reload(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public string Directory { get; }

    public string ConfigPath { get; }

    public string ProfilesDirectory { get; }

    /// <summary>Last valid configuration.</summary>
    public LoadedConfig Current { get; private set; }

    /// <summary>Errors from the last read, or null if it went fine.</summary>
    public string? LastError { get; private set; }

    /// <summary>Raised after every valid reload (the manual one too), from a pool thread.</summary>
    public event Action<LoadedConfig>? Changed;

    /// <summary>Creates the default files if they are missing, loads and starts watching the folder.</summary>
    public LoadedConfig Start()
    {
        EnsureDefaultFiles();
        TryLoad();
        StartWatching();
        return Current;
    }

    /// <summary>Reads the files again right now (tray menu).</summary>
    public void Reload()
    {
        if (_disposed)
        {
            return;
        }

        if (TryLoad())
        {
            Changed?.Invoke(Current);
        }
    }

    public string ProfilePath(string name) => Path.Combine(ProfilesDirectory, name + ".json");

    /// <summary>Names of the profiles in the folder (without ".json"), sorted.</summary>
    public IReadOnlyList<string> ListProfiles()
    {
        try
        {
            return System.IO.Directory.EnumerateFiles(ProfilesDirectory, "*.json")
                .Select(static p => Path.GetFileNameWithoutExtension(p))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Text of a profile in the folder, for the editor.</summary>
    public string ReadProfile(string name) => ReadWithRetry(ProfilePath(name));

    /// <summary>
    /// Writes to config.json the settings that differ from the file's, keeping its
    /// comments. The reload comes afterwards through the watcher (or through <see cref="Reload"/>).
    /// Throws <see cref="ConfigException"/> if the result would not be valid.
    /// </summary>
    public void SaveSettings(TyriaPadSettings settings)
    {
        string json = File.Exists(ConfigPath) ? ReadWithRetry(ConfigPath) : Defaults.ConfigJson;
        TyriaPadSettings onDisk;
        try
        {
            onDisk = ParseSettings(json, ConfigFileName);
        }
        catch (ConfigException ex)
        {
            // A hand-edited file with errors is not overwritten: what is in it would be lost.
            throw new ConfigException($"{ConfigFileName} has errors and is not overwritten; fix it or delete it (it is created again) before saving from here.{Environment.NewLine}{ex.Message}");
        }

        string updated = SettingsWriter.Apply(json, onDisk, settings);
        ParseSettings(updated, ConfigFileName);
        if (updated != json)
        {
            WriteAtomically(ConfigPath, updated);
            Log.Write("config.json saved from the settings");
        }
    }

    /// <summary>Writes <c>profiles/&lt;name&gt;.json</c> as is (the caller already validated it).</summary>
    public void SaveProfile(string name, string json)
    {
        System.IO.Directory.CreateDirectory(ProfilesDirectory);
        WriteAtomically(ProfilePath(name), json);
        Log.Write($"Profile saved from the settings: {name}.json");
    }

    // Written to a temporary file and replaced, so the watcher does not read a half-written file.
    private static void WriteAtomically(string path, string content)
    {
        string temp = path + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Reads and validates without touching the disk except to read. Public so it can be tested.</summary>
    public static LoadedConfig Load(string configPath, string profilesDirectory)
    {
        TyriaPadSettings settings = ParseSettings(ReadWithRetry(configPath), ConfigFileName);
        string profilePath = Path.Combine(profilesDirectory, settings.Profile + ".json");
        if (!File.Exists(profilePath))
        {
            throw new ConfigException($"{ConfigFileName}: profile \"{settings.Profile}\" does not exist ({profilePath})");
        }

        Profile profile;
        try
        {
            profile = ProfileParser.Parse(ReadWithRetry(profilePath));
        }
        catch (ConfigException ex)
        {
            throw new ConfigException($"{ProfilesDirectoryName}/{settings.Profile}.json:{Environment.NewLine}{ex.Message}");
        }

        return new LoadedConfig(settings, profile, profilePath);
    }

    public static TyriaPadSettings ParseSettings(string json, string source)
    {
        TyriaPadSettings? settings;
        try
        {
            settings = JsonSerializer.Deserialize<TyriaPadSettings>(json, ProfileParser.JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ConfigException($"{source}: invalid JSON: {ex.Message}");
        }

        settings ??= TyriaPadSettings.Default;
        IReadOnlyList<string> errors = settings.Validate();
        if (errors.Count > 0)
        {
            throw new ConfigException($"{source}:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}");
        }

        return settings;
    }

    public void Dispose()
    {
        _disposed = true;
        _reloadTimer.Dispose();
        _configWatcher?.Dispose();
        _profileWatcher?.Dispose();
    }

    private bool TryLoad()
    {
        lock (_lock)
        {
            try
            {
                Current = Load(ConfigPath, ProfilesDirectory);
                LastError = null;
                Log.Write($"Configuration loaded: profile \"{Current.Profile.Name}\" ({Current.Settings.Profile})");
                return true;
            }
            catch (Exception ex) when (ex is ConfigException or IOException or UnauthorizedAccessException)
            {
                LastError = ex.Message;
                Log.Write($"Configuration error (keeping the previous one):{Environment.NewLine}{ex.Message}");
                return false;
            }
        }
    }

    private void EnsureDefaultFiles()
    {
        try
        {
            System.IO.Directory.CreateDirectory(ProfilesDirectory);
            if (!File.Exists(ConfigPath))
            {
                File.WriteAllText(ConfigPath, Defaults.ConfigJson);
                Log.Write($"Created {ConfigPath}");
            }

            string defaultProfile = Path.Combine(ProfilesDirectory, Defaults.ProfileName + ".json");
            if (!File.Exists(defaultProfile))
            {
                File.WriteAllText(defaultProfile, Defaults.ProfileJson);
                Log.Write($"Created {defaultProfile}");
            }
            else if (Defaults.IsPreviousDefaultProfile(File.ReadAllText(defaultProfile)))
            {
                // It is the default profile of a previous version and was not edited: put in the new one (e.g. with M1).
                File.WriteAllText(defaultProfile, Defaults.ProfileJson);
                Log.Write($"Updated {defaultProfile} to this version's default profile (it had no changes of yours)");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Could not create the configuration files: {ex.Message}");
        }
    }

    private void StartWatching()
    {
        try
        {
            _configWatcher = Watch(Directory, ConfigFileName);
            _profileWatcher = Watch(ProfilesDirectory, "*.json");
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            Log.Write($"Cannot watch the configuration (edit it and use \"Reload\"): {ex.Message}");
        }
    }

    private FileSystemWatcher Watch(string directory, string filter)
    {
        var watcher = new FileSystemWatcher(directory, filter)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
        };
        watcher.Changed += OnFileChanged;
        watcher.Created += OnFileChanged;
        watcher.Renamed += OnFileChanged;
        watcher.Deleted += OnFileChanged;
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (!_disposed)
        {
            _reloadTimer.Change(Debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private static string ReadWithRetry(string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(50);
            }
        }
    }
}
