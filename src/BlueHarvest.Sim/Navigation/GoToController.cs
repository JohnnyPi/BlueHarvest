using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim.Navigation;

/// <summary>
/// Walks to a goal that may move, replanning when the goal drifts, the next move is blocked, or the
/// plan gets old. Waits when there and backs off for a while when no path exists.
/// A building block for the AI; on its own it knows where the goal is without perceiving it.
/// </summary>
public sealed class GoToController : IActorController
{
    private const int ReplanAfterSteps = 20;
    private const int GoalDriftTiles = 2;
    private const int FailureBackoffTurns = 10;

    private readonly Func<TilePos> _goal;
    private readonly int _tolerance;
    private List<Direction> _path = new();
    private int _next;
    private int _stepsSincePlan;
    private int _backoff;

    public GoToController(Func<TilePos> goal, int tolerance)
    {
        _goal = goal;
        _tolerance = tolerance;
    }

    /// <summary>The goal the current plan was made for, or null with no plan.</summary>
    public TilePos? PlannedGoal { get; private set; }

    /// <summary>Moves still to make on the current plan.</summary>
    public IReadOnlyList<Direction> RemainingPath => _path.Skip(_next).ToList();

    public ActorAction? Decide(Actor self, Simulation sim)
    {
        var goal = _goal();
        var wait = new WaitAction(self.Movement.StepTicks);
        if (Math.Max(Math.Abs(self.Position.X - goal.X), Math.Abs(self.Position.Y - goal.Y)) <= _tolerance)
            return wait;

        bool stale = _next >= _path.Count
            || PlannedGoal is not { } planned
            || Math.Max(Math.Abs(planned.X - goal.X), Math.Abs(planned.Y - goal.Y)) > GoalDriftTiles
            || _stepsSincePlan >= ReplanAfterSteps
            || !sim.CanMove(self, _path[_next]);

        if (stale)
        {
            if (_backoff > 0)
            {
                _backoff--;
                return wait;
            }

            var path = Pathfinder.FindPath(sim, self, goal, _tolerance);
            if (path is null || path.Count == 0)
            {
                _path.Clear();
                _next = 0;
                PlannedGoal = null;
                _backoff = FailureBackoffTurns;
                return wait;
            }

            _path = path.ToList();
            _next = 0;
            _stepsSincePlan = 0;
            PlannedGoal = goal;
        }

        _stepsSincePlan++;
        return new MoveAction(_path[_next++]);
    }
}
