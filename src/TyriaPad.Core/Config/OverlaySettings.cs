using System.Text.Json.Serialization;

namespace TyriaPad.Core.Config;

/// <summary>Screen corner for the mode indicator.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<IndicatorCorner>))]
public enum IndicatorCorner
{
    None,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary><c>overlay</c> section of <c>config.json</c>.</summary>
public sealed record OverlaySettings
{
    public bool Enabled { get; init; } = true;

    /// <summary>Size of the glyphs relative to the slot (1 = the one matching the interface size).</summary>
    public float Scale { get; init; } = 1f;

    /// <summary>Overall overlay opacity, 0–1.</summary>
    public float Opacity { get; init; } = 0.9f;

    /// <summary>How many F1…Fn slots to draw on the profession bar (0–8).</summary>
    public int ProfessionSlots { get; init; } = 5;

    /// <summary>Hide the glyphs while MumbleLink reports the map open.</summary>
    public bool HideWhenMapOpen { get; init; } = true;

    /// <summary>Where the mode indicator goes (camera / cursor / mount); none = don't show it.</summary>
    public IndicatorCorner Indicator { get; init; } = IndicatorCorner.TopRight;

    /// <summary>Radius of the radial menus, in pixels.</summary>
    public float RadialRadius { get; init; } = 130f;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Scale is < 0.25f or > 4f)
        {
            errors.Add("overlay.scale: must be between 0.25 and 4");
        }

        if (Opacity is < 0f or > 1f)
        {
            errors.Add("overlay.opacity: must be between 0 and 1");
        }

        if (ProfessionSlots is < 0 or > 8)
        {
            errors.Add("overlay.professionSlots: must be between 0 and 8");
        }

        if (RadialRadius is < 40f or > 600f)
        {
            errors.Add("overlay.radialRadius: must be between 40 and 600");
        }

        return errors;
    }
}
