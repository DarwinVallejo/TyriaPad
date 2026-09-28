namespace TyriaPad.Core.Input;

/// <summary>
/// Unprocessed controller reading, with the same ranges as XInput. <paramref name="Extra"/> carries the
/// buttons that don't come from XInput (M1/M2).
/// </summary>
public readonly record struct RawGamepad(
    ushort Buttons,
    byte LeftTrigger,
    byte RightTrigger,
    short LeftX,
    short LeftY,
    short RightX,
    short RightY,
    GamepadButtons Extra = GamepadButtons.None);

public sealed record InputSettings
{
    public static InputSettings Default { get; } = new();

    /// <summary>Radial deadzone, 0–1. The defaults are the ones XInput recommends.</summary>
    public float LeftStickDeadzone { get; init; } = 0.24f;

    public float RightStickDeadzone { get; init; } = 0.265f;

    /// <summary>The trigger counts as pressed once past this value…</summary>
    public float TriggerPressThreshold { get; init; } = 0.30f;

    /// <summary>…and as released once below this other one (hysteresis to avoid bouncing).</summary>
    public float TriggerReleaseThreshold { get; init; } = 0.20f;

    /// <summary>
    /// If XInput reads idle, read the controller through Raw Input. That's what allows playing in the
    /// Xbox full screen experience, where Windows doesn't give XInput to background apps.
    /// </summary>
    public bool RawInputFallback { get; init; } = true;
}

/// <summary>Normalized stick: magnitude 0 inside the deadzone and 1 at full tilt.</summary>
public readonly record struct Stick(float X, float Y)
{
    public float Magnitude => MathF.Sqrt((X * X) + (Y * Y));

    public static Stick FromRaw(short x, short y, float deadzone)
    {
        float fx = Math.Max(x / 32767f, -1f);
        float fy = Math.Max(y / 32767f, -1f);
        float magnitude = MathF.Sqrt((fx * fx) + (fy * fy));
        if (magnitude <= deadzone)
        {
            return default;
        }

        // Rescales so the value starts at 0 right when leaving the deadzone.
        float scaled = (MathF.Min(magnitude, 1f) - deadzone) / (1f - deadzone);
        float factor = scaled / magnitude;
        return new Stick(fx * factor, fy * factor);
    }
}

public readonly record struct GamepadState(
    GamepadButtons Buttons,
    Stick Left,
    Stick Right,
    float LeftTrigger,
    float RightTrigger)
{
    private const ushort XInputButtonMask = 0xF3FF;
    private const GamepadButtons ExtraMask = GamepadButtons.M1 | GamepadButtons.M2;

    public bool IsDown(GamepadButtons button) => (Buttons & button) != 0;

    /// <param name="previous">Buttons of the previous state, for the trigger hysteresis.</param>
    public static GamepadState FromRaw(in RawGamepad raw, GamepadButtons previous, InputSettings settings)
    {
        var buttons = (GamepadButtons)(raw.Buttons & XInputButtonMask) | (raw.Extra & ExtraMask);
        float leftTrigger = raw.LeftTrigger / 255f;
        float rightTrigger = raw.RightTrigger / 255f;

        if (IsTriggerDown(leftTrigger, (previous & GamepadButtons.LeftTrigger) != 0, settings))
        {
            buttons |= GamepadButtons.LeftTrigger;
        }

        if (IsTriggerDown(rightTrigger, (previous & GamepadButtons.RightTrigger) != 0, settings))
        {
            buttons |= GamepadButtons.RightTrigger;
        }

        return new GamepadState(
            buttons,
            Stick.FromRaw(raw.LeftX, raw.LeftY, settings.LeftStickDeadzone),
            Stick.FromRaw(raw.RightX, raw.RightY, settings.RightStickDeadzone),
            leftTrigger,
            rightTrigger);
    }

    private static bool IsTriggerDown(float value, bool wasDown, InputSettings settings)
    {
        return wasDown ? value > settings.TriggerReleaseThreshold : value >= settings.TriggerPressThreshold;
    }
}

/// <summary>One poller cycle: current state plus the edges relative to the previous cycle.</summary>
public readonly record struct GamepadFrame(
    GamepadState State,
    GamepadButtons Pressed,
    GamepadButtons Released,
    bool Connected,
    TimeSpan Time,
    TimeSpan Delta)
{
    public static GamepadFrame Create(GamepadButtons previous, GamepadState state, bool connected, TimeSpan time, TimeSpan delta)
    {
        return new GamepadFrame(state, state.Buttons & ~previous, previous & ~state.Buttons, connected, time, delta);
    }
}