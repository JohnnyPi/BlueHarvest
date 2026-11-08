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

    /// <summary>Time to open or close a door.</summary>
    public const int DoorTicks = 60;

    /// <summary>Extra time for a large body to shove a vehicle as it steps.</summary>
    public const int ShoveTicks = 60;

    /// <summary>Force a body needs to shove a vehicle.</summary>
    public const int ShoveForce = 60;

    /// <summary>Integrity a vehicle loses each time it's shoved.</summary>
    public const int ShoveDamage = 10;

    /// <summary>Most a shoved vehicle can be pushed uphill, and dropped, per tile.</summary>
    public const int VehicleMaxStepUpDm = 5;
    public const int VehicleMaxDropDm = 20;

    /// <summary>Noises older than this are forgotten.</summary>
    public const int NoiseMemoryTicks = 300;

    public const int BumpLoudness = 10;
    public const int DoorLoudness = 15;
    public const int SmashLoudness = 40;
    public const int StrikeLoudness = 25;
    public const int JawsLoudness = 30;
    public const int BiteCoverLoudness = 35;
    public const int ShoveLoudness = 40;
    public const int BreachLoudness = 45;
    public const int ClimbLoudness = 12;
    public const int CollapseLoudness = 55;

    private readonly List<Actor> _actors = new();
    private readonly List<LightSource> _lights = new();
    private readonly List<NoiseEvent> _noises = new();
    private readonly List<Vehicle> _vehicles = new();
    private readonly List<Platform> _platforms = new();
    private readonly Scheduler _scheduler = new();
    private readonly int[] _occupant; // actor id + 1 for each core tile, 0 if empty
    private readonly int[] _vehicleAt; // vehicle id + 1 for each vehicle tile, 0 if none
    private readonly int[] _deckOccupant; // actor id + 1 standing on each tile's deck, 0 if empty
    private readonly int[] _platformAt; // platform id + 1 for each deck tile, 0 if none
    private LightGrid? _lightGrid;
    private int _ambientLight = 100;

    /// <summary>
    /// Starts a simulation on a map. Every connected patch of vehicle cover becomes a Vehicle and
    /// every connected patch of deck a Platform, so set those tiles before creating the simulation.
    /// </summary>
    public Simulation(WorldMap map, ulong seed)
    {
        Map = map;
        Rng = new Rng(seed);
        _occupant = new int[map.Width * map.Height];
        _vehicleAt = new int[map.Width * map.Height];
        _deckOccupant = new int[map.Width * map.Height];
        _platformAt = new int[map.Width * map.Height];
        FindVehicles();
        FindPlatforms();
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

    /// <summary>Raised for each actor caught in a bite: (biter, victim).</summary>
    public event Action<Actor, Actor>? ActorBitten;

    /// <summary>Raised after every bite, hit or miss.</summary>
    public event Action<Actor, BiteResult>? Bit;

    /// <summary>Raised when a vehicle loses integrity: (vehicle, by whom, how much).</summary>
    public event Action<Vehicle, Actor, int>? VehicleDamaged;

    /// <summary>Raised when a vehicle is shoved a tile: (vehicle, by whom).</summary>
    public event Action<Vehicle, Actor>? VehicleShoved;

    /// <summary>Raised when a platform comes down: (platform, who smashed its last support, who was on it).</summary>
    public event Action<Platform, Actor?, IReadOnlyList<Actor>>? PlatformCollapsed;

    /// <summary>Raised when an actor falls: (actor, height fallen in decimeters).</summary>
    public event Action<Actor, int>? ActorFell;

    public WorldMap Map { get; }

    public HeightMap Terrain => Map.Heights;

    public CoverMap Cover => Map.Cover;

    /// <summary>Root random stream. Systems should Fork their own stream from it.</summary>
    public Rng Rng { get; }

    public long CurrentTick { get; private set; }

    public long ActionsResolved { get; private set; }

    /// <summary>Tiles of cover smashed or trampled so far.</summary>
    public int CoverChanges { get; private set; }

    /// <summary>Doors opened, closed or locked so far.</summary>
    public int StructureChanges { get; private set; }

    public IReadOnlyList<Actor> Actors => _actors;

    public IReadOnlyList<LightSource> Lights => _lights;

    public IReadOnlyList<Vehicle> Vehicles => _vehicles;

    public IReadOnlyList<Platform> Platforms => _platforms;

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

    /// <summary>Light level per tile; recomputed when lights, ambient light, cover or doors change.</summary>
    public LightGrid Light => _lightGrid ??= LightGrid.Compute(Map, _lights, _ambientLight);

    /// <param name="onDeck">Start on the deck over <paramref name="position"/> (single-tile actors only).</param>
    public Actor AddActor(string name, char glyph, TilePos position, Species species, IActorController controller,
        Direction facing = Direction.South, bool onDeck = false)
    {
        var actor = new Actor(_actors.Count, name, glyph, position, species, facing, controller)
        {
            NextActTick = CurrentTick,
            OnDeck = onDeck,
        };
        if (onDeck && (actor.IsLarge || !Map.InBounds(position) || !Map.Structures.HasDeck(position)))
            throw new ArgumentException($"{name} can't start on a deck at {position}.", nameof(onDeck));
        foreach (var tile in CoreTiles(actor.Body))
        {
            if (!Map.InBounds(tile))
                throw new ArgumentOutOfRangeException(nameof(position), $"{name} at {position} would stick out of the map at {tile}.");
            if (ActorAt(tile, onDeck) is { } other)
                throw new InvalidOperationException($"{name} at {position} would overlap {other.Name} at {tile}.");
        }

        _actors.Add(actor);
        Occupy(actor);
        _scheduler.Schedule(actor.Id, actor.NextActTick);
        return actor;
    }

    /// <summary>Swaps an actor's controller, e.g. to make the Rex hunt. Not recorded in replays.</summary>
    public void SetController(Actor actor, IActorController controller) => actor.Controller = controller;

    /// <summary>Adds a light. One placed on a vehicle tile is mounted on that vehicle and moves with it.</summary>
    public void AddLight(LightSource light)
    {
        _lights.Add(light);
        if (Map.InBounds(light.Position) && VehicleAt(light.Position) is { } vehicle)
            vehicle.LightIndices.Add(_lights.Count - 1);
        _lightGrid = null;
    }

    public void EmitNoise(TilePos position, int loudness, string kind, int? actorId = null)
    {
        _noises.Add(new NoiseEvent(CurrentTick, position, loudness, kind, actorId));
        _noises.RemoveAll(n => n.Tick < CurrentTick - NoiseMemoryTicks);
    }

    /// <summary>
    /// The actor on the ground at this tile, if any: a single-tile actor, or a large body whose core
    /// covers it. Heads and tails don't occupy tiles. Actors on a deck above are found with ActorOnDeck.
    /// </summary>
    public Actor? ActorAt(TilePos position) => ActorAt(position, onDeck: false);

    /// <summary>The actor standing on the deck over this tile, if any.</summary>
    public Actor? ActorOnDeck(TilePos position) => ActorAt(position, onDeck: true);

    public Actor? ActorAt(TilePos position, bool onDeck)
    {
        if (!Map.InBounds(position))
            return null;
        int id = (onDeck ? _deckOccupant : _occupant)[position.Y * Map.Width + position.X] - 1;
        return id >= 0 ? _actors[id] : null;
    }

    /// <summary>The platform whose deck covers this tile, if any (collapsed ones included).</summary>
    public Platform? PlatformAt(TilePos position)
    {
        if (!Map.InBounds(position))
            return null;
        int id = _platformAt[position.Y * Map.Width + position.X] - 1;
        return id >= 0 ? _platforms[id] : null;
    }

    /// <summary>The support posts still standing under a platform.</summary>
    public IEnumerable<TilePos> SupportsOf(Platform platform) =>
        platform.Tiles.Where(t => Cover[t] == CoverType.Support);

    /// <summary>Absolute height an actor stands at: its deck, or the ground under its anchor.</summary>
    public int StandHeight(Actor actor) => Map.SurfaceHeight(actor.Position, actor.OnDeck);

    /// <summary>Absolute height of an actor's eyes (and ears).</summary>
    public int EyeZ(Actor actor) =>
        (actor.IsLarge ? Terrain[EyeTile(actor)] : StandHeight(actor)) + actor.Species.Senses.EyeHeightDm;

    /// <summary>The vehicle covering this tile, if any.</summary>
    public Vehicle? VehicleAt(TilePos position)
    {
        if (!Map.InBounds(position))
            return null;
        int id = _vehicleAt[position.Y * Map.Width + position.X] - 1;
        return id >= 0 ? _vehicles[id] : null;
    }

    /// <summary>Sets a door's state directly (setup, editor, debug). Actors use UseDoorAction.</summary>
    public void SetDoor(TilePos position, DoorState state)
    {
        if (Cover[position] != CoverType.Door)
            throw new InvalidOperationException($"There's no doorway at {position}.");
        if (Map.Structures.DoorAt(position) == state)
            return;
        Map.Structures.SetDoor(position, state);
        StructureChanges++;
        _lightGrid = null;
    }

    /// <summary>Sets a vehicle's integrity directly (setup, debug), 0-100. 0 means breached.</summary>
    public void SetVehicleIntegrity(Vehicle vehicle, int integrity) =>
        vehicle.Integrity = Math.Clamp(integrity, 0, Vehicle.MaxIntegrity);

    /// <summary>Where an actor's eyes and ears are: its head tip for large bodies, clamped to the map.</summary>
    public TilePos EyeTile(Actor actor)
    {
        if (!actor.IsLarge)
            return actor.Position;
        var tip = actor.Species.Body.HeadTip(actor.Position, Compass16.Of(actor.Facing));
        return new TilePos(Math.Clamp(tip.X, 0, Map.Width - 1), Math.Clamp(tip.Y, 0, Map.Height - 1));
    }

    /// <summary>
    /// Whether an actor can reach a surface at absolute height <paramref name="surfaceDm"/> on a tile
    /// with its jaws (or hands): within its reach up and down from where it stands, and not across a
    /// roof line (a head outdoors can't reach under a roof, and the reverse).
    /// </summary>
    public bool CanReach(Actor actor, TilePos tile, int surfaceDm)
    {
        int rise = surfaceDm - StandHeight(actor);
        return rise <= actor.Species.ReachUpDm
            && -rise <= actor.Species.ReachDownDm
            && Map.IsIndoor(tile) == Map.IsIndoor(actor.IsLarge ? EyeTile(actor) : actor.Position);
    }

    /// <summary>Whether an actor can reach a tile's ground or, if it has one, its deck.</summary>
    public bool CanReachAnySurface(Actor actor, TilePos tile) =>
        CanReach(actor, tile, Terrain[tile])
        || (Map.Structures.HasDeck(tile) && CanReach(actor, tile, Map.DeckTop(tile)));

    /// <summary>
    /// Tiles an actor's jaws reach: every tile next to (or on) its head and neck, except its own core,
    /// where its ground or deck is within its reach height. Small actors reach the eight tiles around them.
    /// </summary>
    public IReadOnlyList<TilePos> BiteZone(Actor actor)
    {
        var zone = new List<TilePos>();

        if (!actor.IsLarge)
        {
            foreach (var direction in Directions.All)
            {
                var p = actor.Position.Offset(direction);
                if (Map.InBounds(p) && CanReachAnySurface(actor, p))
                    zone.Add(p);
            }
            return zone;
        }

        var core = new HashSet<TilePos>(CoreTiles(actor.Body));
        var seen = new HashSet<TilePos>();
        foreach (var tile in actor.Body)
        {
            if (tile.Part != BodyPart.Head)
                continue;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    var p = new TilePos(tile.Position.X + dx, tile.Position.Y + dy);
                    if (!Map.InBounds(p) || core.Contains(p) || !seen.Add(p))
                        continue;
                    if (CanReachAnySurface(actor, p))
                        zone.Add(p);
                }
            }
        }
        return zone;
    }

    /// <summary>
    /// Why a move is impossible, or null if it's allowed. For large bodies a move that isn't straight
    /// ahead is a 45° turn; with <paramref name="shove"/>, a step blocked only by a vehicle pushes it
    /// if it can move.
    /// </summary>
    public string? MoveBlockReason(Actor actor, Direction direction, bool shove = false)
    {
        if (!actor.IsLarge)
            return SmallStep(actor, actor.Position, actor.OnDeck, direction, ignoreSmallActors: false).Blocked;
        return PlanLargeMove(actor, direction, shove).Blocked;
    }

    public bool CanMove(Actor actor, Direction direction, bool shove = false) => MoveBlockReason(actor, direction, shove) is null;

    /// <summary>
    /// Rules for a single-tile actor stepping from <paramref name="from"/> (on its deck if that's where
    /// the actor stands now, else on the ground).
    /// </summary>
    public string? StepBlockReason(Actor actor, TilePos from, Direction direction, bool ignoreSmallActors) =>
        SmallStep(actor, from, from == actor.Position && actor.OnDeck, direction, ignoreSmallActors).Blocked;

    /// <summary>
    /// Where a single-tile actor's step lands and whether it can make it. The next tile may offer two
    /// surfaces: its ground and its deck. Each must be within the actor's step up and safe drop from
    /// where it stands; the ground also has to be passable and have headroom under any deck. When both
    /// work it takes the one nearer its current height (so stairs lead up, and walking under a high
    /// catwalk stays on the ground). A closed (not locked) door doesn't block actors that open doors:
    /// walking into it opens it.
    /// </summary>
    public (string? Blocked, bool ToDeck) SmallStep(Actor actor, TilePos from, bool fromDeck, Direction direction,
        bool ignoreSmallActors)
    {
        var to = from.Offset(direction);
        if (!Map.InBounds(to))
            return ("the edge of the map", false);

        int fromZ = Map.SurfaceHeight(from, fromDeck);
        string? groundReason = GroundStepReason(actor, fromZ, to, ignoreSmallActors);
        if (!Map.Structures.HasDeck(to))
            return (groundReason, false);

        int deckZ = Map.DeckTop(to);
        string? deckReason = HeightStepReason(actor, deckZ - fromZ)
            ?? OccupiedReason(actor, ActorOnDeck(to), ignoreSmallActors);

        if (deckReason is null && groundReason is null)
            return (null, Math.Abs(deckZ - fromZ) <= Math.Abs(Terrain[to] - fromZ));
        if (deckReason is null)
            return (null, true);
        if (groundReason is null)
            return (null, false);
        return Math.Abs(deckZ - fromZ) <= Math.Abs(Terrain[to] - fromZ) ? (deckReason, true) : (groundReason, false);
    }

    private string? GroundStepReason(Actor actor, int fromZ, TilePos to, bool ignoreSmallActors)
    {
        var cover = Map.CoverAt(to);
        if (cover.BlocksMovement && !(IsClosedDoor(to) && actor.Species.OpensDoors))
            return $"{cover.Name} is in the way";

        if (Map.Structures.HasDeck(to) && Map.Structures.DeckDm(to) < actor.Species.BodyHeightDm)
            return "a platform is in the way";

        return HeightStepReason(actor, Terrain[to] - fromZ)
            ?? OccupiedReason(actor, ActorAt(to), ignoreSmallActors);
    }

    private static string? HeightStepReason(Actor actor, int step)
    {
        if (step > actor.Movement.MaxStepUpDm)
            return $"too steep to climb ({Units.FormatMeters(step)})";
        if (-step > actor.Movement.MaxDropDm)
            return $"too far to drop ({Units.FormatMeters(-step)})";
        return null;
    }

    private static string? OccupiedReason(Actor actor, Actor? other, bool ignoreSmallActors) =>
        other is not null && other != actor && !(ignoreSmallActors && !other.IsLarge)
            ? $"{other.Name} is in the way"
            : null;

    /// <summary>A doorway whose door is shut but not locked.</summary>
    public bool IsClosedDoor(TilePos p) =>
        Cover[p] == CoverType.Door && Map.Structures.DoorAt(p) == DoorState.Closed;

    /// <summary>
    /// Rules for a large body standing at <paramref name="anchor"/> facing <paramref name="facing"/>:
    /// every core tile must be on the map, passable or smashable, within climbing range of
    /// <paramref name="fromHeightDm"/>, and free of other actors' cores. Tiles of
    /// <paramref name="shoving"/> are checked for height only: that vehicle is being pushed out of the way.
    /// </summary>
    public string? BodyBlockReason(Actor actor, TilePos anchor, Direction facing, int fromHeightDm, bool ignoreSmallActors,
        Vehicle? shoving = null)
    {
        foreach (var tile in CoreTiles(actor.Species.Body.Footprint(anchor, Compass16.Of(facing))))
        {
            if (!Map.InBounds(tile))
                return "the edge of the map";

            bool movingAway = shoving is not null && VehicleAt(tile) == shoving;
            var cover = Map.CoverAt(tile);
            if (!movingAway && cover.StopsLargeBody(actor.Species.Force))
                return $"{cover.Name} is in the way";

            if (Map.Structures.HasDeck(tile) && Map.Structures.DeckDm(tile) < actor.Species.BodyHeightDm)
                return "a platform is in the way";

            int step = Terrain[tile] - fromHeightDm;
            if (step > actor.Movement.MaxStepUpDm)
                return $"too steep to climb ({Units.FormatMeters(step)})";
            if (-step > actor.Movement.MaxDropDm)
                return $"too far to drop ({Units.FormatMeters(-step)})";

            if (!movingAway && ActorAt(tile) is { } other && other != actor && !(ignoreSmallActors && !other.IsLarge))
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
            if (Map.InBounds(tile) && Map.CoverAt(tile).IsSmashedBy(actor.Species.Force))
                count++;
        }
        return count;
    }

    /// <summary>
    /// Why a vehicle can't be shoved one tile by (dx, dy), or null if it can: every tile it would move
    /// onto must be on the map, free of obstacles, other vehicles and actors (its occupants move with
    /// it), and no more than a small step up or a 2 m drop.
    /// </summary>
    public string? ShoveBlockReason(Vehicle vehicle, int dx, int dy)
    {
        foreach (var tile in vehicle.Tiles)
        {
            var dest = new TilePos(tile.X + dx, tile.Y + dy);
            if (!Map.InBounds(dest))
                return "the edge of the map is behind it";
            var other = VehicleAt(dest);
            if (other == vehicle)
                continue;
            if (other is not null)
                return $"{other.Name} is behind it";

            var cover = Map.CoverAt(dest);
            if (cover.BlocksMovement || cover.BlocksLargeBodies)
                return $"{cover.Name} is behind it";

            int step = Terrain[dest] - Terrain[tile];
            if (step > VehicleMaxStepUpDm)
                return "the ground rises behind it";
            if (-step > VehicleMaxDropDm)
                return "there's a drop behind it";

            if (ActorAt(dest) is { } actor)
                return $"{actor.Name} is behind it";
        }
        return null;
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
        var hasher = new StateHasher().Add(CurrentTick).Add(CoverChanges).Add(StructureChanges);
        foreach (var actor in _actors)
        {
            hasher.Add(actor.Id)
                .Add(actor.Position.X)
                .Add(actor.Position.Y)
                .Add(actor.OnDeck ? 1 : 0)
                .Add((int)actor.Facing)
                .Add(actor.LastMovedTick)
                .Add(actor.NextActTick);
        }
        foreach (var vehicle in _vehicles)
        {
            hasher.Add(vehicle.Anchor.X)
                .Add(vehicle.Anchor.Y)
                .Add(vehicle.Integrity);
        }
        foreach (var platform in _platforms)
            hasher.Add(platform.Collapsed ? 1 : 0);
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
        var grid = actor.OnDeck ? _deckOccupant : _occupant;
        foreach (var tile in CoreTiles(actor.Body))
        {
            if (Map.InBounds(tile))
                grid[tile.Y * Map.Width + tile.X] = value;
        }
    }

    private void Place(Actor actor, TilePos anchor, Direction facing) => Place(actor, anchor, facing, actor.OnDeck);

    private void Place(Actor actor, TilePos anchor, Direction facing, bool onDeck)
    {
        Vacate(actor);
        actor.Position = anchor;
        actor.Facing = facing;
        actor.OnDeck = onDeck;
        actor.Body = actor.Species.Body.Footprint(anchor, Compass16.Of(facing));
        Occupy(actor);
    }

    private int Resolve(Actor actor, ActorAction action) => action switch
    {
        WaitAction wait => Math.Max(1, wait.Ticks),
        MoveAction move => actor.IsLarge ? TryMoveLarge(actor, move.Direction, move.Shove) : TryMove(actor, move.Direction),
        BiteAction bite => ResolveBite(actor, bite.Investigative),
        UseDoorAction door => UseDoor(actor, door.Direction),
        ClimbAction => Climb(actor),
        _ => throw new ArgumentOutOfRangeException(nameof(action), $"Unknown action {action}."),
    };

    private int Bump(Actor actor, string reason)
    {
        EmitNoise(actor.Position, BumpLoudness, NoiseKinds.Bump, actor.Id);
        MoveBlocked?.Invoke(actor, reason);
        return BumpTicks;
    }

    private int TryMove(Actor actor, Direction direction)
    {
        actor.Facing = direction;

        var (blocked, toDeck) = SmallStep(actor, actor.Position, actor.OnDeck, direction, ignoreSmallActors: false);
        if (blocked is not null)
            return Bump(actor, blocked);

        var to = actor.Position.Offset(direction);
        if (!toDeck && IsClosedDoor(to))
            return SwingDoor(actor, to, DoorState.Open);

        int step = Map.SurfaceHeight(to, toDeck) - StandHeight(actor);
        var cover = toDeck ? Covers.Deck : Map.CoverAt(to);
        Place(actor, to, direction, toDeck);
        actor.LastMovedTick = CurrentTick;
        EmitNoise(to, actor.Species.FootstepLoudness + cover.NoiseModifier, NoiseKinds.Footsteps, actor.Id);

        int baseCost = actor.Movement.CostTicks(step, direction.IsDiagonal());
        return baseCost * cover.MoveCostPercent / 100;
    }

    /// <summary>Climbs the ladder on the actor's tile, up to the deck or down to the ground.</summary>
    private int Climb(Actor actor)
    {
        var p = actor.Position;
        if (!actor.Species.ClimbsLadders || actor.IsLarge)
            return Bump(actor, "it can't climb");
        if (!Map.Structures.HasLadder(p) || !Map.Structures.HasDeck(p))
            return Bump(actor, "there's no ladder here");

        bool up = !actor.OnDeck;
        if (ActorAt(p, onDeck: up) is { } inTheWay)
            return Bump(actor, $"{inTheWay.Name} is in the way {(up ? "at the top" : "at the bottom")}");
        if (!up && Map.CoverAt(p).BlocksMovement)
            return Bump(actor, $"{Map.CoverAt(p).Name} is at the bottom");

        Place(actor, p, actor.Facing, up);
        actor.LastMovedTick = CurrentTick;
        EmitNoise(p, ClimbLoudness, NoiseKinds.Climbing, actor.Id);
        return Math.Max(1, Map.Structures.DeckDm(p) * actor.Movement.ClimbTicksPerDm);
    }

    private int UseDoor(Actor actor, Direction direction)
    {
        actor.Facing = direction;
        var tile = actor.Position.Offset(direction);
        if (!actor.Species.OpensDoors)
            return Bump(actor, "it can't work doors");
        if (!Map.InBounds(tile) || Cover[tile] != CoverType.Door)
            return Bump(actor, "there's no door there");

        switch (Map.Structures.DoorAt(tile))
        {
            case DoorState.Open:
                if (ActorAt(tile) is { } inTheWay)
                    return Bump(actor, $"{inTheWay.Name} is in the doorway");
                return SwingDoor(actor, tile, DoorState.Closed);
            case DoorState.Closed:
                return SwingDoor(actor, tile, DoorState.Open);
            default:
                return Bump(actor, "the door is locked");
        }
    }

    /// <summary>Opens or shuts a door. Working a door is motion as far as eyes are concerned.</summary>
    private int SwingDoor(Actor actor, TilePos tile, DoorState state)
    {
        SetDoor(tile, state);
        actor.LastMovedTick = CurrentTick;
        EmitNoise(tile, DoorLoudness, NoiseKinds.Door, actor.Id);
        return DoorTicks;
    }

    /// <summary>
    /// Where a large body's move goes, and whether it's blocked. With <paramref name="allowShove"/>, a
    /// straight step blocked only by one vehicle shoves that vehicle, if the body is strong enough and
    /// the vehicle has room to move.
    /// </summary>
    private (TilePos Anchor, Direction Facing, string? Blocked, Vehicle? Shove) PlanLargeMove(Actor actor, Direction direction,
        bool allowShove)
    {
        var (anchor, facing) = LargeMoveTarget(actor.Position, actor.Facing, direction);
        int fromHeight = Terrain[actor.Position];
        string? blocked = BodyBlockReason(actor, anchor, facing, fromHeight, ignoreSmallActors: false);
        if (blocked is null)
            return (anchor, facing, null, null);
        if (!allowShove || facing != actor.Facing || actor.Species.Force < ShoveForce)
            return (anchor, facing, blocked, null);

        // Is a single vehicle all that's in the way?
        Vehicle? vehicle = null;
        foreach (var tile in CoreTiles(actor.Species.Body.Footprint(anchor, Compass16.Of(facing))))
        {
            if (VehicleAt(tile) is not { } v)
                continue;
            if (vehicle is not null && v != vehicle)
                return (anchor, facing, blocked, null);
            vehicle = v;
        }
        if (vehicle is null || BodyBlockReason(actor, anchor, facing, fromHeight, ignoreSmallActors: false, vehicle) is not null)
            return (anchor, facing, blocked, null);

        var (dx, dy) = facing.Delta();
        string? stuck = ShoveBlockReason(vehicle, dx, dy);
        return stuck is null
            ? (anchor, facing, null, vehicle)
            : (anchor, facing, $"{vehicle.Name} won't budge: {stuck}", null);
    }

    private int TryMoveLarge(Actor actor, Direction direction, bool allowShove)
    {
        var (anchor, facing, blocked, shove) = PlanLargeMove(actor, direction, allowShove);
        bool turning = facing != actor.Facing;
        if (blocked is not null)
            return Bump(actor, blocked);

        int extra = 0;
        if (shove is not null)
        {
            var (dx, dy) = facing.Delta();
            ShoveVehicle(shove, dx, dy, actor);
            extra += ShoveTicks;
        }

        int oldDir16 = Compass16.Of(actor.Facing);
        int heightStep = Terrain[anchor] - Terrain[actor.Position];
        Place(actor, anchor, facing);
        actor.LastMovedTick = CurrentTick;

        // The core smashes what it can and tramples the rest.
        int smashed = 0;
        foreach (var tile in CoreTiles(actor.Body))
        {
            if (Map.CoverAt(tile).IsSmashedBy(actor.Species.Force))
                smashed++;
            TryBreak(tile, actor.Species.Force, actor);
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
            var cover = Map.CoverAt(tile);
            if (!cover.BlocksMovement && cover.BrokenInto is not null && cover.BreakStrength <= actor.Species.Force / 2)
                TryBreak(tile, actor.Species.Force, actor);

            if (ActorAt(tile) is { } victim && victim != actor && struck.Add(victim.Id))
            {
                EmitNoise(tile, StrikeLoudness, NoiseKinds.HeavyBlow, actor.Id);
                ActorStruck?.Invoke(actor, victim);
                CryOut(victim);
            }
        }

        var anchorCover = Map.CoverAt(anchor);
        EmitNoise(anchor, actor.Species.FootstepLoudness + anchorCover.NoiseModifier, NoiseKinds.Footsteps, actor.Id);
        if (smashed > 0)
            EmitNoise(anchor, SmashLoudness, NoiseKinds.Crashing, actor.Id);

        int cost = turning
            ? actor.Movement.TurnTicks
            : actor.Movement.CostTicks(heightStep, facing.IsDiagonal()) * anchorCover.MoveCostPercent / 100;
        return cost + smashed * SmashTicks + extra;
    }

    /// <summary>
    /// Moves a vehicle one tile, taking its occupants and mounted lights along, and puts back the
    /// ground cover it was sitting on. Assumes ShoveBlockReason said yes.
    /// </summary>
    private void ShoveVehicle(Vehicle vehicle, int dx, int dy, Actor by)
    {
        var oldTiles = vehicle.Tiles.ToList();
        var oldSet = new HashSet<TilePos>(oldTiles);
        var occupants = _actors.Where(a => !a.IsLarge && !a.OnDeck && oldSet.Contains(a.Position)).ToList();
        foreach (var occupant in occupants)
            Vacate(occupant);

        var newUnder = new Dictionary<TilePos, CoverType>();
        foreach (var tile in oldTiles)
        {
            var dest = new TilePos(tile.X + dx, tile.Y + dy);
            newUnder[dest] = oldSet.Contains(dest) ? vehicle.Under[dest] : Cover[dest];
        }
        foreach (var tile in oldTiles)
        {
            Cover[tile] = vehicle.Under[tile];
            _vehicleAt[tile.Y * Map.Width + tile.X] = 0;
        }

        vehicle.MoveBy(dx, dy, newUnder);
        foreach (var tile in vehicle.Tiles)
        {
            Cover[tile] = CoverType.Vehicle;
            _vehicleAt[tile.Y * Map.Width + tile.X] = vehicle.Id + 1;
        }

        foreach (var occupant in occupants)
        {
            occupant.Position = new TilePos(occupant.Position.X + dx, occupant.Position.Y + dy);
            occupant.Body = occupant.Species.Body.Footprint(occupant.Position, Compass16.Of(occupant.Facing));
            Occupy(occupant);
        }

        foreach (int index in vehicle.LightIndices)
        {
            var light = _lights[index];
            _lights[index] = light with { Position = new TilePos(light.Position.X + dx, light.Position.Y + dy) };
        }

        _lightGrid = null;
        EmitNoise(vehicle.Anchor, ShoveLoudness, NoiseKinds.MetalScrape, by.Id);
        VehicleShoved?.Invoke(vehicle, by);
        DamageVehicle(vehicle, ShoveDamage, by);
    }

    private void DamageVehicle(Vehicle vehicle, int amount, Actor by)
    {
        if (amount <= 0 || vehicle.Breached)
            return;
        vehicle.Integrity = Math.Max(0, vehicle.Integrity - amount);
        VehicleDamaged?.Invoke(vehicle, by, amount);
        if (vehicle.Breached)
            EmitNoise(vehicle.Anchor, BreachLoudness, NoiseKinds.MetalTear, by.Id);
    }

    /// <summary>
    /// Closes the jaws on the bite zone. Actors there are hit (and cry out if their species does),
    /// except those inside a vehicle that hasn't been breached yet. Every vehicle in the zone loses
    /// integrity. The result is kept on the biter for its AI to read.
    /// </summary>
    private int ResolveBite(Actor actor, bool investigative)
    {
        var zone = BiteZone(actor);
        var hits = new List<BiteHit>();
        var coverBitten = new List<TilePos>();
        var vehiclesBitten = new List<Vehicle>();
        var hitIds = new HashSet<int>();
        string? coverSound = null;

        foreach (var tile in zone)
        {
            var vehicle = VehicleAt(tile);
            if (vehicle is not null && !vehiclesBitten.Contains(vehicle))
                vehiclesBitten.Add(vehicle);

            // On the ground: anyone not shut inside an unbreached vehicle. On a deck: anyone in reach.
            if (ActorAt(tile) is { } victim && victim != actor && hitIds.Add(victim.Id)
                && vehicle is not { Breached: false } && CanReach(actor, tile, Terrain[tile]))
                hits.Add(new BiteHit(victim, tile));
            if (ActorOnDeck(tile) is { } high && high != actor && CanReach(actor, tile, Map.DeckTop(tile))
                && hitIds.Add(high.Id))
                hits.Add(new BiteHit(high, tile));

            if (Map.CoverAt(tile).BiteSound is { } sound)
            {
                coverBitten.Add(tile);
                coverSound ??= sound;
            }
        }

        EmitNoise(EyeTile(actor), JawsLoudness, NoiseKinds.Jaws, actor.Id);
        if (coverSound is not null)
            EmitNoise(coverBitten[0], BiteCoverLoudness, coverSound, actor.Id);

        foreach (var hit in hits)
        {
            ActorBitten?.Invoke(actor, hit.Victim);
            CryOut(hit.Victim);
        }
        foreach (var vehicle in vehiclesBitten)
            DamageVehicle(vehicle, actor.Species.BiteDamage, actor);

        var result = new BiteResult(CurrentTick, investigative, zone, hits, coverBitten, vehiclesBitten);
        actor.LastBite = result;
        Bit?.Invoke(actor, result);
        return Math.Max(1, actor.Movement.BiteTicks);
    }

    private void CryOut(Actor victim)
    {
        if (victim.Species.ScreamLoudness > 0)
            EmitNoise(victim.Position, victim.Species.ScreamLoudness, NoiseKinds.Scream, victim.Id);
    }

    private void TryBreak(TilePos tile, int force, Actor? by)
    {
        var type = Cover[tile];
        var cover = Covers.Get(type);
        if (cover.BrokenInto is not { } into || cover.BreakStrength > force)
            return;
        Cover[tile] = into;
        CoverChanges++;
        _lightGrid = null;
        if (type == CoverType.Support && PlatformAt(tile) is { Collapsed: false } platform)
            CheckCollapse(platform, by);
    }

    /// <summary>A platform with fewer than half its support posts left standing comes down.</summary>
    private void CheckCollapse(Platform platform, Actor? by)
    {
        if (platform.InitialSupports == 0)
            return;
        int standing = SupportsOf(platform).Count();
        if (standing * 2 < platform.InitialSupports)
            Collapse(platform, by);
    }

    /// <summary>
    /// Brings a platform down: its deck and ladders are gone, the ground under it is strewn with
    /// rubble (walls and the like stay), and everyone on it falls to the nearest open ground and cries out.
    /// </summary>
    private void Collapse(Platform platform, Actor? by)
    {
        platform.Collapsed = true;
        var riders = new List<(Actor Actor, int FromDm)>();
        foreach (var tile in platform.Tiles)
        {
            if (ActorOnDeck(tile) is { } rider)
                riders.Add((rider, Map.DeckTop(tile)));
        }

        foreach (var (rider, _) in riders)
            Vacate(rider);
        foreach (var tile in platform.Tiles)
        {
            Map.Structures.SetDeck(tile, 0);
            Map.Structures.SetLadder(tile, false);
            var cover = Covers.Get(Cover[tile]);
            if (!cover.BlocksMovement && !cover.BlocksLargeBodies && Cover[tile] != CoverType.Rubble)
            {
                Cover[tile] = CoverType.Rubble;
                CoverChanges++;
            }
        }

        var fell = new List<Actor>();
        foreach (var (rider, fromDm) in riders)
        {
            rider.OnDeck = false;
            rider.Position = LandingSpot(rider.Position);
            rider.Body = rider.Species.Body.Footprint(rider.Position, Compass16.Of(rider.Facing));
            Occupy(rider);
            fell.Add(rider);
            ActorFell?.Invoke(rider, Math.Max(0, fromDm - Terrain[rider.Position]));
            CryOut(rider);
        }

        StructureChanges++;
        _lightGrid = null;
        EmitNoise(platform.Tiles[platform.Tiles.Count / 2], CollapseLoudness, NoiseKinds.Collapse, by?.Id);
        PlatformCollapsed?.Invoke(platform, by, fell);
    }

    /// <summary>The nearest tile (ring by ring, in reading order) whose ground is passable and free.</summary>
    private TilePos LandingSpot(TilePos from)
    {
        int maxRadius = Math.Max(Map.Width, Map.Height);
        for (int r = 0; r <= maxRadius; r++)
        {
            for (int y = from.Y - r; y <= from.Y + r; y++)
            {
                for (int x = from.X - r; x <= from.X + r; x++)
                {
                    if (Math.Max(Math.Abs(x - from.X), Math.Abs(y - from.Y)) != r)
                        continue;
                    var p = new TilePos(x, y);
                    if (Map.InBounds(p) && !Map.CoverAt(p).BlocksMovement && ActorAt(p) is null)
                        return p;
                }
            }
        }
        return from;
    }

    /// <summary>
    /// Turns each 4-connected patch of vehicle cover into a Vehicle, in reading order. The cover under
    /// it is guessed from its neighbours (usually the track).
    /// </summary>
    private void FindVehicles()
    {
        int width = Map.Width, height = Map.Height;
        var seen = new bool[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var start = new TilePos(x, y);
                if (seen[y * width + x] || Cover[start] != CoverType.Vehicle)
                    continue;

                var tiles = new List<TilePos>();
                var queue = new Queue<TilePos>();
                queue.Enqueue(start);
                seen[y * width + x] = true;
                while (queue.Count > 0)
                {
                    var p = queue.Dequeue();
                    tiles.Add(p);
                    foreach (var d in Directions.Cardinal)
                    {
                        var n = p.Offset(d);
                        if (Map.InBounds(n) && !seen[n.Y * width + n.X] && Cover[n] == CoverType.Vehicle)
                        {
                            seen[n.Y * width + n.X] = true;
                            queue.Enqueue(n);
                        }
                    }
                }

                var under = GuessUnder(tiles);
                var vehicle = new Vehicle(_vehicles.Count, $"vehicle {_vehicles.Count + 1}", tiles,
                    tiles.ToDictionary(t => t, _ => under));
                _vehicles.Add(vehicle);
                foreach (var t in tiles)
                    _vehicleAt[t.Y * width + t.X] = vehicle.Id + 1;
            }
        }
    }

    /// <summary>Turns each 4-connected patch of deck into a Platform, counting the support posts under it.</summary>
    private void FindPlatforms()
    {
        int width = Map.Width, height = Map.Height;
        var seen = new bool[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var start = new TilePos(x, y);
                if (seen[y * width + x] || !Map.Structures.HasDeck(start))
                    continue;

                var tiles = new List<TilePos>();
                var queue = new Queue<TilePos>();
                queue.Enqueue(start);
                seen[y * width + x] = true;
                while (queue.Count > 0)
                {
                    var p = queue.Dequeue();
                    tiles.Add(p);
                    foreach (var d in Directions.Cardinal)
                    {
                        var n = p.Offset(d);
                        if (Map.InBounds(n) && !seen[n.Y * width + n.X] && Map.Structures.HasDeck(n))
                        {
                            seen[n.Y * width + n.X] = true;
                            queue.Enqueue(n);
                        }
                    }
                }

                int supports = tiles.Count(t => Cover[t] == CoverType.Support);
                var platform = new Platform(_platforms.Count, tiles, supports);
                _platforms.Add(platform);
                foreach (var t in tiles)
                    _platformAt[t.Y * width + t.X] = platform.Id + 1;
            }
        }
    }

    /// <summary>The most common cover next to a patch of tiles (ties go to the lower CoverType); grass if none.</summary>
    private CoverType GuessUnder(List<TilePos> tiles)
    {
        var set = new HashSet<TilePos>(tiles);
        var counts = new int[Covers.All.Count];
        foreach (var p in tiles)
        {
            foreach (var d in Directions.Cardinal)
            {
                var n = p.Offset(d);
                if (Map.InBounds(n) && !set.Contains(n) && Cover[n] != CoverType.Vehicle)
                    counts[(int)Cover[n]]++;
            }
        }

        int best = 0;
        for (int i = 1; i < counts.Length; i++)
        {
            if (counts[i] > counts[best])
                best = i;
        }
        return counts[best] > 0 ? (CoverType)best : CoverType.Grass;
    }
}
