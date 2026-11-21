#nullable enable
using System;
using BlueHarvest.Sim.World;
using BlueHarvest.Sim.World.Generation;
using Godot;

namespace BlueHarvest.Game.Rendering;

/// <summary>What the Layered mode draws on one tile of natural ground.</summary>
/// <param name="Shadow">Draw a faint copy of the glyph a pixel or two down and right (tall growth).</param>
/// <param name="Mark">Draw a micro-mark at (<paramref name="MarkX"/>, <paramref name="MarkY"/>), each 0 to 1 across the cell.</param>
public readonly record struct GroundPick(char Glyph, Color Ink, Color Ground, bool Shadow, bool Mark, float MarkX, float MarkY);

/// <summary>
/// The Layered mode's choice for a tile of natural ground, in the guide's five steps: the cover's
/// profile (or one for its generator ground), its context (biome, slope, the cover next to it), a
/// patch from smooth noise over island metres, a rare accent, and then the renderer applies fog,
/// light and height as for any tile. Everything depends only on the island's seed, the tile's island
/// position and what is on and around the tile, so it looks the same however the map streams.
/// </summary>
public static class GroundLook
{
    /// <summary>A neighbour this much higher or lower (decimetres; 1 m in 1 m) makes ground steep: stony accents, greyer ground.</summary>
    public const int SteepDm = 10;

    /// <summary>How often soft ground beside other soft ground takes one of its neighbour's glyphs.</summary>
    private const int EdgeBlendPerMille = 220;

    /// <summary>The patch field's dithered edge: below Low a tile is outside, above High inside, between it's a hashed mix.</summary>
    private const float PatchLow = 0.52f, PatchHigh = 0.68f;

    private const string SteepAccents = "∴▴";
    private static readonly Color SteepGround = new(0.33f, 0.32f, 0.30f);

    /// <summary>
    /// The look for a tile, or null if its cover has no profile (it then keeps its Classic look).
    /// (<paramref name="x"/>, <paramref name="y"/>) is the tile's island position.
    /// </summary>
    /// <param name="biome">The generator's biome there; null where there is none (the testbed).</param>
    /// <param name="ground">The generator's own cover there; null where there is none.</param>
    /// <param name="slopeDm">The biggest height difference to a neighbouring tile, in decimetres.</param>
    /// <param name="tideWet">The sea's edge, uncovered by the tide within the last half tide: wet sand, darker ground.</param>
    /// <param name="rainWet">How wet the ground is from recent rain, 0 to 1 (Simulation.RecentRainPercent / 100).</param>
    /// <param name="rich">The Expressive look: half as many accents again and twice the micro-marks.</param>
    public static GroundPick? Choose(CoverType cover, int x, int y, int seed, Biome? biome, Cover? ground, int slopeDm,
        CoverType? north, CoverType? east, CoverType? south, CoverType? west, bool tideWet = false, float rainWet = 0f, bool rich = false)
    {
        var profile = tideWet && cover == CoverType.Sand ? VisualProfiles.WetSand : VisualProfiles.For(cover, ground);
        if (profile is null)
            return null;
        int salt = profile.Salt * 16;

        // 3 · patch: inside or outside, with a dithered edge so patches don't have hard outlines
        float field = LookNoise.Field(seed, x, y, profile.PatchScaleM, profile.Salt);
        float into = Math.Clamp((field - PatchLow) / (PatchHigh - PatchLow), 0f, 1f);
        bool inPatch = into >= 1f || into > 0f && LookNoise.Unit(seed, x, y, salt + 1) < into;
        string family = inPatch ? profile.Patch : profile.Primary;
        char glyph = Pick(family, seed, x, y, salt + 2);

        // 2 · context and 4 · accent: steep soft ground turns stony; otherwise the profile's own accents,
        // or at an edge one of the neighbouring ground's glyphs
        bool steep = profile.Soft && slopeDm >= SteepDm;
        string accents = steep ? SteepAccents : profile.Accents;
        int accentOdds = steep ? Math.Max(120, profile.AccentPerMille * 2) : profile.AccentPerMille;
        if (rich)
            accentOdds = accentOdds * 3 / 2;
        if (accents.Length > 0 && LookNoise.Chance(seed, x, y, salt + 3, accentOdds))
        {
            glyph = Pick(accents, seed, x, y, salt + 4);
        }
        else if (profile.Soft && LookNoise.Chance(seed, x, y, salt + 5, EdgeBlendPerMille))
        {
            CoverType? beside = (LookNoise.Hash(seed, x, y, salt + 6) & 3) switch
            {
                0 => north,
                1 => east,
                2 => south,
                _ => west,
            };
            if (beside is { } other && other != cover && VisualProfiles.For(other, null) is { Soft: true } next)
                glyph = Pick(next.Primary, seed, x, y, salt + 7);
        }

        // colours: blend through the patch, tint by biome, grey where steep
        float t = Math.Clamp((field - 0.35f) / 0.4f, 0f, 1f);
        t = t * t * (3f - 2f * t);
        var groundColor = profile.GroundA.Lerp(profile.GroundB, t);
        var ink = profile.InkA.Lerp(profile.InkB, t);
        if (biome is { } b)
        {
            var (tintGround, tintInk) = VisualProfiles.Tint(b);
            groundColor = Multiply(groundColor, tintGround);
            ink = Multiply(ink, tintInk);
        }
        if (steep)
            groundColor = groundColor.Lerp(SteepGround, 0.25f);
        // wet ground is darker: the tide's edge (wet sand has its own profile), and anything but water after rain
        bool water = cover is CoverType.ShallowWater or CoverType.DeepWater;
        if (tideWet && cover != CoverType.Sand && !water)
            groundColor = Scale(groundColor, 0.8f);
        if (rainWet > 0f && !water)
            groundColor = Scale(groundColor, 1f - 0.16f * Math.Clamp(rainWet, 0f, 1f));

        bool mark = LookNoise.Chance(seed, x, y, salt + 8, rich ? profile.MarkPerMille * 2 : profile.MarkPerMille);
        float markX = mark ? LookNoise.Unit(seed, x, y, salt + 9) : 0f;
        float markY = mark ? LookNoise.Unit(seed, x, y, salt + 10) : 0f;
        return new GroundPick(glyph, ink, groundColor, profile.Shadow, mark, markX, markY);
    }

    private static char Pick(string glyphs, int seed, int x, int y, int salt) =>
        glyphs[(int)(LookNoise.Hash(seed, x, y, salt) % (uint)glyphs.Length)];

    private static Color Scale(Color c, float f) => new(c.R * f, c.G * f, c.B * f, c.A);

    /// <summary>Colour times tint, channel by channel; the alpha stays the colour's.</summary>
    private static Color Multiply(Color c, Color tint) => new(c.R * tint.R, c.G * tint.G, c.B * tint.B, c.A);
}
