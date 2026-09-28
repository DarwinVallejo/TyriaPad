using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Text.Json;

using TyriaPad.Core.Mapping;
using TyriaPad.Core.Overlay;

namespace TyriaPad.Core.Game;

/// <summary>Bits of <c>uiState</c> in GW2's MumbleLink context.</summary>
[Flags]
public enum UiState : uint
{
    None = 0,
    MapOpen = 1 << 0,
    CompassTopRight = 1 << 1,
    CompassRotationEnabled = 1 << 2,
    GameHasFocus = 1 << 3,
    InCompetitiveMode = 1 << 4,
    TextboxHasFocus = 1 << 5,
    InCombat = 1 << 6,
}

public readonly record struct MumbleLinkData(
    uint UiVersion,
    uint UiTick,
    uint MapId,
    UiState UiState,
    uint ProcessId,
    byte MountIndex)
{
    // Mumble's LinkedMem struct: the header takes 1108 bytes and then comes context[256],
    // which GW2 fills with its own struct. identity is wchar_t[256] holding a JSON
    // ({"name":…, "profession":…, "uisz":1, …}).
    internal const int IdentityOffset = 592;
    internal const int IdentityLength = 256 * 2;
    internal const int ContextOffset = 1108;
    internal const int MapIdOffset = ContextOffset + 28;
    internal const int UiStateOffset = ContextOffset + 48;
    internal const int ProcessIdOffset = ContextOffset + 80;
    internal const int MountIndexOffset = ContextOffset + 84;
    internal const int MinimumSize = MountIndexOffset + 1;

    public static MumbleLinkData Parse(ReadOnlySpan<byte> link)
    {
        return new MumbleLinkData(
            BinaryPrimitives.ReadUInt32LittleEndian(link),
            BinaryPrimitives.ReadUInt32LittleEndian(link[4..]),
            BinaryPrimitives.ReadUInt32LittleEndian(link[MapIdOffset..]),
            (UiState)BinaryPrimitives.ReadUInt32LittleEndian(link[UiStateOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(link[ProcessIdOffset..]),
            link[MountIndexOffset]);
    }

    /// <summary>Interface size from the <c>identity</c> JSON, or null if it can't be read.</summary>
    public static UiSize? ParseUiSize(ReadOnlySpan<byte> identityUtf16)
    {
        int end = 0;
        while (end + 1 < identityUtf16.Length && (identityUtf16[end] != 0 || identityUtf16[end + 1] != 0))
        {
            end += 2;
        }

        if (end == 0)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(Encoding.Unicode.GetString(identityUtf16[..end]));
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("uisz", out JsonElement uisz)
                && uisz.TryGetInt32(out int value)
                && value is >= 0 and <= 3)
            {
                return (UiSize)value;
            }
        }
        catch (JsonException)
        {
            // GW2 writes identity in several steps; retried on the next read.
        }

        return null;
    }
}

/// <summary>
/// Reads the "MumbleLink" shared memory that GW2 publishes for external programs.
/// It's the path ArenaNet officially offers; it doesn't read the game's memory.
/// Thread-safe: used by the poller thread and the log heartbeat.
/// </summary>
public sealed class MumbleLinkReader : IDisposable
{
    public const string DefaultName = "MumbleLink";

    /// <summary>sizeof(LinkedMem): header + context[256] + description[2048] (wchar_t).</summary>
    internal const int LinkedMemSize = MumbleLinkData.ContextOffset + 256 + (2048 * 2);

    // If uiTick stops advancing (loading screen, game closed) the data is no longer reliable.
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(1);

    private readonly long _start = Stopwatch.GetTimestamp();
    private readonly MemoryMappedFile _file;
    private readonly MemoryMappedViewAccessor _view;
    private readonly byte[] _buffer = new byte[MumbleLinkData.MinimumSize];
    private readonly byte[] _identity = new byte[MumbleLinkData.IdentityLength];
    private readonly byte[] _lastIdentity = new byte[MumbleLinkData.IdentityLength];
    private readonly Lock _lock = new();
    private uint _lastTick;
    private TimeSpan? _lastTickChange;
    private UiSize? _uiSize;

    public MumbleLinkReader(string name = DefaultName)
    {
        // CreateOrOpen: if GW2 hasn't started yet, we create it and GW2 will open the same one.
        _file = MemoryMappedFile.CreateOrOpen(name, LinkedMemSize, MemoryMappedFileAccess.ReadWrite);
        _view = _file.CreateViewAccessor(0, MumbleLinkData.MinimumSize, MemoryMappedFileAccess.Read);
        _lastIdentity[0] = 0xFF; // differs from any real identity so the first read gets parsed
    }

    public MumbleLinkData Read()
    {
        lock (_lock)
        {
            return ReadUnlocked();
        }
    }

    /// <summary>True if a text box has focus (chat, search…) and the data is up to date.</summary>
    public bool IsTextboxFocused() => IsTextboxFocused(Now);

    /// <summary>True if GW2 itself says it has focus and the data is up to date. Doesn't depend on the Windows shell.</summary>
    public bool IsGameFocused() => HasFreshFlag(UiState.GameHasFocus, Now);

    /// <summary>Mount and map open, or nothing if the data isn't up to date.</summary>
    public GameSignals GetSignals()
    {
        lock (_lock)
        {
            (MumbleLinkData data, bool fresh) = Refresh(Now);
            return fresh ? new GameSignals(data.MountIndex, (data.UiState & UiState.MapOpen) != 0) : default;
        }
    }

    /// <summary>Interface flags, or null if the game isn't updating MumbleLink (loading, closed).</summary>
    public UiState? GetUiState()
    {
        lock (_lock)
        {
            (MumbleLinkData data, bool fresh) = Refresh(Now);
            return fresh ? data.UiState : null;
        }
    }

    /// <summary>
    /// GW2 interface size (identity.uisz). The JSON is only parsed again when it changes.
    /// The last known value is kept even if the data stops being up to date.
    /// </summary>
    public UiSize? GetUiSize()
    {
        lock (_lock)
        {
            _view.ReadArray(MumbleLinkData.IdentityOffset, _identity, 0, _identity.Length);
            if (!_identity.AsSpan().SequenceEqual(_lastIdentity))
            {
                _identity.CopyTo(_lastIdentity, 0);
                _uiSize = MumbleLinkData.ParseUiSize(_identity) ?? _uiSize;
            }

            return _uiSize;
        }
    }

    /// <summary>Summary for the log.</summary>
    public string Describe()
    {
        lock (_lock)
        {
            (MumbleLinkData data, bool fresh) = Refresh(Now);
            return data.UiTick == 0
                ? "no data (GW2 not in the world)"
                : $"tick {data.UiTick} {(fresh ? "up to date" : "stalled")}, uiState {data.UiState}, pid {data.ProcessId}";
        }
    }

    internal bool IsTextboxFocused(TimeSpan now) => HasFreshFlag(UiState.TextboxHasFocus, now);

    private TimeSpan Now => Stopwatch.GetElapsedTime(_start);

    private bool HasFreshFlag(UiState flag, TimeSpan now)
    {
        lock (_lock)
        {
            (MumbleLinkData data, bool fresh) = Refresh(now);
            return fresh && (data.UiState & flag) != 0;
        }
    }

    private (MumbleLinkData Data, bool Fresh) Refresh(TimeSpan now)
    {
        MumbleLinkData data = ReadUnlocked();
        if (data.UiTick != _lastTick)
        {
            _lastTick = data.UiTick;
            _lastTickChange = now;
        }

        bool fresh = _lastTickChange is { } changedAt && now - changedAt < StaleAfter;
        return (data, fresh);
    }

    private MumbleLinkData ReadUnlocked()
    {
        _view.ReadArray(0, _buffer, 0, _buffer.Length);
        return MumbleLinkData.Parse(_buffer);
    }

    public void Dispose()
    {
        _view.Dispose();
        _file.Dispose();
    }
}