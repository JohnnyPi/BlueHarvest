#nullable enable
using BlueHarvest.Sim.World;
using Godot;

namespace BlueHarvest.Game.Rendering;

/// <summary>
/// How one kind of natural ground looks in the Layered mode: the design guide's visual profile, kept as
/// a C# table until the format settles (YAML comes with V6). Every glyph string is a list of glyphs,
/// each equally likely (repeat one to weight it). Looks only: nothing here changes what a tile is.
/// </summary>
/// <param name="Id">A stable name, as a content id would be (VIS-DENSE-JUNGLE).</param>
/// <param name="Cover">The cover type it draws.</param>
/// <param name="Primary">The usual glyphs.</param>
/// <param name="Patch">The glyphs inside a patch (a fern stand in sparse jungle, rippled sand), where the patch field is high.</param>
/// <param name="Accents">Rare glyphs scattered through both, at <paramref name="AccentPerMille"/>.</param>
/// <param name="GroundA">Background outside patches; blends to <paramref name="GroundB"/> inside them.</param>
/// <param name="InkA">Glyph colour outside patches; blends to <paramref name="InkB"/> inside them.</param>
/// <param name="PatchScaleM">About how wide a patch is, in metres.</param>
/// <param name="Salt">Keeps this profile's patches apart from every other's.</param>
/// <param name="Motion">The renderer preset that may move it later (none yet): a registered name, never code.</param>
/// <param name="Shadow">Tall growth: a faint copy of the glyph is drawn a pixel or two down and right.</param>
/// <param name="MarkPerMille">How often a tile gets a micro-mark (a speck of grit, a seed head).</param>
/// <param name="Soft">Soft ground blends at its edges: a tile beside other soft ground sometimes takes one of its neighbour's glyphs.</param>
public sealed record VisualProfile(
    string Id,
    CoverType Cover,
    string Primary,
    string Patch,
    string Accents,
    int AccentPerMille,
    Color GroundA,
    Color GroundB,
    Color InkA,
    Color InkB,
    float PatchScaleM,
    int Salt,
    string Motion = "none",
    bool Shadow = false,
    int MarkPerMille = 0,
    bool Soft = true)
{
    /// <summary>Every glyph the profile can draw.</summary>
    public string AllGlyphs => Primary + Patch + Accents;
}
