using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim.Navigation;

/// <summary>A pose along a planned path: where the anchor is and which way the body faces.</summary>
public readonly record struct PathPose(TilePos Anchor, Direction Facing);

/// <summary>
/// A* pathfinding in ticks of travel time. Small actors search (tile, ground or deck) in 8 directions,
/// so stairs and ramps onto platforms work (ladders aren't planned yet: climbing is a separate action).
/// Large bodies search (tile, facing) poses with moves "step forward" and "turn 45°", checking their
/// whole core at every pose, so paths respect width, turning room and what they can smash.
/// Other small actors are ignored (they move); other large bodies are obstacles.
/// </summary>
public static class Pathfinder
{
    /// <summary>
    /// Moves that bring the actor's anchor within <paramref name="tolerance"/> tiles (Chebyshev) of
    /// <paramref name="goal"/>, as MoveAction directions, or null if there's no path within
    /// <paramref name="maxNodes"/> expansions. An empty list means it's already there.
    /// </summary>
    public static IReadOnlyList<Direction>? FindPath(Simulation sim, Actor actor, TilePos goal, int tolerance = 0, int maxNodes = 60_000) =>
        FindPath(sim, actor, p => Chebyshev(p, goal) <= tolerance, goal, tolerance, maxNodes);

    /// <summary>
    /// Moves that bring the actor's anchor to any tile where <paramref name="isGoal"/> holds, or null.
    /// Every goal tile must lie within <paramref name="slack"/> tiles (Chebyshev) of
    /// <paramref name="near"/>; that keeps the search's estimate honest.
    /// </summary>
    public static IReadOnlyList<Direction>? FindPath(Simulation sim, Actor actor, Func<TilePos, bool> isGoal, TilePos near,
        int slack, int maxNodes = 60_000)
    {
        return actor.IsLarge
            ? Search(sim, actor, isGoal, near, slack, maxNodes, layers: 8, startLayer: (int)actor.Facing, LargeNeighbors)
            : Search(sim, actor, isGoal, near, slack, maxNodes, layers: 2, startLayer: actor.OnDeck ? 1 : 0, SmallNeighbors);
    }

    /// <summary>The poses an actor passes through following <paramref name="moves"/> from where it stands.</summary>
    public static IReadOnlyList<PathPose> Trace(Actor actor, IEnumerable<Direction> moves)
    {
        var poses = new List<PathPose>();
        var anchor = actor.Position;
        var facing = actor.Facing;
        foreach (var move in moves)
        {
            if (actor.IsLarge)
                (anchor, facing) = Simulation.LargeMoveTarget(anchor, facing, move);
            else
                (anchor, facing) = (anchor.Offset(move), move);
            poses.Add(new PathPose(anchor, facing));
        }
        return poses;
    }

    /// <summary>A state to move to. Layer is the facing for large bodies, and 0 ground / 1 deck for small ones.</summary>
    private readonly record struct Neighbor(TilePos Anchor, int Layer, Direction Move, int Cost);

    private delegate void NeighborSource(Simulation sim, Actor actor, TilePos anchor, int layer, List<Neighbor> into);

    private static IReadOnlyList<Direction>? Search(Simulation sim, Actor actor, Func<TilePos, bool> isGoal, TilePos near,
        int slack, int maxNodes, int layers, int startLayer, NeighborSource neighbors)
    {
        int width = sim.Map.Width, height = sim.Map.Height;
        int states = width * height * layers;
        var g = new int[states];
        Array.Fill(g, int.MaxValue);
        var cameFrom = new int[states];
        var moveTaken = new Direction[states];
        var closed = new bool[states];
        var open = new PriorityQueue<int, (int F, long Order)>();
        long order = 0;

        int minStep = Math.Max(1, actor.Movement.StepTicks * 90 / 100); // fastest cover is the track at 90%
        int Heuristic(TilePos p) => Math.Max(0, Chebyshev(p, near) - slack) * minStep;
        int Index(TilePos p, int layer) => (p.Y * width + p.X) * layers + layer;

        int start = Index(actor.Position, startLayer);
        g[start] = 0;
        cameFrom[start] = -1;
        open.Enqueue(start, (Heuristic(actor.Position), order++));

        var buffer = new List<Neighbor>();
        int expanded = 0;
        while (open.TryDequeue(out int current, out _))
        {
            if (closed[current])
                continue;
            closed[current] = true;

            int cell = current / layers;
            var anchor = new TilePos(cell % width, cell / width);
            int layer = current % layers;

            if (isGoal(anchor))
                return Reconstruct(current, cameFrom, moveTaken);
            if (++expanded > maxNodes)
                return null;

            buffer.Clear();
            neighbors(sim, actor, anchor, layer, buffer);
            foreach (var (nextAnchor, nextLayer, move, cost) in buffer)
            {
                int next = Index(nextAnchor, nextLayer);
                if (closed[next])
                    continue;
                int tentative = g[current] + cost;
                if (tentative >= g[next])
                    continue;
                g[next] = tentative;
                cameFrom[next] = current;
                moveTaken[next] = move;
                open.Enqueue(next, (tentative + Heuristic(nextAnchor), order++));
            }
        }
        return null;
    }

    private static void SmallNeighbors(Simulation sim, Actor actor, TilePos anchor, int layer, List<Neighbor> into)
    {
        bool onDeck = layer == 1;
        int fromZ = sim.Map.SurfaceHeight(anchor, onDeck);
        foreach (var direction in Directions.All)
        {
            var (blocked, toDeck) = sim.SmallStep(actor, anchor, onDeck, direction, ignoreSmallActors: true);
            if (blocked is not null)
                continue;
            var to = anchor.Offset(direction);
            var cover = toDeck ? Covers.Deck : sim.Map.CoverAt(to);
            int cost = actor.Movement.CostTicks(sim.Map.SurfaceHeight(to, toDeck) - fromZ, direction.IsDiagonal())
                * cover.MoveCostPercent / 100
                + (!toDeck && sim.IsClosedDoor(to) ? Simulation.DoorTicks : 0);
            into.Add(new Neighbor(to, toDeck ? 1 : 0, direction, Math.Max(1, cost)));
        }
    }

    private static void LargeNeighbors(Simulation sim, Actor actor, TilePos anchor, int layer, List<Neighbor> into)
    {
        var facing = (Direction)layer;
        int fromHeight = sim.Terrain[anchor];

        // Step forward.
        var ahead = anchor.Offset(facing);
        if (sim.Map.InBounds(ahead) && sim.BodyBlockReason(actor, ahead, facing, fromHeight, ignoreSmallActors: true) is null)
        {
            int cost = actor.Movement.CostTicks(sim.Terrain[ahead] - fromHeight, facing.IsDiagonal())
                * sim.Map.CoverAt(ahead).MoveCostPercent / 100
                + sim.SmashCount(actor, ahead, facing) * Simulation.SmashTicks;
            into.Add(new Neighbor(ahead, (int)facing, facing, Math.Max(1, cost)));
        }

        // Turn 45° either way.
        foreach (int delta in new[] { 1, 7 })
        {
            var turned = (Direction)(((int)facing + delta) % 8);
            if (sim.BodyBlockReason(actor, anchor, turned, fromHeight, ignoreSmallActors: true) is not null)
                continue;
            int cost = actor.Movement.TurnTicks + sim.SmashCount(actor, anchor, turned) * Simulation.SmashTicks;
            into.Add(new Neighbor(anchor, (int)turned, turned, Math.Max(1, cost)));
        }
    }

    private static IReadOnlyList<Direction> Reconstruct(int end, int[] cameFrom, Direction[] moveTaken)
    {
        var moves = new List<Direction>();
        for (int state = end; cameFrom[state] != -1; state = cameFrom[state])
            moves.Add(moveTaken[state]);
        moves.Reverse();
        return moves;
    }

    private static int Chebyshev(TilePos a, TilePos b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}
