using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim.Actors;

/// <summary>Decides what an actor does on its turn. The player, AI and replays all implement this.</summary>
public interface IActorController
{
    /// <summary>
    /// Returns the action for this turn, or null to wait for input (the turn is not used up).
    /// Called once per turn that actually happens, so AI may draw random numbers here.
    /// </summary>
    ActorAction? Decide(Actor self, Simulation sim);
}

/// <summary>Placeholder AI: wanders, mostly keeping its heading, and sometimes holds still.</summary>
public sealed class RandomWalker : IActorController
{
    private readonly Rng _rng;

    public RandomWalker(Rng rng) => _rng = rng;

    public ActorAction? Decide(Actor self, Simulation sim)
    {
        if (_rng.Chance(1, 5))
            return new WaitAction(self.Movement.StepTicks);

        if (_rng.Chance(7, 10) && sim.CanMove(self, self.Facing))
            return new MoveAction(self.Facing);

        int count = Directions.All.Count;
        int start = _rng.NextInt(count);
        for (int i = 0; i < count; i++)
        {
            var direction = Directions.All[(start + i) % count];
            if (sim.CanMove(self, direction))
                return new MoveAction(direction);
        }
        return new WaitAction(self.Movement.StepTicks);
    }
}

/// <summary>Plays back a fixed list of actions, then waits. Used for tests and input replays.</summary>
public sealed class ScriptedController : IActorController
{
    private readonly IReadOnlyList<ActorAction> _script;
    private int _next;

    public ScriptedController(IReadOnlyList<ActorAction> script) => _script = script;

    public ActorAction? Decide(Actor self, Simulation sim) =>
        _next < _script.Count ? _script[_next++] : new WaitAction(Ticks.PerSecond);
}

/// <summary>
/// Human input. The front end sets <see cref="Pending"/>; the simulation pauses on this
/// actor's turn until it does. Every action taken is recorded for replays.
/// </summary>
public sealed class PlayerController : IActorController
{
    private readonly List<ActorAction> _history = new();

    /// <summary>The action to take on the next turn. Cleared once used.</summary>
    public ActorAction? Pending { get; set; }

    /// <summary>Every action the player has taken, in order. Feed it to a ScriptedController to replay.</summary>
    public IReadOnlyList<ActorAction> History => _history;

    public ActorAction? Decide(Actor self, Simulation sim)
    {
        var action = Pending;
        if (action is null)
            return null;

        Pending = null;
        _history.Add(action);
        return action;
    }
}
