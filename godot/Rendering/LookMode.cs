#nullable enable
namespace BlueHarvest.Game.Rendering;

/// <summary>
/// How richly the map is drawn (the design guide's presentation levels). Saved in user://ui.cfg; F3
/// cycles. Append only: Expressive (motion and condition-linked accents) comes with the effects work.
/// </summary>
public enum LookMode : byte
{
    /// <summary>One glyph family per cover with hashed variants and flat colours: the look the game had before the visual system.</summary>
    Classic,

    /// <summary>Correlated patches, accents, biome tints, edge blending, micro-marks and canopy shadows. The default.</summary>
    Layered,
}
