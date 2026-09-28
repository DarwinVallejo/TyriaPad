using System.Text.Json.Serialization;

using TyriaPad.Core.Input;
using TyriaPad.Core.Mapping;
using TyriaPad.Core.Output;

namespace TyriaPad.Core.Config;

/// <summary>Gesture thresholds, in milliseconds.</summary>
public sealed record GestureSettings
{
    /// <summary>From here on a press counts as "hold".</summary>
    public int HoldMs { get; init; } = 250;

    /// <summary>Maximum time between releasing and pressing again for it to count as a double tap.</summary>
    public int DoubleTapMs { get; init; } = 250;

    /// <summary>
    /// Keeping the Action Camera button held until here flips the internal mode again without
    /// sending a key, to resync TyriaPad with the game.
    /// </summary>
    public int ResyncMs { get; init; } = 1500;

    /// <summary>Mouse wheel rate while a wheel:up/down button is held.</summary>
    public int WheelRepeatMs { get; init; } = 200;

    [JsonIgnore]
    public TimeSpan Hold => TimeSpan.FromMilliseconds(HoldMs);

    [JsonIgnore]
    public TimeSpan DoubleTap => TimeSpan.FromMilliseconds(DoubleTapMs);

    [JsonIgnore]
    public TimeSpan Resync => TimeSpan.FromMilliseconds(ResyncMs);

    [JsonIgnore]
    public TimeSpan WheelRepeat => TimeSpan.FromMilliseconds(WheelRepeatMs);
}

/// <summary>Thresholds (with hysteresis) for the stick to press the movement keys.</summary>
public sealed record MovementSettings
{
    public float PressThreshold { get; init; } = 0.5f;

    public float ReleaseThreshold { get; init; } = 0.4f;
}

/// <summary>Contents of <c>config.json</c>. Everything has a default value: an empty file ({}) is valid.</summary>
public sealed record TyriaPadSettings
{
    public static TyriaPadSettings Default { get; } = new();

    /// <summary>Profile name: <c>profiles/&lt;name&gt;.json</c> is loaded.</summary>
    public string Profile { get; init; } = "blaggletoad";

    /// <summary>Key bound in GW2 to "Toggle Action Camera".</summary>
    public string ActionCameraKey { get; init; } = ",";

    /// <summary>
    /// GW2 keybinds XML (Documents\Guild Wars 2\InputBinds): empty = the most recently exported one,
    /// a file name or a path, or "none" to assume the game's default keys.
    /// </summary>
    public string InputBinds { get; init; } = string.Empty;

    /// <summary>
    /// Switch mode according to the Windows cursor: visible = cursor, hidden = camera.
    /// Covers the menus and dialogs GW2 opens with Action Camera on.
    /// </summary>
    public bool FollowCursor { get; init; } = true;

    /// <summary>The cursor has to stay in the same state this long to switch mode (avoids flicker).</summary>
    public int CursorDebounceMs { get; init; } = 80;

    /// <summary>After a manual switch with the Action Camera button, the cursor is ignored for this long while the game reacts.</summary>
    public int ManualGraceMs { get; init; } = 600;

    /// <summary>Use the cursor context while MumbleLink reports the map open, even if the cursor is not visible.</summary>
    public bool PointerWhenMapOpen { get; init; } = true;

    /// <summary>
    /// Close TyriaPad when GW2 closes. Does not apply if it was opened by Start with Windows, which
    /// leaves it idle waiting for the next session.
    /// </summary>
    public bool ExitWithGame { get; init; }

    public GestureSettings Gestures { get; init; } = new();

    /// <summary>Stick as camera (Action Camera).</summary>
    public StickMouseSettings Camera { get; init; } = new() { Speed = 1400f, Exponent = 2f };

    /// <summary>Stick as cursor (menus).</summary>
    public StickMouseSettings Pointer { get; init; } = new() { Speed = 900f, Exponent = 2f };

    public MovementSettings Movement { get; init; } = new();

    public InputSettings Input { get; init; } = InputSettings.Default;

    /// <summary>Glyphs over the skill bar, radials and mode indicator.</summary>
    public OverlaySettings Overlay { get; init; } = new();

    /// <summary>Ally X M1/M2 over HID.</summary>
    public AllyButtonsSettings AllyButtons { get; init; } = new();

    [JsonIgnore]
    public TimeSpan CursorDebounce => TimeSpan.FromMilliseconds(CursorDebounceMs);

    [JsonIgnore]
    public TimeSpan ManualGrace => TimeSpan.FromMilliseconds(ManualGraceMs);

    /// <summary>Scancode of <see cref="ActionCameraKey"/>. Throws if <see cref="Validate"/> would return errors.</summary>
    [JsonIgnore]
    public Key ActionCameraKeyCode => KeyNames.TryParseKey(ActionCameraKey, out Key key)
        ? key
        : throw new ConfigException($"actionCameraKey: unknown key '{ActionCameraKey}'");

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Profile))
        {
            errors.Add("profile: the profile name is missing");
        }

        if (!KeyNames.TryParseKey(ActionCameraKey, out _))
        {
            errors.Add($"actionCameraKey: unknown key '{ActionCameraKey}'");
        }

        if (Gestures.HoldMs <= 0 || Gestures.DoubleTapMs <= 0 || Gestures.WheelRepeatMs <= 0)
        {
            errors.Add("gestures: holdMs, doubleTapMs and wheelRepeatMs must be greater than 0");
        }

        if (Gestures.ResyncMs <= Gestures.HoldMs)
        {
            errors.Add("gestures.resyncMs: must be greater than holdMs");
        }

        if (Camera.Speed <= 0 || Pointer.Speed <= 0 || Camera.Exponent <= 0 || Pointer.Exponent <= 0)
        {
            errors.Add("camera/pointer: speed and exponent must be greater than 0");
        }

        if (Movement.ReleaseThreshold >= Movement.PressThreshold || Movement.ReleaseThreshold < 0 || Movement.PressThreshold > 1)
        {
            errors.Add("movement: requires 0 <= releaseThreshold < pressThreshold <= 1");
        }

        if (Input.LeftStickDeadzone is < 0 or >= 1 || Input.RightStickDeadzone is < 0 or >= 1)
        {
            errors.Add("input: deadzones go from 0 to 1 (exclusive)");
        }

        if (Input.TriggerReleaseThreshold >= Input.TriggerPressThreshold || Input.TriggerReleaseThreshold < 0 || Input.TriggerPressThreshold > 1)
        {
            errors.Add("input: requires 0 <= triggerReleaseThreshold < triggerPressThreshold <= 1");
        }

        errors.AddRange(Overlay.Validate());
        errors.AddRange(AllyButtons.Validate());
        return errors;
    }
}
