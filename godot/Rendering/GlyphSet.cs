#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace BlueHarvest.Game.Rendering;

/// <summary>
/// The map's glyph vocabulary (design guide §04, settled 21 Nov 2026): which glyphs may be drawn, which
/// are reserved for things that carry meaning, and a check at start-up that the font has them all.
/// Box-drawing and block glyphs are drawn as shapes (<see cref="BoxGlyphs"/>), so the font needn't have those.
/// </summary>
public static class GlyphSet
{
    /// <summary>
    /// The approved non-ASCII glyphs, by family. Every one is single width in DejaVu Sans Mono 2.37.
    /// The guide's ∿ and ﹏ aren't in the font: ∽ and ⌒ stand in for them.
    /// </summary>
    public const string Approved =
        "♣♠♧♤ψΨτʻⁿ⚘¦" +            // organic: canopy, ferns, shrubs, stems
        "·∴∵˙°" +                   // ground: grains, litter, grit, shells, droplets
        "▲△▴∩▰▧▪■" +                // stone and elevation
        "≈∼≋∽⌒~" +                  // water and air
        "≡∕◌○◉↑↗" +                 // tracks, breaks, rings and devices, direction
        "✚♨☕•▣" +                   // item families: medical, food, drinks, ammunition, furniture
        "─│┌┐└┘├┤┬┴┼═║╔╗╚╝╠╣╦╩╬╤╧╪╫╌╭╮╰╯╱╲" + // architecture (drawn as shapes)
        "━┃┏┓┗┛┣┫┳┻╋" +             // heavy framing (drawn as shapes)
        "░▒▓█▀▄▌▐";                 // shade and mass (drawn as shapes)

    /// <summary>
    /// Glyphs that mean something: doors (+ /), exits and ladders (&gt; &lt; H), lights (*), the player
    /// (@), bodies and breaches (%), and letters and digits (people, animals, items). No texture may use
    /// them, so a door never turns into a plant because it stands in a greenhouse.
    /// </summary>
    public const string Reserved = "+/<>H*@%";

    public static bool IsReserved(char c) => Reserved.Contains(c) || char.IsAsciiLetterOrDigit(c);

    /// <summary>Whether a glyph may be drawn: printable ASCII or an approved glyph.</summary>
    public static bool IsApproved(char c) => c is >= '!' and <= '~' || c == ' ' || Approved.Contains(c);

    /// <summary>
    /// Checks the font and the look tables and reports in the Godot output: one line when all is well,
    /// a warning per problem otherwise. Pass the font before any fallback is added, so a glyph it lacks
    /// isn't hidden by a system font. Returns the number of problems.
    /// </summary>
    public static int Audit(Font font)
    {
        int problems = 0;
        var wanted = new SortedSet<char>(Approved.Where(c => !BoxGlyphs.Handles(c)));
        for (char c = '!'; c <= '~'; c++)
            wanted.Add(c);
        var missing = wanted.Where(c => !font.HasChar(c)).ToList();
        if (missing.Count > 0)
        {
            GD.PushWarning($"Glyph check: {font.GetFontName()} lacks {missing.Count} map glyph(s): {new string(missing.ToArray())}");
            problems += missing.Count;
        }

        foreach (char c in MapLook.AllGlyphs.Distinct())
        {
            if (!IsApproved(c))
            {
                GD.PushWarning($"Glyph check: MapLook draws '{c}' (U+{(int)c:X4}), which isn't in the approved set.");
                problems++;
            }
        }
        // items: approved, never a glyph that means something else, and never one the ground uses
        var ground = new HashSet<char>(MapLook.AllGlyphs + FacilityLook.AllGlyphs + string.Concat(VisualProfiles.All.Select(v => v.AllGlyphs)));
        // the items come from the content packs: loading them here is the first use on most runs
        if (!ContentIntegration.GameContent.ItemsReady)
        {
            GD.PushWarning("Glyph check: the items didn't load, so their glyphs weren't checked.");
            problems++;
        }
        foreach (var def in ContentIntegration.GameContent.Catalog?.All ?? System.Array.Empty<BlueHarvest.Sim.Actors.ItemDef>())
        {
            char c = def.Glyph;
            if (!IsApproved(c) || IsReserved(c) || ground.Contains(c))
            {
                GD.PushWarning($"Glyph check: the {def.Name} uses '{c}', which " +
                    (!IsApproved(c) ? "isn't in the approved set." : IsReserved(c) ? "is reserved for things that carry meaning." : "the ground also uses."));
                problems++;
            }
        }
        foreach (char c in FacilityLook.AllGlyphs.Distinct())
        {
            if (!IsApproved(c))
            {
                GD.PushWarning($"Glyph check: FacilityLook draws '{c}' (U+{(int)c:X4}), which isn't in the approved set.");
                problems++;
            }
        }
        foreach (var profile in VisualProfiles.All)
        {
            foreach (char c in profile.AllGlyphs.Distinct())
            {
                if (!IsApproved(c))
                {
                    GD.PushWarning($"Glyph check: {profile.Id} draws '{c}' (U+{(int)c:X4}), which isn't in the approved set.");
                    problems++;
                }
                if (IsReserved(c))
                {
                    GD.PushWarning($"Glyph check: {profile.Id} draws '{c}', which is reserved for things that carry meaning.");
                    problems++;
                }
            }
            if (profile.Primary.Length == 0 || profile.Patch.Length == 0)
            {
                GD.PushWarning($"Glyph check: {profile.Id} has an empty glyph list.");
                problems++;
            }
        }

        if (problems == 0)
            GD.Print($"Glyph check: {wanted.Count} map glyphs in {font.GetFontName()}, {Approved.Count(BoxGlyphs.Handles)} drawn as shapes, {VisualProfiles.All.Count()} visual profiles, all fine.");
        return problems;
    }
}
