using System.IO.MemoryMappedFiles;

using TyriaPad.Core.Game;

namespace TyriaPad.Core.Tests;

public sealed class MumbleLinkReaderTests : IDisposable
{
    private readonly string _name = $"TyriaPadTest-{Guid.NewGuid():N}";
    private readonly MemoryMappedFile _game;
    private readonly MemoryMappedViewAccessor _gameView;
    private readonly MumbleLinkReader _reader;

    public MumbleLinkReaderTests()
    {
        // Simulates GW2 writing to the shared memory.
        _game = MemoryMappedFile.CreateNew(_name, MumbleLinkReader.LinkedMemSize);
        _gameView = _game.CreateViewAccessor();
        _reader = new MumbleLinkReader(_name);
    }

    [Fact]
    public void Read_ParsesGw2Context()
    {
        Write(tick: 42, UiState.InCombat | UiState.GameHasFocus);
        _gameView.Write(MumbleLinkData.MapIdOffset, 1206u);
        _gameView.Write(MumbleLinkData.ProcessIdOffset, 1234u);
        _gameView.Write(MumbleLinkData.MountIndexOffset, (byte)3);

        MumbleLinkData data = _reader.Read();

        Assert.Equal(2u, data.UiVersion);
        Assert.Equal(42u, data.UiTick);
        Assert.Equal(1206u, data.MapId);
        Assert.Equal(UiState.InCombat | UiState.GameHasFocus, data.UiState);
        Assert.Equal(1234u, data.ProcessId);
        Assert.Equal(3, data.MountIndex);
    }

    [Fact]
    public void TextboxFocus_DetectedWhileTickAdvances()
    {
        Write(tick: 1, UiState.TextboxHasFocus);

        Assert.True(_reader.IsTextboxFocused(TimeSpan.FromSeconds(10)));

        Write(tick: 2, UiState.None);

        Assert.False(_reader.IsTextboxFocused(TimeSpan.FromSeconds(10.1)));
    }

    [Fact]
    public void TextboxFocus_IgnoredWhenDataIsStale()
    {
        Write(tick: 1, UiState.TextboxHasFocus);
        Assert.True(_reader.IsTextboxFocused(TimeSpan.FromSeconds(10)));

        // The game stopped updating (closed or loading): don't block forever.
        Assert.False(_reader.IsTextboxFocused(TimeSpan.FromSeconds(12)));
    }

    [Fact]
    public void GameFocus_ComesFromGameHasFocusFlag()
    {
        Write(tick: 5, UiState.GameHasFocus);
        Assert.True(_reader.IsGameFocused());

        Write(tick: 6, UiState.None);
        Assert.False(_reader.IsGameFocused());
    }

    [Fact]
    public void TextboxFocus_FalseWhenGameNeverWrote()
    {
        Assert.False(_reader.IsTextboxFocused(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void UiState_IsNullUntilTheGameWrites_AndUiSizeComesFromIdentity()
    {
        Assert.Null(_reader.GetUiState());
        Assert.Null(_reader.GetUiSize());

        Write(tick: 1, UiState.MapOpen);
        WriteIdentity("""{"name":"Kira","uisz":2}""");

        Assert.Equal(UiState.MapOpen, _reader.GetUiState());
        Assert.Equal(TyriaPad.Core.Overlay.UiSize.Large, _reader.GetUiSize());

        // With identity half-written the last known size is kept.
        WriteIdentity("{\"name\":\"Ki");
        Assert.Equal(TyriaPad.Core.Overlay.UiSize.Large, _reader.GetUiSize());
    }

    private void WriteIdentity(string json)
    {
        var buffer = new byte[MumbleLinkData.IdentityLength];
        System.Text.Encoding.Unicode.GetBytes(json).CopyTo(buffer, 0);
        _gameView.WriteArray(MumbleLinkData.IdentityOffset, buffer, 0, buffer.Length);
    }

    public void Dispose()
    {
        _reader.Dispose();
        _gameView.Dispose();
        _game.Dispose();
    }

    private void Write(uint tick, UiState uiState)
    {
        _gameView.Write(0, 2u);
        _gameView.Write(4, tick);
        _gameView.Write(MumbleLinkData.UiStateOffset, (uint)uiState);
    }
}