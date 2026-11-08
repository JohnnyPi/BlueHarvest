using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.Lighting;
using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.Time;
using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim;

/// <summary>
/// The whole game state and its rules. Contains no Godot code, so it runs in tests,
/// the headless runner and the game alike.
/// </summary>
public sealed class Simulation
{
    /// <summary>Time lost walking into something you can't pass.</summary>
    public const int BumpTicks = 10;

    /// <summary>Extra time for each tile of cover a large body smashes through.</summary>
    public const int SmashTicks = 30;

    /// <summary>Noises older than this are forgotten.</summary>
    public const int NoiseMemoryTicks = 300;

    public const int BumpLoudness = 10;
    public const int SmashLoudness = 40;
    public const int StrikeLoudness = 25;

    private readonly List<Actor> _actors = new();
    private readonly List<LightSource> _lights = new();
    private readonly List<NoiseEvent> _noises = new();
    private readonly Scheduler _scheduler = new();
    private readonly int[] _occupant; // actor id + 1 for each core tile, 0 if empty
    private LightGrid? _lightGrid;
    private int _ambientLight = 100;

    public Simulation(WorldMap map, ulong seed)
    {
        Map = map;
        Rng = new Rng(seed);
        _occupant = new int[map.Width * map.Height];
    }

    /// <summary>A map with heights only; every tile is grass.</summary>
    public Simulation(HeightMap terrain, ulong seed)
        : this(new WorldMap(terrain, new CoverMap(terrain.Width, terrain.Height)), seed)
    {
    }

    /// <summary>Raised when an actor tries to move and can't, with a short reason ("too steep to climb (2.0 m)").</summary>
    public event Action<Actor, string>? MoveBlocked;

    /// <summary>Raised when a large body's head or tail sweeps into another actor: (striker, victim).</summary>
    public event Action<Actor, Actor>? ActorStruck;

    public WorldMap Map { get; }

    public HeightMap Terrain => Map.Heights;

    public CoverMap Cover => Map.Cover;

    /// <summary>Root random stream. Systems should Fork their own stream from it.</summary>
    public Rng Rng { get; }

    public long CurrentTick { get; private set; }

    public long ActionsResolved { get; private set; }

    /// <summary>Tiles of cover smashed or trampled so far.</summary>
    public int CoverChanges { get; private set; }

    public IReadOnlyList<Actor> Actors => _actors;

    public IReadOnlyList<LightSource> Lights => _lights;

    /// <summary>Noises from the last <see cref="NoiseMemoryTicks"/> ticks, oldest first.</summary>
    public IReadOnlyList<NoiseEvent> RecentNoises => _noises;

    /// <summary>Light everywhere outdoors before any lights are added: 100 is daylight, about 5 a stormy night.</summary>
    public int AmbientLightPercent
    {
        get => _ambientLight;
        set
        {
            _ambientLight = Math.Clamp(value, 0, 100);
            _lightGrid = null;
        }
    }

    /// <summary>Light level per tile; recomputed when lights, ambient light or cover change.</summary>
    public LightGrid Light => _lightGrid ??= LightGrid.Compute(Map, _lights, _ambientLight);

    public Actor AddActor(string name, char glyph, TilePos position, Species species, IActorController controller,
        Direction facing = Direction.South)
    {
        var actor = new Actor(_actors.Count, name, glyph, position, species, facing, controller)
        {
            NextActTick = CurrentTick,
        };
        foreach (var tile in CoreTiles(actor.Body))
        {
            if (!Map.InBounds(tile))
                throw new ArgumentOutOfRangeException(nameof(position), $"{name} at {position} would stick out of the map at {tile}.");
            if (ActorAt(tile) is { } other)
                throw new InvalidOperationException($"{name} at {position} would overlap {other.Name} at {tile}.");
        }

        _actors.Add(actor);
        Occupy(actor);
        _scheduler.Schedule(actor.Id, actor.NextActTick);
        return actor;
    }

    /// <summary>Swaps an actor's controller, e.g. to make the Rex hunt. Not recorded in replays.</summary>
    public void SetController(Actor actor, IActorController controller) => actor.Controller = controller;

    public void AddLight(LightSource light)
    {
        _lights.Add(light);
        _lightGrid = null;
    }

    public void EmitNoise(TilePos position, int loudness, string kind, int? actorId = null)
    {
        _noises.Add(new NoiseEvent(CurrentTick, position, loudness, kind, actorId));
        _noises.RemoveAll(n => n.Tick < CurrentTick - NoiseMemoryTicks);
    }

    /// <summary>The actor whose core covers this tile, if any. Heads and tails don't occupy tiles.</summary>
    public Actor? ActorAt(TilePos position)
    {
        if (!Map.InBounds(position))
            return null;
        int id = _occupant[position.Y * Map.Width + position.X] - 1;
        return id >= 0 ? _actors[id] : null;
    }

    /// <summary>Where an actor's eyes and ears are: its head tip for large bodies, clamped to the map.</summary>
    public TilePos EyeTile(Actor actor)
    {
        if (!actor.IsLarge)
            return actor.Position;
        var tip = actor.Species.Body.HeadTip(actor.Position, Compass16.Of(actor.Facing));
        return new TilePos(Math.Clamp(tip.X, 0, Map.Width - 1), Math.Clamp(tip.Y, 0, Map.Height - 1));
    }

    /// <summary>Why a move is impossible, or null if it's allowed. For large bodies a move that isn't straight ahead is a 45° turn.</summary>
    public string? MoveBlockReason(Actor actor, Direction direction)
    {
        if (!actor.IsLarge)
            return StepBlockReason(actor, actor.Position, direction, ignoreSmallActors: false);

        var (anchor, facing) = LargeMoveTarget(actor.Position, actor.Facing, direction);
        return BodyBlockReason(actor, anchor, facing, Terrain[actor.Position], ignoreSmallActors: false);
    }

    public bool CanMove(Actor actor, Direction direction) => MoveBlockReason(actor, direction) is null;

    /// <summary>Rules for a single-tile actor stepping from <paramref name="from"/>.</summary>
    public string? StepBlockReason(Actor actor, TilePos from, Direction direction, bool ignoreSmallActors)
    {
        var to = from.Offset(direction);
        if (!Map.InBounds(to))
            return "the edge of the map";

        var cover = Covers.Get(Cover[to]);
        if (cover.BlocksMovement)
            return $"{cover.Name} is in the way";

        int step = Terrain[to] - Terrain[from];
        if (step > actor.Movement.MaxStepUpDm)
            return $"too steep to climb ({Units.FormatMeters(step)})";
        if (-step > actor.Movement.MaxDropDm)
            return $"too far to drop ({Units.FormatMeters(-step)})";

        if (ActorAt(to) is { } other && other != actor && !(ignoreSmallActors && !other.IsLarge))
            return $"{other.Name} is in the way";

        return null;
    }

    /// <summary>
    /// Rules for a large body standing at <paramref name="anchor"/> facing <paramref name="facing"/>:
    /// every core tile must be on the map, passable or smashable, within climbing range of
    /// <paramref name="fromHeightDm"/>, and free of other actors' cores.
    /// </summary>
    public string? BodyBlockReason(Actor actor, TilePos anchor, Direction facing, int fromHeightDm, bool ignoreSmallActors)
    {
        foreach (var tile in CoreTiles(actor.Species.Body.Footprint(anchor, Compass16.Of(facing))))
        {
            if (!Map.InBounds(tile))
                return "the edge of the map";

            var cover = Covers.Get(Cover[tile]);
            if (cover.StopsLargeBody(actor.Species.Force))
                return $"{cover.Name} is in the way";

            int step = Terrain[tile] - fromHeightDm;
            if (step > actor.Movement.MaxStepUpDm)
                return $"too steep to climb ({Units.FormatMeters(step)})";
            if (-step > actor.Movement.MaxDropDm)
                return $"too far to drop ({Units.FormatMeters(-step)})";

            if (ActorAt(tile) is { } other && other != actor && !(ignoreSmallActors && !other.IsLarge))
                return $"{other.Name} is in the way";
        }
        return null;
    }

    /// <summary>How many core tiles of a body at this pose would smash cover.</summary>
    public int SmashCount(Actor actor, TilePos anchor, Direction facing)
    {
        int count = 0;
        foreach (var tile in CoreTiles(actor.Species.Body.Footprint(anchor, Compass16.Of(facing))))
        {
            if (Map.InBounds(tile) && Covers.Get(Cover[tile]).IsSmashedBy(actor.Species.Force))
                count++;
        }
        return count;
    }

    /// <summary>
    /// What a large body's move in <paramref name="direction"/> does: straight ahead steps forward;
    /// anything else turns 45° toward it (clockwise when directly behind).
    /// </summary>
    public static (TilePos Anchor, Direction Facing) LargeMoveTarget(TilePos anchor, Direction facing, Direction direction) =>
        direction == facing ? (anchor.Offset(direction), facing) : (anchor, RotateToward(facing, direction));

    public static Direction RotateToward(Direction from, Direction to)
    {
        int diff = ((int)to - (int)from + 8) % 8;
        if (diff == 0)
            return from;
        return (Direction)(((int)from + (diff <= 4 ? 1 : 7)) % 8);
    }

    /// <summary>
    /// Gives the next due actor one turn. If that actor is waiting for input, nothing changes
    /// and <see cref="StepResult.AwaitingInput"/> is returned.
    /// </summary>
    public StepResult Step()
    {
        if (!_scheduler.TryPeek(out int id, out long tick))
            return StepResult.Idle;

        CurrentTick = tick;
        var actor = _actors[id];
        var action = actor.Controller.Decide(actor, this);
        if (action is null)
            return StepResult.AwaitingInput;

        _scheduler.TryDequeue(out _, out _);
        int cost = Resolve(actor, action);
        actor.NextActTick = tick + cost;
        _scheduler.Schedule(id, actor.NextActTick);
        ActionsResolved++;
        return StepResult.Acted;
    }

    /// <summary>Runs every turn due at or before the given tick, stopping early if input is needed.</summary>
    public void RunUntil(long tick)
    {
        while (_scheduler.TryPeekTick(out long next) && next <= tick)
        {
            if (Step() == StepResult.AwaitingInput)
                return;
        }
        if (tick > CurrentTick)
            CurrentTick = tick;
    }

    /// <summary>Turn-based mode: runs everyone else until an actor needs input.</summary>
    public StepResult RunUntilInputNeeded(int maxActions = 100_000)
    {
        for (int i = 0; i < maxActions; i++)
        {
            var result = Step();
            if (result != StepResult.Acted)
                return result;
        }
        throw new InvalidOperationException($"No actor asked for input within {maxActions} actions.");
    }

    /// <summary>Fingerprint of the current state. Same seed and inputs must give the same hash.</summary>
    public ulong ComputeStateHash()
    {
        var hasher = new StateHasher().Add(CurrentTick).Add(CoverChanges);
        foreach (var actor in _actors)
        {
            hasher.Add(actor.Id)
                .Add(actor.Position.X)
                .Add(actor.Position.Y)
                .Add((int)actor.Facing)
                .Add(actor.LastMovedTick)
                .Add(actor.NextActTick);
        }
        return hasher.Value;
    }

    private static IEnumerable<TilePos> CoreTiles(IReadOnlyList<BodyTile> body)
    {
        foreach (var tile in body)
        {
            if (tile.Part == BodyPart.Core)
                yield return tile.Position;
        }
    }

    private void Occupy(Actor actor) => SetOccupancy(actor, actor.Id + 1);

    private void Vacate(Actor actor) => SetOccupancy(actor, 0);

    private void SetOccupancy(Actor actor, int value)
    {
        foreach (var tile in CoreTiles(actor.Body))
        {
            if (Map.InBounds(tile))
                _occupant[tile.Y * Map.Width + tile.X] = value;
        }
    }

    private void Place(Actor actor, TilePos anchor, Direction facing)
    {
        Vacate(actor);
        actor.Position = anchor;
        actor.Facing = facing;
        actor.Body = actor.Species.Body.Footprint(anchor, Compass16.Of(facing));
        Occupy(actor);
    }

    private int Resolve(Actor actor, ActorAction action) => action switch
    {
        WaitAction wait => Math.Max(1, wait.Ticks),
        MoveAction move => actor.IsLarge ? TryMoveLarge(actor, move.Direction) : TryMove(actor, move.Direction),
        _ => throw new ArgumentOutOfRangeException(nameof(action), $"Unknown action {action}."),
    };

    private int Bump(Actor actor, string reason)
    {
        EmitNoise(actor.Position, BumpLoudness, "bump", actor.Id);
        MoveBlocked?.Invoke(actor, reason);
        return BumpTicks;
    }

    private int TryMove(Actor actor, Direction direction)
    {
        actor.Facing = direction;

        string? blocked = StepBlockReason(actor, actor.Position, direction, ignoreSmallActors: false);
        if (blocked is not null)
            return Bump(actor, blocked);

        var to = actor.Position.Offset(direction);
        int step = Terrain.StepDm(actor.Position, to);
        var cover = Covers.Get(Cover[to]);
        Place(actor, to, direction);
        actor.LastMovedTick = CurrentTick;
        EmitNoise(to, actor.Species.FootstepLoudness + cover.NoiseModifier, "footsteps", actor.Id);

        int baseCost = actor.Movement.CostTicks(step, direction.IsDiagonal());
        return baseCost * cover.MoveCostPercent / 100;
    }

    private int TryMoveLarge(Actor actor, Direction direction)
    {
        var (anchor, facing) = LargeMoveTarget(actor.Position, actor.Facing, direction);
        bool turning = facing != actor.Facing;

        string? blocked = BodyBlockReason(actor, anchor, facing, Terrain[actor.Position], ignoreSmallActors: false);
        if (blocked is not null)
            return Bump(actor, blocked);

        int oldDir16 = Compass16.Of(actor.Facing);
        int heightStep = Terrain[anchor] - Terrain[actor.Position];
        Place(actor, anchor, facing);
        actor.LastMovedTick = CurrentTick;

        // The core smashes what it can and tramples the rest.
        int smashed = 0;
        foreach (var tile in CoreTiles(actor.Body))
        {
            var cover = Covers.Get(Cover[tile]);
            if (cover.IsSmashedBy(actor.Species.Force))
                smashed++;
            TryBreak(tile, actor.Species.Force);
        }

        // Head and tail flatten light growth and strike anyone in their way, including the
        // arc swept on the way round when turning.
        var swept = new List<TilePos>();
        foreach (var tile in actor.Body)
        {
            if (tile.Part != BodyPart.Core)
                swept.Add(tile.Position);
        }
        if (turning)
        {
            int midDir16 = oldDir16 + (Compass16.Of(facing) == (oldDir16 + 2) % 16 ? 1 : -1);
            foreach (var tile in actor.Species.Body.Footprint(anchor, midDir16))
            {
                if (tile.Part != BodyPart.Core)
                    swept.Add(tile.Position);
            }
        }

        var struck = new HashSet<int>();
        foreach (var tile in swept)
        {
            if (!Map.InBounds(tile))
                continue;
            var cover = Covers.Get(Cover[tile]);
            if (!cover.BlocksMovement && cover.BrokenInto is not null && cover.BreakStrength <= actor.Species.Force / 2)
                TryBreak(tile, actor.Species.Force);

            if (ActorAt(tile) is { } victim && victim != actor && struck.Add(victim.Id))
            {
                EmitNoise(tile, StrikeLoudness, "a heavy blow", actor.Id);
                ActorStruck?.Invoke(actor, victim);
            }
        }

        var anchorCover = Covers.Get(Cover[anchor]);
        EmitNoise(anchor, actor.Species.FootstepLoudness + anchorCover.NoiseModifier, "footsteps", actor.Id);
        if (smashed > 0)
            EmitNoise(anchor, SmashLoudness, "crashing", actor.Id);

        int cost = turning
            ? actor.Movement.TurnTicks
            : actor.Movement.CostTicks(heightStep, facing.IsDiagonal()) * anchorCover.MoveCostPercent / 100;
        return cost + smashed * SmashTicks;
    }

    private void TryBreak(TilePos tile, int force)
    {
        var cover = Covers.Get(Cover[tile]);
        if (cover.BrokenInto is not { } into || cover.BreakStrength > force)
            return;
        Cover[tile] = into;
        CoverChanges++;
        _lightGrid = null;
    }
}
