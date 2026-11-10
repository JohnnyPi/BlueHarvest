#nullable enable
using BlueHarvest.Sim.World;
using Godot;

namespace BlueHarvest.Game.Rendering;

/// <summary>
/// How each kind of ground looks: its colours and glyph, its varied glyphs, how far it stands up in the
/// raised view and what colour its face is, and whether the park plan shows it. Looks only; nothing
/// here touches the simulation.
/// </summary>
public static class MapLook
{
    /// <summary>Background, glyph and glyph colour per cover type, indexed by CoverType.</summary>
    public static readonly (Color Background, char Glyph, Color Ink)[] Cover =
    {
        (new Color(0.16f, 0.30f, 0.14f), '.', new Color(1f, 1f, 1f, 0.18f)),          // Grass
        (new Color(0.24f, 0.32f, 0.15f), ',', new Color(0.75f, 0.85f, 0.45f, 0.55f)), // Scrub
        (new Color(0.10f, 0.25f, 0.10f), '"', new Color(0.45f, 0.80f, 0.35f, 0.75f)), // Sparse jungle
        (new Color(0.05f, 0.16f, 0.06f), '&', new Color(0.30f, 0.65f, 0.25f, 0.85f)), // Dense jungle
        (new Color(0.36f, 0.30f, 0.20f), '=', new Color(0.75f, 0.65f, 0.45f, 0.45f)), // Track
        (new Color(0.28f, 0.28f, 0.28f), '.', new Color(1f, 1f, 1f, 0.25f)),          // Floor
        (new Color(0.55f, 0.55f, 0.53f), '#', new Color(0.15f, 0.15f, 0.15f)),        // Wall
        (new Color(0.40f, 0.26f, 0.12f), '+', new Color(0.95f, 0.80f, 0.55f)),        // Door
        (new Color(0.16f, 0.22f, 0.12f), '#', new Color(0.95f, 0.85f, 0.20f)),        // Fence
        (new Color(0.38f, 0.36f, 0.33f), '^', new Color(0.80f, 0.78f, 0.72f)),        // Rock
        (new Color(0.20f, 0.28f, 0.40f), '#', new Color(0.65f, 0.80f, 1f)),           // Vehicle
        (new Color(0.18f, 0.22f, 0.12f), '%', new Color(0.70f, 0.60f, 0.20f)),        // Broken fence
        (new Color(0.30f, 0.22f, 0.14f), '|', new Color(0.85f, 0.65f, 0.40f)),        // Support post
        (new Color(0.26f, 0.24f, 0.22f), ':', new Color(0.65f, 0.60f, 0.55f, 0.7f)),  // Rubble
        (new Color(0.16f, 0.22f, 0.12f), '#', new Color(0.55f, 0.75f, 1f)),           // Sagging fence
        (new Color(0.12f, 0.31f, 0.41f), '~', new Color(0.50f, 0.75f, 0.85f, 0.6f)),  // Shallow water
        (new Color(0.06f, 0.16f, 0.25f), '≈', new Color(0.31f, 0.53f, 0.72f, 0.6f)),  // Deep water
        (new Color(0.61f, 0.54f, 0.36f), '·', new Color(0.91f, 0.84f, 0.65f, 0.5f)),  // Sand
        (new Color(0.29f, 0.24f, 0.16f), '∴', new Color(0.66f, 0.56f, 0.40f, 0.6f)),  // Mud
        (new Color(0.33f, 0.38f, 0.17f), '"', new Color(0.79f, 0.81f, 0.45f, 0.7f)),  // Tall grass
        (new Color(0.18f, 0.29f, 0.24f), '¦', new Color(0.62f, 0.77f, 0.63f, 0.8f)),  // Reeds
        (new Color(0.13f, 0.25f, 0.16f), 'ψ', new Color(0.50f, 0.81f, 0.54f, 0.75f)), // Ferns
        (new Color(0.33f, 0.32f, 0.30f), '^', new Color(0.72f, 0.70f, 0.65f, 0.5f)),  // Bare rock
        (new Color(0.16f, 0.15f, 0.14f), '%', new Color(0.54f, 0.50f, 0.47f, 0.6f)),  // Old lava
        (new Color(0.24f, 0.24f, 0.25f), '▐', new Color(0.62f, 0.71f, 0.78f)),        // Equipment
    };

    /// <summary>The roof over a building, seen from outside.</summary>
    public static readonly Color Roof = new(0.30f, 0.24f, 0.22f);
    public static readonly Color RoofFace = new(0.22f, 0.20f, 0.19f);
    public static readonly Color RoofSeam = new(0.17f, 0.13f, 0.12f);

    /// <summary>The face of a drop in the ground: earth and rock.</summary>
    public static readonly Color EarthFace = new(0.30f, 0.26f, 0.21f);

    /// <summary>Tiles you haven't seen but know from the park plan: a faint blueprint.</summary>
    public static readonly Color PlanGround = new(0.024f, 0.04f, 0.047f);
    public static readonly Color PlanInk = new(0.35f, 0.65f, 0.80f, 0.3f);

    /// <summary>
    /// How far a thing stands above the ground in the raised view, in decimetres. The simulation's
    /// sight heights are taller (a fence is 4 m); these are lower so they hide less of what's behind.
    /// </summary>
    public static int StandsUpDm(CoverType cover) => cover switch
    {
        CoverType.Wall => 30,
        CoverType.Fence or CoverType.WeakFence => 20,
        CoverType.Rock => 20,
        CoverType.Vehicle => 12,
        _ => 0,
    };

    /// <summary>The colour of a thing's south face in the raised view.</summary>
    public static Color Face(CoverType cover) => cover switch
    {
        CoverType.Wall => new Color(0.40f, 0.40f, 0.38f),
        CoverType.Fence or CoverType.WeakFence => new Color(0.26f, 0.27f, 0.17f),
        CoverType.Rock => new Color(0.30f, 0.28f, 0.25f),
        CoverType.Vehicle => new Color(0.13f, 0.18f, 0.26f),
        _ => EarthFace,
    };

    /// <summary>Whether the park plan (what a member of staff would know) shows this cover.</summary>
    public static bool OnPlan(CoverType cover) => cover is CoverType.Wall or CoverType.Door or CoverType.Fence
        or CoverType.WeakFence or CoverType.Track or CoverType.Floor or CoverType.Vehicle or CoverType.Support;

    /// <summary>A stable per-tile hash for picking glyph variants and colour jitter (looks only).</summary>
    public static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 374761393) + (uint)(y * 668265263) + (uint)(salt * 1274126177);
        h = (h ^ (h >> 13)) * 1274126177u;
        return h ^ (h >> 16);
    }

    /// <summary>0.93 to 1.07: a little brightness variation so a field doesn't look like wallpaper.</summary>
    public static float Jitter(int x, int y) => 0.93f + 0.14f * (Hash(x, y, 1) % 1000) / 1000f;

    private static readonly char[] GrassGlyphs = { '.', '.', '·', '\'', '`', ' ', '.' };
    private static readonly char[] ScrubGlyphs = { ',', ';', ',', 'ⁿ' };
    private static readonly char[] SparseGlyphs = { '"', 'ʻ', 'τ', '"', 'ʻ' };
    private static readonly char[] DenseGlyphs = { '♣', '&', '♠', '♣', 'Ψ' };
    private static readonly char[] TrackGlyphs = { '=', '·', '≡', '·', '=' };
    private static readonly char[] FloorGlyphs = { '·', '·', ' ' };
    private static readonly char[] RockGlyphs = { '▲', '^', '∩', '▴' };
    private static readonly char[] BrokenGlyphs = { '⁄', '\\', '~', '.' };
    private static readonly char[] RubbleGlyphs = { '∴', ':', '∵' };

    /// <summary>Box-drawing wall pieces by which neighbours are walls or doors (N=1, E=2, S=4, W=8).</summary>
    private static readonly char[] WallPieces =
    {
        '■', '║', '═', '╚', '║', '║', '╔', '╠', '═', '╝', '═', '╩', '╗', '╣', '╦', '╬',
    };

    /// <summary>
    /// The varied glyph for a tile: hashed variants for ground, joined box-drawing for walls, posts
    /// every 4 m on a fence. <paramref name="coverAt"/> gives the cover of any tile (null off the map).
    /// </summary>
    public static char VariedGlyph(CoverType cover, int x, int y, System.Func<int, int, CoverType?> coverAt)
    {
        switch (cover)
        {
            case CoverType.Wall:
            {
                bool Wallish(int dx, int dy) => coverAt(x + dx, y + dy) is CoverType.Wall or CoverType.Door;
                int mask = (Wallish(0, -1) ? 1 : 0) | (Wallish(1, 0) ? 2 : 0) | (Wallish(0, 1) ? 4 : 0) | (Wallish(-1, 0) ? 8 : 0);
                return WallPieces[mask];
            }
            case CoverType.Fence:
            {
                bool Fenceish(int dx, int dy) => coverAt(x + dx, y + dy) is CoverType.Fence or CoverType.WeakFence
                    or CoverType.BrokenFence or CoverType.Door or CoverType.Rock;
                bool across = Fenceish(-1, 0) || Fenceish(1, 0);
                bool along = Fenceish(0, -1) || Fenceish(0, 1);
                if (along && !across)
                    return y % 4 == 0 ? '┼' : '│';
                return across && along ? '┼' : x % 4 == 0 ? '┼' : '─';
            }
            case CoverType.WeakFence:
                return '╌';
            case CoverType.Support:
                return '╫';
            case CoverType.Vehicle:
                return '▒';
        }

        var glyphs = cover switch
        {
            CoverType.Grass => GrassGlyphs,
            CoverType.Scrub => ScrubGlyphs,
            CoverType.SparseJungle => SparseGlyphs,
            CoverType.DenseJungle => DenseGlyphs,
            CoverType.Track => TrackGlyphs,
            CoverType.Floor => FloorGlyphs,
            CoverType.Rock => RockGlyphs,
            CoverType.BrokenFence => BrokenGlyphs,
            CoverType.Rubble => RubbleGlyphs,
            _ => null,
        };
        return glyphs is null ? Cover[(int)cover].Glyph : glyphs[Hash(x, y, 3) % (uint)glyphs.Length];
    }
}
