#nullable enable
namespace BlueHarvest.Game.Rendering;

/// <summary>
/// How richly the map is drawn (the design guide's presentation levels). Saved in user://ui.cfg; F3
/// cycles. Each level has everything the one before it has. Append only.
/// </summary>
public enum LookMode : byte
{
    /// <summary>One glyph family per cover with hashed variants and flat colours: the look the game had before the visual system.</summary>
    Classic,

    /// <summary>Correlated patches, accents, biome tints, edge blending, micro-marks and canopy shadows. The default.</summary>
    Layered,

    /// <summary>Layered with richer texture (more accents and specks) and stronger motion: gusts with blown leaves, pulsing emergency beacons.</summary>
    Expressive,
}
