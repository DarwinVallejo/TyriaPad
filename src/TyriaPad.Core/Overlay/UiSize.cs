namespace TyriaPad.Core.Overlay;

/// <summary>GW2 interface size (Options → Graphics → Interface Size). It's the MumbleLink <c>uisz</c> field.</summary>
public enum UiSize
{
    Small = 0,
    Normal = 1,
    Large = 2,
    Larger = 3,
}

public static class UiSizeExtensions
{
    /// <summary>
    /// Interface scale factor relative to the Normal size. These are the factors the community
    /// overlays use (Blish HUD): Small 0.810, Normal 0.897, Large 1.0 and Larger 1.103.
    /// </summary>
    public static float Scale(this UiSize size) => size switch
    {
        UiSize.Small => 0.810f / 0.897f,
        UiSize.Large => 1.0f / 0.897f,
        UiSize.Larger => 1.103f / 0.897f,
        _ => 1f,
    };

    /// <summary>Lowercase name for the calibration keys.</summary>
    public static string Key(this UiSize size) => size switch
    {
        UiSize.Small => "small",
        UiSize.Large => "large",
        UiSize.Larger => "larger",
        _ => "normal",
    };
}
