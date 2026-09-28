using System.Text.Json;
using System.Text.Json.Serialization;

using TyriaPad.Core.Diagnostics;

namespace TyriaPad.Core.Overlay;

/// <summary>
/// Saves the skill bar calibrations in <c>calibration.json</c>, next to the executable, with one
/// entry per resolution and interface size (e.g. "1920x1080@normal").
/// Used only from the UI thread.
/// </summary>
public sealed class CalibrationStore
{
    public const string FileName = "calibration.json";

    private static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly Dictionary<string, SkillBarCalibration> _entries = new(StringComparer.OrdinalIgnoreCase);

    public CalibrationStore(string directory)
    {
        Path = System.IO.Path.Combine(directory, FileName);
        Load();
    }

    public string Path { get; }

    public int Count => _entries.Count;

    public SkillBarCalibration? Get(string key) => _entries.GetValueOrDefault(key);

    public void Set(string key, SkillBarCalibration calibration)
    {
        _entries[key] = calibration;
        Save();
    }

    public bool Remove(string key)
    {
        bool removed = _entries.Remove(key);
        if (removed)
        {
            Save();
        }

        return removed;
    }

    /// <summary>Converts to and from the file's JSON. Public so it can be tested.</summary>
    public static string Serialize(IReadOnlyDictionary<string, SkillBarCalibration> entries)
        => JsonSerializer.Serialize(entries.ToDictionary(static e => e.Key, static e => Entry.From(e.Value)), s_json);

    public static Dictionary<string, SkillBarCalibration> Deserialize(string json)
    {
        var entries = JsonSerializer.Deserialize<Dictionary<string, Entry>>(json, s_json) ?? [];
        var result = new Dictionary<string, SkillBarCalibration>(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, Entry entry) in entries)
        {
            if (entry.ToCalibration() is { } calibration)
            {
                result[key] = calibration;
            }
        }

        return result;
    }

    private void Load()
    {
        if (!File.Exists(Path))
        {
            return;
        }

        try
        {
            foreach ((string key, SkillBarCalibration value) in Deserialize(File.ReadAllText(Path)))
            {
                _entries[key] = value;
            }

            Log.Write($"Calibration loaded: {_entries.Count} entries ({string.Join(", ", _entries.Keys)})");
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Write($"Could not read {Path}: {ex.Message}");
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(Path, Serialize(_entries));
            Log.Write($"Calibration saved to {Path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Could not save {Path}: {ex.Message}");
        }
    }

    private sealed class Entry
    {
        public PointEntry? Slot1 { get; set; }

        public PointEntry? Slot5 { get; set; }

        public PointEntry? Slot10 { get; set; }

        public PointEntry? F1 { get; set; }

        public PointEntry? F2 { get; set; }

        public static Entry From(SkillBarCalibration c) => new()
        {
            Slot1 = PointEntry.From(c.Slot1),
            Slot5 = PointEntry.From(c.Slot5),
            Slot10 = PointEntry.From(c.Slot10),
            F1 = PointEntry.From(c.F1),
            F2 = PointEntry.From(c.F2),
        };

        public SkillBarCalibration? ToCalibration()
            => Slot1 is { } s1 && Slot5 is { } s5 && Slot10 is { } s10 && F1 is { } f1 && F2 is { } f2
                ? new SkillBarCalibration(s1.ToPoint(), s5.ToPoint(), s10.ToPoint(), f1.ToPoint(), f2.ToPoint())
                : null;
    }

    private sealed class PointEntry
    {
        [JsonPropertyName("x")]
        public float X { get; set; }

        [JsonPropertyName("y")]
        public float Y { get; set; }

        public static PointEntry From(Point2 p) => new() { X = p.X, Y = p.Y };

        public Point2 ToPoint() => new(X, Y);
    }
}
