#nullable enable
using System.Collections.Generic;
using BlueHarvest.Sim.World;
using BlueHarvest.Sim.World.Generation;
using Godot;

namespace BlueHarvest.Game.Rendering;

/// <summary>
/// The Layered mode's profiles for natural ground, and the tints biomes give them. Structures (walls,
/// doors, fences, floors, equipment, vehicles) and roads have no profile: they keep their Classic look
/// until the facility work (V4), because their glyphs carry meaning.
/// </summary>
public static class VisualProfiles
{
    private static Color C(float r, float g, float b, float a = 1f) => new(r, g, b, a);

    /// <summary>One profile per cover type that has one.</summary>
    private static readonly Dictionary<CoverType, VisualProfile> ByCover = new();

    /// <summary>Profiles for a cover on a particular kind of generator ground (cloud forest is dense jungle on Cloud ground).</summary>
    private static readonly Dictionary<(CoverType, Cover), VisualProfile> ByGround = new();

    public static IEnumerable<VisualProfile> All
    {
        get
        {
            foreach (var p in ByCover.Values) yield return p;
            foreach (var p in ByGround.Values) yield return p;
            yield return WetSand;
        }
    }

    /// <summary>
    /// Sand at the sea's edge that the tide has uncovered within the last half tide (below
    /// Simulation.RecentHighWaterDm): darker and smoother, with the odd shell or puddle.
    /// </summary>
    public static readonly VisualProfile WetSand = new("VIS-WET-SAND", CoverType.Sand,
        Primary: "·.·˙", Patch: "∴·.·", Accents: "°", AccentPerMille: 30,
        GroundA: C(0.46f, 0.41f, 0.29f), GroundB: C(0.43f, 0.39f, 0.29f),
        InkA: C(0.80f, 0.76f, 0.62f, 0.42f), InkB: C(0.76f, 0.74f, 0.64f, 0.45f),
        PatchScaleM: 9, Salt: 28, Motion: "water", MarkPerMille: 40);

    static VisualProfiles()
    {
        Add(new VisualProfile("VIS-GRASS", CoverType.Grass,
            Primary: "..,'·.,`", Patch: "\",;\"·,", Accents: "τ;", AccentPerMille: 30,
            GroundA: C(0.16f, 0.30f, 0.14f), GroundB: C(0.20f, 0.33f, 0.15f),
            InkA: C(0.82f, 0.90f, 0.62f, 0.26f), InkB: C(0.90f, 0.92f, 0.58f, 0.32f),
            PatchScaleM: 14, Salt: 11, Motion: "vegetation_sway", MarkPerMille: 110));
        Add(new VisualProfile("VIS-SCRUB", CoverType.Scrub,
            Primary: ",;,ⁿ,", Patch: "τ,;♧τ", Accents: "∴♧", AccentPerMille: 50,
            GroundA: C(0.24f, 0.32f, 0.15f), GroundB: C(0.27f, 0.30f, 0.14f),
            InkA: C(0.75f, 0.85f, 0.45f, 0.55f), InkB: C(0.82f, 0.80f, 0.42f, 0.6f),
            PatchScaleM: 10, Salt: 12, Motion: "vegetation_sway", MarkPerMille: 90));
        Add(new VisualProfile("VIS-SPARSE-JUNGLE", CoverType.SparseJungle,
            Primary: "\"ʻτ\"♧ʻ", Patch: "ψ♧ψτ\"ψ", Accents: "♣ψ", AccentPerMille: 70,
            GroundA: C(0.10f, 0.25f, 0.10f), GroundB: C(0.11f, 0.24f, 0.13f),
            InkA: C(0.45f, 0.80f, 0.35f, 0.75f), InkB: C(0.48f, 0.82f, 0.50f, 0.78f),
            PatchScaleM: 12, Salt: 13, Motion: "vegetation_sway", MarkPerMille: 40));
        Add(new VisualProfile("VIS-DENSE-JUNGLE", CoverType.DenseJungle,
            Primary: "♣♠♣♣Ψ♠", Patch: "Ψ♠Ψ♣Ψ", Accents: "ψτ", AccentPerMille: 50,
            GroundA: C(0.05f, 0.16f, 0.06f), GroundB: C(0.04f, 0.13f, 0.05f),
            InkA: C(0.30f, 0.65f, 0.25f, 0.85f), InkB: C(0.36f, 0.70f, 0.30f, 0.88f),
            PatchScaleM: 16, Salt: 14, Motion: "vegetation_sway", Shadow: true));
        Add(new VisualProfile("VIS-TALL-GRASS", CoverType.TallGrass,
            Primary: "\"\"ʻ\"", Patch: "\";¦\"", Accents: "τ,", AccentPerMille: 40,
            GroundA: C(0.33f, 0.38f, 0.17f), GroundB: C(0.36f, 0.37f, 0.16f),
            InkA: C(0.79f, 0.81f, 0.45f, 0.7f), InkB: C(0.86f, 0.80f, 0.46f, 0.72f),
            PatchScaleM: 11, Salt: 15, Motion: "vegetation_sway", MarkPerMille: 60));
        Add(new VisualProfile("VIS-REEDS", CoverType.Reeds,
            Primary: "¦¦ψ¦", Patch: "¦\"¦,", Accents: "∽", AccentPerMille: 35,
            GroundA: C(0.18f, 0.29f, 0.24f), GroundB: C(0.16f, 0.27f, 0.25f),
            InkA: C(0.62f, 0.77f, 0.63f, 0.8f), InkB: C(0.58f, 0.76f, 0.70f, 0.8f),
            PatchScaleM: 9, Salt: 16, Motion: "vegetation_sway"));
        Add(new VisualProfile("VIS-FERNS", CoverType.Ferns,
            Primary: "ψψτψ", Patch: "ψΨψ\"", Accents: "♧⚘", AccentPerMille: 35,
            GroundA: C(0.13f, 0.25f, 0.16f), GroundB: C(0.12f, 0.23f, 0.17f),
            InkA: C(0.50f, 0.81f, 0.54f, 0.75f), InkB: C(0.55f, 0.84f, 0.60f, 0.78f),
            PatchScaleM: 10, Salt: 17, Motion: "vegetation_sway", Shadow: true));
        Add(new VisualProfile("VIS-SAND", CoverType.Sand,
            Primary: "··.·", Patch: "∴·∵·", Accents: "°'", AccentPerMille: 25,
            GroundA: C(0.61f, 0.54f, 0.36f), GroundB: C(0.58f, 0.51f, 0.34f),
            InkA: C(0.91f, 0.84f, 0.65f, 0.5f), InkB: C(0.86f, 0.78f, 0.60f, 0.55f),
            PatchScaleM: 9, Salt: 18, MarkPerMille: 90));
        Add(new VisualProfile("VIS-MUD", CoverType.Mud,
            Primary: "∴∵∴·", Patch: "∵,∴.", Accents: "°", AccentPerMille: 30,
            GroundA: C(0.29f, 0.24f, 0.16f), GroundB: C(0.25f, 0.21f, 0.15f),
            InkA: C(0.66f, 0.56f, 0.40f, 0.6f), InkB: C(0.58f, 0.52f, 0.42f, 0.6f),
            PatchScaleM: 8, Salt: 19, MarkPerMille: 60));
        Add(new VisualProfile("VIS-BARE-ROCK", CoverType.BareRock,
            Primary: "∩∴·∩", Patch: "▴∩^∴", Accents: "△", AccentPerMille: 30,
            GroundA: C(0.33f, 0.32f, 0.30f), GroundB: C(0.36f, 0.34f, 0.31f),
            InkA: C(0.72f, 0.70f, 0.65f, 0.5f), InkB: C(0.76f, 0.73f, 0.66f, 0.55f),
            PatchScaleM: 13, Salt: 20, MarkPerMille: 120, Soft: false));
        Add(new VisualProfile("VIS-BOULDERS", CoverType.Rock,
            Primary: "▲▲▴▲", Patch: "▲∩▲^", Accents: "", AccentPerMille: 0,
            GroundA: C(0.38f, 0.36f, 0.33f), GroundB: C(0.40f, 0.37f, 0.33f),
            InkA: C(0.80f, 0.78f, 0.72f), InkB: C(0.84f, 0.80f, 0.72f),
            PatchScaleM: 12, Salt: 21, Soft: false));
        Add(new VisualProfile("VIS-OLD-LAVA", CoverType.Lava,
            Primary: "∴∵·∴", Patch: "▒∴▒∵", Accents: "▰", AccentPerMille: 45,
            GroundA: C(0.16f, 0.15f, 0.14f), GroundB: C(0.13f, 0.12f, 0.12f),
            InkA: C(0.54f, 0.50f, 0.47f, 0.45f), InkB: C(0.50f, 0.46f, 0.46f, 0.4f),
            PatchScaleM: 15, Salt: 22, MarkPerMille: 80, Soft: false));
        Add(new VisualProfile("VIS-ASH", CoverType.Ash,
            Primary: "·,·.", Patch: "∵·∴·", Accents: "'", AccentPerMille: 40,
            GroundA: C(0.12f, 0.11f, 0.10f), GroundB: C(0.10f, 0.10f, 0.09f),
            InkA: C(0.45f, 0.42f, 0.38f, 0.6f), InkB: C(0.40f, 0.38f, 0.36f, 0.55f),
            PatchScaleM: 7, Salt: 23, MarkPerMille: 60, Soft: false));
        Add(new VisualProfile("VIS-RUBBLE", CoverType.Rubble,
            Primary: "∴:∵", Patch: "∵∴:", Accents: "▪", AccentPerMille: 60,
            GroundA: C(0.26f, 0.24f, 0.22f), GroundB: C(0.24f, 0.23f, 0.22f),
            InkA: C(0.65f, 0.60f, 0.55f, 0.7f), InkB: C(0.60f, 0.58f, 0.55f, 0.7f),
            PatchScaleM: 6, Salt: 24, Soft: false));
        // shallow water never uses ≈ or ≋: those mean water too deep to wade
        Add(new VisualProfile("VIS-SHALLOW-WATER", CoverType.ShallowWater,
            Primary: "~~∼~", Patch: "∼~∼∼", Accents: "", AccentPerMille: 0,
            GroundA: C(0.12f, 0.31f, 0.41f), GroundB: C(0.13f, 0.33f, 0.40f),
            InkA: C(0.50f, 0.75f, 0.85f, 0.6f), InkB: C(0.55f, 0.78f, 0.85f, 0.6f),
            PatchScaleM: 12, Salt: 25, Motion: "water", Soft: false));
        Add(new VisualProfile("VIS-DEEP-WATER", CoverType.DeepWater,
            Primary: "≈≈≋≈", Patch: "≋≈∼≈", Accents: "", AccentPerMille: 0,
            GroundA: C(0.06f, 0.16f, 0.25f), GroundB: C(0.05f, 0.14f, 0.24f),
            InkA: C(0.31f, 0.53f, 0.72f, 0.6f), InkB: C(0.34f, 0.56f, 0.74f, 0.6f),
            PatchScaleM: 20, Salt: 26, Motion: "water", Soft: false));

        // water over coral: paler, greener, with branching coral showing through (still no ≈ in the shallows)
        AddOnGround(Cover.Reef, new VisualProfile("VIS-REEF-SHALLOWS", CoverType.ShallowWater,
            Primary: "~∼~∼", Patch: "∼°~∼", Accents: "♧ψ", AccentPerMille: 70,
            GroundA: C(0.13f, 0.37f, 0.42f), GroundB: C(0.15f, 0.40f, 0.42f),
            InkA: C(0.62f, 0.86f, 0.84f, 0.6f), InkB: C(0.70f, 0.88f, 0.82f, 0.62f),
            PatchScaleM: 7, Salt: 29, Motion: "water", Soft: false));
        AddOnGround(Cover.Reef, new VisualProfile("VIS-REEF-DEEP", CoverType.DeepWater,
            Primary: "≈≋≈≈", Patch: "≋≈∼≈", Accents: "♧", AccentPerMille: 30,
            GroundA: C(0.08f, 0.24f, 0.32f), GroundB: C(0.09f, 0.26f, 0.32f),
            InkA: C(0.42f, 0.66f, 0.76f, 0.6f), InkB: C(0.48f, 0.70f, 0.76f, 0.6f),
            PatchScaleM: 9, Salt: 30, Motion: "water", Soft: false));
        AddOnGround(Cover.Cloud, new VisualProfile("VIS-CLOUD-FOREST", CoverType.DenseJungle,
            Primary: "♣Ψψ♣", Patch: "ψ¦ψΨ", Accents: "°¦", AccentPerMille: 45,
            GroundA: C(0.08f, 0.17f, 0.12f), GroundB: C(0.10f, 0.19f, 0.15f),
            InkA: C(0.45f, 0.70f, 0.55f, 0.8f), InkB: C(0.52f, 0.74f, 0.62f, 0.82f),
            PatchScaleM: 14, Salt: 27, Motion: "vegetation_sway", Shadow: true));
    }

    private static void Add(VisualProfile profile) => ByCover[profile.Cover] = profile;

    private static void AddOnGround(Cover ground, VisualProfile profile) => ByGround[(profile.Cover, ground)] = profile;

    /// <summary>
    /// The profile for a cover, picking a ground-specific one where the generator's ground still fits
    /// the cover (a tile that burnt keeps its old ground, but Ash on Cloud ground has no profile of its
    /// own, so it simply gets Ash's). Null for covers without a profile.
    /// </summary>
    public static VisualProfile? For(CoverType cover, Cover? ground)
    {
        if (ground is { } g && ByGround.TryGetValue((cover, g), out var special))
            return special;
        return ByCover.TryGetValue(cover, out var profile) ? profile : null;
    }

    /// <summary>
    /// What a biome does to ground and glyph colours: multipliers near 1, so the biome shows as a
    /// gradual shift (dry scrub yellower, rainforest deeper, cloud forest cooler) rather than a new palette.
    /// </summary>
    public static (Color Ground, Color Ink) Tint(Biome biome) => biome switch
    {
        Biome.Strand => (C(1.06f, 1.03f, 0.95f), C(1.03f, 1.02f, 0.97f)),
        Biome.Marsh => (C(0.92f, 1.00f, 1.04f), C(0.96f, 1.00f, 1.02f)),
        Biome.DryScrub => (C(1.07f, 1.00f, 0.87f), C(1.04f, 1.00f, 0.92f)),
        Biome.Grassland => (C(1.05f, 1.03f, 0.92f), C(1.03f, 1.02f, 0.95f)),
        Biome.Moist => (C(0.94f, 1.02f, 0.96f), C(0.97f, 1.01f, 0.98f)),
        Biome.Wet => (C(0.86f, 0.98f, 0.96f), C(0.93f, 1.00f, 0.98f)),
        Biome.Cloud => (C(0.92f, 0.98f, 1.06f), C(0.95f, 0.99f, 1.04f)),
        Biome.Ridge => (C(0.98f, 0.97f, 0.97f), C(0.99f, 0.98f, 0.98f)),
        Biome.Cliff => (C(0.95f, 0.95f, 0.96f), C(0.97f, 0.97f, 0.98f)),
        Biome.Lava => (C(0.95f, 0.92f, 0.92f), C(0.97f, 0.95f, 0.95f)),
        _ => (C(1f, 1f, 1f), C(1f, 1f, 1f)),
    };
}
