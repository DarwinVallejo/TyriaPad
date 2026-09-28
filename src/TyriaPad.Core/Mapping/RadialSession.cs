using TyriaPad.Core.Input;

namespace TyriaPad.Core.Mapping;

/// <summary>What the overlay sees while a radial is open.</summary>
public sealed record RadialState(RadialMenu Menu, int? Selected)
{
    public RadialItem? SelectedItem => Selected is { } index ? Menu.Items[index] : null;
}

/// <summary>
/// An open radial: it follows the stick and remembers the last option pointed at, so that on releasing the
/// button it is chosen even if the stick has already returned to the center. The options are laid out in a circle
/// starting at the top (index 0) and going clockwise.
/// </summary>
public sealed class RadialSession(RadialMenu menu)
{
    /// <summary>Minimum stick magnitude to point at an option.</summary>
    public const float SelectThreshold = 0.5f;

    public RadialMenu Menu { get; } = menu;

    public int? Selected { get; private set; }

    public RadialState State => new(Menu, Selected);

    public void Update(Stick stick)
    {
        if (stick.Magnitude < SelectThreshold)
        {
            return;
        }

        Selected = SectorOf(stick, Menu.Items.Count);
    }

    /// <summary>Index of the option the stick points at (0 = top, clockwise).</summary>
    public static int SectorOf(Stick stick, int count)
    {
        // atan2(x, y): 0 at the top, positive to the right (clockwise on screen).
        double angle = Math.Atan2(stick.X, stick.Y);
        if (angle < 0)
        {
            angle += Math.Tau;
        }

        double sector = Math.Tau / count;
        return (int)Math.Round(angle / sector) % count;
    }
}
