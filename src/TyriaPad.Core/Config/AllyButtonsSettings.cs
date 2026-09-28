using System.Text.Json.Serialization;

namespace TyriaPad.Core.Config;

/// <summary>Ally X back buttons M1/M2, read read-only over HID. The codes are learned from the tray.</summary>
public sealed record AllyButtonsSettings
{
    public bool Enabled { get; init; } = true;

    /// <summary>A button that sends no code on release is considered released after this long without reports.</summary>
    public int ReleaseAfterMs { get; init; } = 150;

    [JsonIgnore]
    public TimeSpan ReleaseAfter => TimeSpan.FromMilliseconds(ReleaseAfterMs);

    public IReadOnlyList<string> Validate()
        => ReleaseAfterMs <= 0 ? ["allyButtons.releaseAfterMs: must be greater than 0"] : [];
}
