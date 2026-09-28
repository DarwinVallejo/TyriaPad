using TyriaPad.Core.Input;

namespace TyriaPad.Core.Mapping;

/// <summary>Sensitivity of a stick that moves the mouse.</summary>
public sealed record StickMouseSettings
{
    /// <summary>Pixels per second with the stick at full tilt.</summary>
    public float Speed { get; init; } = 1000f;

    /// <summary>Response curve: 1 = linear; higher = more precision near the center.</summary>
    public float Exponent { get; init; } = 2f;
}

/// <summary>Turns a stick into relative mouse movement, accumulating the fractional pixels.</summary>
public sealed class StickToMouse
{
    private float _remainderX;
    private float _remainderY;

    public (int Dx, int Dy) Update(Stick stick, StickMouseSettings settings, TimeSpan delta)
    {
        float magnitude = stick.Magnitude;
        if (magnitude <= 0f)
        {
            Reset();
            return (0, 0);
        }

        float distance = MathF.Pow(MathF.Min(magnitude, 1f), settings.Exponent) * settings.Speed * (float)delta.TotalSeconds;
        float x = (stick.X / magnitude * distance) + _remainderX;
        float y = (-stick.Y / magnitude * distance) + _remainderY; // stick up = mouse up (screen Y grows downward)

        int dx = (int)MathF.Truncate(x);
        int dy = (int)MathF.Truncate(y);
        _remainderX = x - dx;
        _remainderY = y - dy;
        return (dx, dy);
    }

    public void Reset()
    {
        _remainderX = 0f;
        _remainderY = 0f;
    }
}
