#nullable enable
using System.Collections.Generic;
using System.Linq;
using BlueHarvest.Sim;
using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.World;
using Godot;

namespace BlueHarvest.Game.Ui;

/// <summary>
/// Puts the player's sense cues into words: what it is, where (an arrow and how far), and the colour
/// that says how much it matters. All the sidebar's sense wording lives here, so it can be tuned (or
/// translated) in one place. Not a Godot node.
/// </summary>
public static class SenseWords
{
    /// <summary>How many cues each sense line shows.</summary>
    public const int Shown = 3;

    /// <summary>One sense's line: its strongest cues, then how many more there are.</summary>
    public static string Line(Simulation sim, Actor player, IReadOnlyList<SenseCue> cues, Sense sense)
    {
        string muted = HudTheme.Hex(HudTheme.Muted);
        if (cues.Count == 0)
            return $"[color={muted}]{Nothing(sim, player, sense)}[/color]";
        var parts = cues.Take(Shown).Select(c => Cue(sim, c));
        string more = cues.Count > Shown ? $" [color={muted}]+{cues.Count - Shown}[/color]" : "";
        return string.Join("  ·  ", parts) + more;
    }

    /// <summary>One cue: arrow, words, distance, in its colour.</summary>
    public static string Cue(Simulation sim, SenseCue cue)
    {
        string arrow = cue.Toward is { } d ? Arrow(d) + " " : "";
        string where = cue.At is not null && cue.Distance > 0 ? $" {Distance(cue.Distance)}" : "";
        return $"[color={HudTheme.Hex(Tone(sim, cue))}]{arrow}{HudTheme.Escape(What(sim, cue))}{where}[/color]";
    }

    /// <summary>The words for a cue, without where it is: for the log, edge markers and the cue's own line.</summary>
    public static string What(Simulation sim, SenseCue cue)
    {
        string who = cue.ActorId is { } id ? Called(sim.Actors[id]) : "something";
        int s = cue.Strength;
        return cue.Kind switch
        {
            CueKind.Creature => who,
            CueKind.Person => who,
            CueKind.Corpse => cue.ActorId is { } dead ? $"{sim.Actors[dead].Name}'s body" : "a body",
            CueKind.Fire => s >= 70 ? "fire, roaring" : "fire",
            CueKind.Smoke => s >= 50 ? "thick smoke" : "smoke",
            CueKind.Flare => "a flare",
            CueKind.Noise => $"{(s >= 60 ? "loud " : s <= 18 ? "faint " : "")}{cue.NoiseKind ?? "a noise"}{(cue.ActorId is not null ? $" ({who})" : "")}",
            CueKind.Water => s >= 50 ? "rushing water" : "running water",
            CueKind.Surf => s >= 60 ? "pounding surf" : "surf",
            CueKind.Rain => s >= 70 ? "the rain, hammering" : "rain",
            CueKind.Thunder => "thunder",
            CueKind.Trail => s >= 40 ? $"a strong animal smell{Whose(sim, cue)}" : $"an animal smell{Whose(sim, cue)}",
            CueKind.Blood => "blood",
            CueKind.Fuel => "fuel",
            CueKind.Burning => "burning",
            CueKind.Death => "death",
            CueKind.Tremor => s >= 75 ? "the ground shudders" : s >= 45 ? "heavy footfalls" : "a faint tremor",
            CueKind.Heat => s >= 60 ? "scorching heat" : "heat",
            CueKind.Current => s >= 80 ? "the current drags at you" : "the current pushes",
            CueKind.Wading => "water to your knees",
            CueKind.Wind => s >= 50 ? "a strong wind" : "a breeze",
            _ => cue.Kind.ToString().ToLowerInvariant(),
        };
    }

    /// <summary>What a sense says when it has nothing.</summary>
    public static string Nothing(Simulation sim, Actor player, Sense sense) => sense switch
    {
        Sense.Sight => sim.Light.LevelAt(player.Position) < 15 ? "dark all round" : "nothing moving",
        Sense.Sound => "quiet",
        Sense.Smell => "nothing on the air",
        _ => "the ground is still",
    };

    /// <summary>The name to use for someone: "the Rex" for a creature, a person's own name.</summary>
    public static string Called(Actor actor) =>
        actor.Species == Species.Human ? actor.Name : $"the {actor.Name}";

    /// <summary>A scent trail names its owner only if it's someone the player knows by name (a person).</summary>
    private static string Whose(Simulation sim, SenseCue cue) =>
        cue.ActorId is { } id && sim.Actors[id].Species == Species.Human ? $" ({sim.Actors[id].Name})" : "";

    /// <summary>The colour of a cue: danger red, people amber, the ground earth, the rest muted.</summary>
    public static Color Tone(Simulation sim, SenseCue cue)
    {
        if (cue.Danger)
            return HudTheme.Danger;
        return cue.Kind switch
        {
            CueKind.Person or CueKind.Flare or CueKind.Fire or CueKind.Blood or CueKind.Fuel => HudTheme.Amber,
            CueKind.Tremor or CueKind.Current => HudTheme.Earth,
            CueKind.Noise when cue.Strength >= 60 => HudTheme.Amber,
            CueKind.Creature or CueKind.Noise or CueKind.Trail or CueKind.Smoke or CueKind.Heat or CueKind.Corpse or CueKind.Death => HudTheme.Ink,
            _ => HudTheme.Muted,
        };
    }

    /// <summary>A rough distance: close, then metres, then far.</summary>
    public static string Distance(int tiles) => tiles <= 2 ? "close" : tiles <= 60 ? $"{tiles} m" : "far";

    public static string Arrow(Direction d) => d switch
    {
        Direction.North => "↑",
        Direction.NorthEast => "↗",
        Direction.East => "→",
        Direction.SouthEast => "↘",
        Direction.South => "↓",
        Direction.SouthWest => "↙",
        Direction.West => "←",
        _ => "↖",
    };

    /// <summary>The exposure line: a feeling, not a measurement.</summary>
    public static string ExposureLine(Simulation sim, Exposure exposure) => exposure.Stage switch
    {
        ExposureStage.Seen when exposure.WatcherId is { } id => $"{Capitalized(Called(sim.Actors[id]))} sees you.",
        ExposureStage.Seen or ExposureStage.Spotted => "Something has spotted you.",
        ExposureStage.Watched => "You feel like something is watching you.",
        _ => "Nothing seems to have noticed you.",
    };

    /// <summary>The one-word verdict at the head of the exposure block.</summary>
    public static string Verdict(Exposure exposure) => exposure.Stage switch
    {
        ExposureStage.Seen => "seen",
        ExposureStage.Spotted => "spotted",
        ExposureStage.Watched => "watched",
        _ => "unnoticed",
    };

    private static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
