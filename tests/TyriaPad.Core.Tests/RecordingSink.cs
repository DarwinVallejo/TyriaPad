using TyriaPad.Core.Output;

namespace TyriaPad.Core.Tests;

internal sealed class RecordingSink : IInputSink
{
    public List<string> Events { get; } = [];

    public int TotalDx { get; private set; }

    public int TotalDy { get; private set; }

    public void SendKey(Key key, bool down) => Events.Add($"{key} {(down ? "down" : "up")}");

    public void SendMouseButton(MouseButton button, bool down) => Events.Add($"Mouse{button} {(down ? "down" : "up")}");

    public void SendMouseMove(int dx, int dy)
    {
        TotalDx += dx;
        TotalDy += dy;
    }

    public void SendMouseWheel(int delta) => Events.Add($"Wheel {delta}");
}