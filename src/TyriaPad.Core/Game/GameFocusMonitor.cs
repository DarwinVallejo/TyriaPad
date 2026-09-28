using TyriaPad.Core.Diagnostics;

namespace TyriaPad.Core.Game;

/// <summary>
/// Decides whether GW2 has focus from two sources: the foreground window and, if that fails,
/// what GW2 itself publishes in MumbleLink. In the Xbox full screen experience Windows can report
/// the shell window (ApplicationFrameHost) as the foreground one even though the game has focus.
/// Used from the poller thread.
/// </summary>
public sealed class GameFocusMonitor(FocusWatcher window, MumbleLinkReader mumble)
{
    private string? _lastSource;

    public bool IsGameFocused()
    {
        bool byWindow = window.IsGameFocused;
        bool byMumble = !byWindow && mumble.IsGameFocused();

        string source = byWindow ? "foreground window"
            : byMumble ? $"MumbleLink (Windows reports {window.ForegroundName} in the foreground)"
            : "none";
        if (source != _lastSource)
        {
            _lastSource = source;
            Log.Write($"GW2 focus from: {source}");
        }

        return byWindow || byMumble;
    }
}