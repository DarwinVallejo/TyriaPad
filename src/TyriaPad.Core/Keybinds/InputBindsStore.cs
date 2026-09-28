using TyriaPad.Core.Diagnostics;

namespace TyriaPad.Core.Keybinds;

/// <summary>
/// Finds the keybinds XML GW2 exports to the InputBinds folder, reads it and watches the
/// folder to reload when it is exported again. GW2 doesn't write changes there by itself: the
/// active configuration lives in Local.dat, so the binds have to be exported after changing them.
/// Without a file (or with <c>inputBinds: "none"</c>) the game's default keys are used.
/// Changes are announced through <see cref="Changed"/> from a pool thread.
/// </summary>
public sealed class InputBindsStore : IDisposable
{
    public const string Disabled = "none";

    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(500);

    private readonly Lock _lock = new();
    private readonly Timer _reloadTimer;
    private readonly string _directory;
    private FileSystemWatcher? _watcher;
    private string _setting;
    private bool _disposed;

    /// <param name="setting">Value of <c>inputBinds</c>: empty = the newest in the folder; "none"; a name or a path.</param>
    /// <param name="directory">InputBinds folder; by default the one in Documents.</param>
    public InputBindsStore(string setting, string? directory = null)
    {
        _setting = setting;
        _directory = directory ?? DefaultDirectory;
        _reloadTimer = new Timer(_ => Reload(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public static string DefaultDirectory
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Guild Wars 2", "InputBinds");

    public Gw2Keybinds Current { get; private set; } = Gw2Keybinds.Defaults;

    /// <summary>Raised after every reload (exported again or the setting changed).</summary>
    public event Action<Gw2Keybinds>? Changed;

    public Gw2Keybinds Start()
    {
        Current = Load(_setting, _directory);
        StartWatching();
        return Current;
    }

    /// <summary>Changes the value of <c>inputBinds</c> (when config.json reloads) and reads again if needed.</summary>
    /// <returns>true if it read again (and announced it through <see cref="Changed"/>).</returns>
    public bool SetSetting(string setting)
    {
        lock (_lock)
        {
            if (setting == _setting)
            {
                return false;
            }

            _setting = setting;
        }

        Reload();
        return true;
    }

    public void Reload()
    {
        if (_disposed)
        {
            return;
        }

        Gw2Keybinds binds;
        lock (_lock)
        {
            binds = Load(_setting, _directory);
            Current = binds;
        }

        Changed?.Invoke(binds);
    }

    public void Dispose()
    {
        _disposed = true;
        _reloadTimer.Dispose();
        _watcher?.Dispose();
    }

    /// <summary>Resolves the file from the setting and reads it; on any failure, default values. Public so it can be tested.</summary>
    public static Gw2Keybinds Load(string setting, string directory)
    {
        string? path = Resolve(setting, directory);
        if (path is null)
        {
            Log.Write(setting.Trim().Equals(Disabled, StringComparison.OrdinalIgnoreCase)
                ? "GW2 keybinds: disabled (inputBinds = none), using the default keys"
                : $"GW2 keybinds: no XML in {directory}; using the default keys (export them from Options → Control Options)");
            return Gw2Keybinds.Defaults;
        }

        try
        {
            InputBindsFile file = InputBindsParser.Parse(ReadWithRetry(path));
            Log.Write($"GW2 keybinds read from {path}: {file.Overrides.Count} actions changed");
            return Gw2Keybinds.FromFile(file, path);
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            Log.Write($"GW2 keybinds: could not read {path} ({ex.Message}); using the default keys");
            return Gw2Keybinds.Defaults;
        }
    }

    /// <summary>Path of the XML to read, or null if none should be read.</summary>
    public static string? Resolve(string setting, string directory)
    {
        string value = setting.Trim();
        if (value.Equals(Disabled, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (value.Length > 0)
        {
            string path = Path.IsPathRooted(value) ? value : Path.Combine(directory, value);
            if (!File.Exists(path) && !path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && File.Exists(path + ".xml"))
            {
                path += ".xml";
            }

            return File.Exists(path) ? path : null;
        }

        if (!Directory.Exists(directory))
        {
            return null;
        }

        return new DirectoryInfo(directory).EnumerateFiles("*.xml")
            .OrderByDescending(static f => f.LastWriteTimeUtc)
            .FirstOrDefault()?.FullName;
    }

    private void StartWatching()
    {
        // The whole "Guild Wars 2" folder is watched because InputBinds doesn't exist until the first export.
        string? parent = Path.GetDirectoryName(_directory);
        string? root = Directory.Exists(_directory) ? _directory : parent is not null && Directory.Exists(parent) ? parent : null;
        if (root is null)
        {
            Log.Write($"GW2 keybinds: {_directory} does not exist; not watched (use \"Reload configuration\" after saving them)");
            return;
        }

        try
        {
            _watcher = new FileSystemWatcher(root, "*.xml")
            {
                IncludeSubdirectories = root != _directory,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
            };
            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            _watcher.Renamed += OnFileChanged;
            _watcher.Deleted += OnFileChanged;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            Log.Write($"GW2 keybinds: cannot watch {root}: {ex.Message}");
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        string? folder = Path.GetDirectoryName(e.FullPath);
        if (!_disposed && string.Equals(folder, _directory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
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
