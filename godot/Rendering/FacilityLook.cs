#nullable enable
using BlueHarvest.Sim.World;
using Godot;

namespace BlueHarvest.Game.Rendering;

/// <summary>
/// Which built world a tile belongs to, for the Layered look (design guide §07–08, decisions of 21 Nov
/// 2026). Append only.
/// </summary>
public enum Sector : byte
{
    /// <summary>Not built: nature. Structures that end up here (a lone hut, a road barrier) look <see cref="Industrial"/>.</summary>
    None,
    /// <summary>The Menagerie: genetics, the raptor pen, the paddocks and their viewing stations. Secure double walls, amber and security blue.</summary>
    Menagerie,
    /// <summary>The Vitality Institute: the orgone station. Rounded clinical walls, pale limestone and rose.</summary>
    Vitality,
    /// <summary>The Ninth Weather: the weather station. Heavy exposed framing, slate and electric violet.</summary>
    NinthWeather,
    /// <summary>The park's ordinary works (control, comms, docks, depot, airstrip, nuclear, geothermal, labs, watch posts, ley sites, transit): plain concrete, light walls inside a heavy perimeter.</summary>
    Industrial,
    /// <summary>Ancient ruins (terraces, standing stones, platforms): worn masonry, no engineered lines.</summary>
    RuinAncient,
    /// <summary>Newer ruins (chapel, sugar mill, lighthouse, bunker, radar, camps): weathered industrial.</summary>
    RuinWeathered,
}

/// <summary>How a sector's walls are drawn. Every style keeps a wall a wall: only the line changes.</summary>
public enum WallStyle : byte
{
    Double,
    Light,
    Heavy,
    Rounded,
    /// <summary>Blockwork glyphs instead of lines (ancient ruins).</summary>
    Masonry,
}

/// <summary>One sector's palette and line styles.</summary>
public sealed record SectorLook(
    Color WallGround, Color WallInk, Color WallFace,
    Color FloorGround, Color FloorInk,
    Color YardGround, Color YardInk,
    Color EquipmentGround, Color EquipmentInk,
    Color Roof, Color RoofFace, Color RoofSeam,
    WallStyle Inner, WallStyle Perimeter, char FencePost);

/// <summary>
/// The Layered look for built things. Facility identity comes from the walls' line style, the palette,
/// the roofs and the fence posts; what things are and what state they're in never changes. A door is
/// '+' or '/' (red when locked) everywhere, a weak fence is dashed, a broken one is broken, and a live
/// fence's wire is blue. Looks only.
/// </summary>
public static class FacilityLook
{
    private static Color C(float r, float g, float b, float a = 1f) => new(r, g, b, a);

    private static readonly SectorLook Menagerie = new(
        WallGround: C(0.50f, 0.47f, 0.40f), WallInk: C(0.22f, 0.17f, 0.08f), WallFace: C(0.36f, 0.33f, 0.27f),
        FloorGround: C(0.24f, 0.27f, 0.30f), FloorInk: C(0.75f, 0.85f, 1f, 0.22f),
        YardGround: C(0.30f, 0.30f, 0.28f), YardInk: C(1f, 1f, 1f, 0.18f),
        EquipmentGround: C(0.24f, 0.26f, 0.30f), EquipmentInk: C(0.62f, 0.75f, 0.88f),
        Roof: C(0.34f, 0.28f, 0.19f), RoofFace: C(0.25f, 0.21f, 0.15f), RoofSeam: C(0.20f, 0.16f, 0.10f),
        Inner: WallStyle.Double, Perimeter: WallStyle.Double, FencePost: '╋');

    private static readonly SectorLook Vitality = new(
        WallGround: C(0.70f, 0.67f, 0.60f), WallInk: C(0.52f, 0.28f, 0.30f), WallFace: C(0.55f, 0.52f, 0.46f),
        FloorGround: C(0.38f, 0.36f, 0.35f), FloorInk: C(1f, 0.85f, 0.85f, 0.22f),
        YardGround: C(0.40f, 0.38f, 0.35f), YardInk: C(1f, 1f, 1f, 0.18f),
        EquipmentGround: C(0.40f, 0.34f, 0.36f), EquipmentInk: C(0.95f, 0.70f, 0.72f),
        Roof: C(0.56f, 0.52f, 0.49f), RoofFace: C(0.44f, 0.41f, 0.39f), RoofSeam: C(0.40f, 0.36f, 0.35f),
        Inner: WallStyle.Rounded, Perimeter: WallStyle.Rounded, FencePost: '┼');

    private static readonly SectorLook NinthWeather = new(
        WallGround: C(0.40f, 0.43f, 0.48f), WallInk: C(0.60f, 0.52f, 0.86f), WallFace: C(0.29f, 0.31f, 0.35f),
        FloorGround: C(0.22f, 0.24f, 0.28f), FloorInk: C(0.80f, 0.80f, 1f, 0.2f),
        YardGround: C(0.26f, 0.27f, 0.29f), YardInk: C(1f, 1f, 1f, 0.16f),
        EquipmentGround: C(0.26f, 0.27f, 0.34f), EquipmentInk: C(0.70f, 0.65f, 0.95f),
        Roof: C(0.27f, 0.29f, 0.34f), RoofFace: C(0.20f, 0.21f, 0.25f), RoofSeam: C(0.16f, 0.17f, 0.21f),
        Inner: WallStyle.Heavy, Perimeter: WallStyle.Heavy, FencePost: '╋');

    private static readonly SectorLook Industrial = new(
        WallGround: C(0.55f, 0.55f, 0.53f), WallInk: C(0.15f, 0.15f, 0.15f), WallFace: C(0.40f, 0.40f, 0.38f),
        FloorGround: C(0.28f, 0.28f, 0.28f), FloorInk: C(1f, 1f, 1f, 0.25f),
        YardGround: C(0.31f, 0.31f, 0.30f), YardInk: C(1f, 1f, 1f, 0.16f),
        EquipmentGround: C(0.24f, 0.24f, 0.25f), EquipmentInk: C(0.62f, 0.71f, 0.78f),
        Roof: C(0.30f, 0.24f, 0.22f), RoofFace: C(0.22f, 0.20f, 0.19f), RoofSeam: C(0.17f, 0.13f, 0.12f),
        Inner: WallStyle.Light, Perimeter: WallStyle.Heavy, FencePost: '┼');

    private static readonly SectorLook RuinAncient = new(
        WallGround: C(0.40f, 0.38f, 0.31f), WallInk: C(0.74f, 0.68f, 0.52f, 0.8f), WallFace: C(0.30f, 0.28f, 0.23f),
        FloorGround: C(0.30f, 0.29f, 0.25f), FloorInk: C(0.85f, 0.80f, 0.65f, 0.3f),
        YardGround: C(0.30f, 0.29f, 0.25f), YardInk: C(0.85f, 0.80f, 0.65f, 0.25f),
        EquipmentGround: C(0.30f, 0.29f, 0.25f), EquipmentInk: C(0.80f, 0.73f, 0.56f),
        Roof: C(0.34f, 0.31f, 0.25f), RoofFace: C(0.26f, 0.24f, 0.19f), RoofSeam: C(0.21f, 0.19f, 0.15f),
        Inner: WallStyle.Masonry, Perimeter: WallStyle.Masonry, FencePost: '┼');

    private static readonly SectorLook RuinWeathered = new(
        WallGround: C(0.45f, 0.44f, 0.40f), WallInk: C(0.26f, 0.27f, 0.22f), WallFace: C(0.33f, 0.33f, 0.30f),
        FloorGround: C(0.29f, 0.29f, 0.27f), FloorInk: C(1f, 1f, 1f, 0.2f),
        YardGround: C(0.29f, 0.29f, 0.27f), YardInk: C(1f, 1f, 1f, 0.16f),
        EquipmentGround: C(0.27f, 0.27f, 0.26f), EquipmentInk: C(0.60f, 0.62f, 0.58f),
        Roof: C(0.31f, 0.27f, 0.23f), RoofFace: C(0.23f, 0.21f, 0.18f), RoofSeam: C(0.18f, 0.15f, 0.13f),
        Inner: WallStyle.Light, Perimeter: WallStyle.Light, FencePost: '┼');

    /// <summary>The sector a facility kind belongs to (the room index's and the sites' Kind).</summary>
    public static Sector OfKind(string kind) => kind switch
    {
        "genetics" or "raptors" or "paddock" => Sector.Menagerie,
        "orgone" => Sector.Vitality,
        "weather" => Sector.NinthWeather,
        _ => Sector.Industrial,
    };

    /// <summary>The sector a ruin kind belongs to (decision 6).</summary>
    public static Sector OfRuin(string kind) => kind is "terraces" or "stones" or "platform" ? Sector.RuinAncient : Sector.RuinWeathered;

    /// <summary>The look for a sector; built things outside any sector look industrial.</summary>
    public static SectorLook For(Sector sector) => sector switch
    {
        Sector.Menagerie => Menagerie,
        Sector.Vitality => Vitality,
        Sector.NinthWeather => NinthWeather,
        Sector.RuinAncient => RuinAncient,
        Sector.RuinWeathered => RuinWeathered,
        _ => Industrial,
    };

    /// <summary>
    /// The sector whose look a tile takes: its own, except that fences outside any compound are the
    /// paddocks' (the Menagerie's) and other built things outside one are industrial.
    /// </summary>
    public static Sector Effective(Sector sector, CoverType cover) => sector != Sector.None ? sector
        : cover is CoverType.Fence or CoverType.WeakFence or CoverType.BrokenFence ? Sector.Menagerie
        : Sector.Industrial;

    // Wall pieces by neighbour mask (N=1, E=2, S=4, W=8), as MapLook's double ones.
    private const string Light = "■│─└││┌├─┘─┴┐┤┬┼";
    private const string Heavy = "■┃━┗┃┃┏┣━┛━┻┓┫┳╋";
    private const string Rounded = "■│─╰││╭├─╯─┴╮┤┬┼";

    /// <summary>Masonry: mostly cut blocks, some worn down to a small stone.</summary>
    private const string Blocks = "▧▧▧■";

    /// <summary>Every glyph the facility looks can draw, for the start-up glyph check.</summary>
    public const string AllGlyphs = Light + Heavy + Rounded + Blocks + "╋┼";

    /// <summary>
    /// A wall or fence glyph (as MapLook drew it) in the sector's style. A wall keeps its joins (the
    /// neighbour mask) and only changes line; a fence post may get heavier. Other glyphs come back as they are.
    /// </summary>
    /// <param name="perimeter">The compound's outer wall (the generator's reinforced wall) rather than a building's.</param>
    /// <param name="x">The tile's island position, for picking masonry blocks.</param>
    public static char Restyle(char glyph, CoverType cover, SectorLook look, bool perimeter, int x, int y)
    {
        if (cover == CoverType.Fence)
            return glyph == '┼' ? look.FencePost : glyph;
        if (cover != CoverType.Wall)
            return glyph;
        int piece = MapLook.WallPieceIndex(glyph);
        if (piece < 0)
            return glyph;
        return (perimeter ? look.Perimeter : look.Inner) switch
        {
            WallStyle.Light => Light[piece],
            WallStyle.Heavy => Heavy[piece],
            WallStyle.Rounded => Rounded[piece],
            WallStyle.Masonry => Blocks[(int)(LookNoise.Hash(0, x, y, 977) % (uint)Blocks.Length)],
            _ => glyph,
        };
    }
}
