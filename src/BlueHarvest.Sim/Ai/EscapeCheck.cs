using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Navigation;
using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim.Ai;

/// <summary>
/// The testbed's win condition, read from one creature's point of view.
/// You've escaped only when both hold: it can't reach you, and its attention is on something else.
/// </summary>
/// <param name="OutOfReach">
/// No route brings its jaws to you: too high (a ledge or platform), too low, under a roof, or nowhere it
/// can stand near enough. A platform on posts it could knock down still counts as out of reach for now.
/// </param>
/// <param name="AttentionElsewhere">Its focus is not you: something else, or nothing.</param>
public readonly record struct EscapeStatus(bool OutOfReach, bool AttentionElsewhere)
{
    public bool Escaped => OutOfReach && AttentionElsewhere;
}

public static class EscapeCheck
{
    /// <summary>
    /// Reads the creature's contact memory and runs its pathfinder: is there a route to somewhere its
    /// jaws could reach <paramref name="target"/> from (near enough, and within its reach height)?
    /// A search that runs out of <paramref name="maxNodes"/> counts as no route.
    /// A head outdoors can't reach under a roof, so someone indoors is out of reach of an outdoor creature.
    /// </summary>
    public static EscapeStatus Evaluate(Simulation sim, Actor creature, CreatureBrain brain, Actor target, int maxNodes = 60_000)
    {
        var at = target.Position;
        bool targetIndoor = sim.Map.IsIndoor(at);
        bool headIndoor = sim.Map.IsIndoor(sim.EyeTile(creature));
        int reach = Math.Max(1, creature.Species.Body.HeadReach + (creature.IsLarge ? 1 : 0));
        var species = creature.Species;
        int targetZ = sim.StandHeight(target);

        bool CanBiteFrom(TilePos anchor)
        {
            if (Math.Max(Math.Abs(anchor.X - at.X), Math.Abs(anchor.Y - at.Y)) > reach)
                return false;
            int rise = targetZ - sim.Terrain[anchor];
            return rise <= species.ReachUpDm && -rise <= species.ReachDownDm;
        }

        bool outOfReach = targetIndoor != headIndoor
            || Pathfinder.FindPath(sim, creature, CanBiteFrom, at, reach, maxNodes) is null;

        var focus = brain.Focus;
        bool elsewhere = focus is null || focus.Key != ContactKey.ForActor(target.Id);
        return new EscapeStatus(outOfReach, elsewhere);
    }
}
