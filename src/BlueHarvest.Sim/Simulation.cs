using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.Injuries;
using BlueHarvest.Sim.Lighting;
using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.Time;
using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim;

/// <summary>
/// The whole game state and its rules. Contains no Godot code, so it runs in tests,
/// the headless runner and the game alike.
/// </summary>
public sealed partial class Simulation
{
    /// <summary>Time lost walking into something you can't pass.</summary>
    public const int BumpTicks = 10;

    /// <summary>How long a scream takes.</summary>
    public const int ScreamTicks = 60;

    /// <summary>How long giving an order takes.</summary>
    public const int OrderTicks = 60;

    /// <summary>How far (tiles) an order carries.</summary>
    public const int OrderRange = 8;

    /// <summary>How loud an order is, at the speaker.</summary>
    public const int TalkLoudness = 14;

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
    private readonly List<BloodSpot> _blood = new();
    private readonly List<ScentMark> _scent = new();
    private readonly List<VibrationEvent> _vibrations = new();
    private long _weatherTick = -1;
    private int _lightGridAmbient;
    private long _lightGridTick = long.MinValue;
    private readonly Rng _injuryRng;
    private readonly Rng _gearRng;
    private readonly List<(int Light, long OffTick)> _lightExpiry = new();
    private readonly List<RopeSpot> _ropes = new();
    private bool _playerDied;
    private bool _playerEscaped;
    private readonly List<ExitPoint> _exits = new();
    private readonly List<SparkingFence> _sparks = new();
    private long _sparkTick = -1;
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
        Weather = new Weather(Rng.Fork("weather").NextULong());
        _injuryRng = Rng.Fork("injuries");
        _gearRng = Rng.Fork("gear");
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

    /// <summary>Raised when a section is wounded: (actor, section, its severity now, cause).</summary>
    public event Action<Actor, BodySection, Severity, string>? ActorInjured;

    /// <summary>Raised when an actor dies: (actor, cause). Its body stays where it fell.</summary>
    public event Action<Actor, string>? ActorDied;

    /// <summary>Raised when someone steps onto an exit and leaves the map.</summary>
    public event Action<Actor, ExitPoint>? ActorEscaped;

    /// <summary>Raised when a sparking fence goes dead because its section was broken.</summary>
    public event Action<SparkingFence>? SparksDied;

    /// <summary>Raised when cover is smashed or bitten through: (tile, what it was, who did it).</summary>
    public event Action<TilePos, CoverType, Actor?>? CoverBroken;

    /// <summary>Raised when a concussed actor blacks out for a few seconds.</summary>
    public event Action<Actor>? ActorBlackedOut;

    /// <summary>Raised after an attempt at first aid: (who gave it, who got it, section treated, whether it worked).</summary>
    public event Action<Actor, Actor, BodySection, bool>? ActorTreated;

    /// <summary>Raised when someone cries out on purpose (panicking): (who).</summary>
    public event Action<Actor>? ActorScreamed;

    /// <summary>Raised when an order is given and heard: (who gave it, who got it, the order).</summary>
    public event Action<Actor, Actor, NpcOrder>? OrderGiven;

    /// <summary>Raised when something is thrown: (thrower, what, where it landed).</summary>
    public event Action<Actor, ItemKind, TilePos>? ItemThrown;

    /// <summary>Raised after a rappel: (actor, whether it slipped and fell part of the way).</summary>
    public event Action<Actor, bool>? Rappelled;

    /// <summary>Raised as time passes a lightning strike: the flash is now; its thunder follows.</summary>
    public event Action<LightningStrike>? LightningStruck;

    /// <summary>Raised when the weather is set.</summary>
    public event Action<WeatherSettings>? WeatherChanged;

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

    /// <summary>Ropes left tied where people rappelled down.</summary>
    public IReadOnlyList<RopeSpot> Ropes => _ropes;

    /// <summary>When a fence tile (electric or sagging) first broke, by anything: the moment something is loose.</summary>
    public long? FenceBreachTick { get; private set; }

    /// <summary>The map's escape hatches.</summary>
    public IReadOnlyList<ExitPoint> Exits => _exits;

    /// <summary>Shorted fence sections, live or dead.</summary>
    public IReadOnlyList<SparkingFence> Sparks => _sparks;

    /// <summary>The exit on a tile, if there is one.</summary>
    public ExitPoint? ExitAt(TilePos p)
    {
        foreach (var exit in _exits)
        {
            if (exit.Position == p)
                return exit;
        }
        return null;
    }

    /// <summary>Adds an escape hatch (scenario setup).</summary>
    public ExitPoint AddExit(TilePos position, string name)
    {
        if (!Map.InBounds(position))
            throw new ArgumentOutOfRangeException(nameof(position), $"Exit {name} at {position} is off the map.");
        var exit = new ExitPoint(position, name);
        _exits.Add(exit);
        return exit;
    }

    /// <summary>
    /// Makes a sagging-fence tile spark: a low flickering light, and a crackle every 1.5 s, until the
    /// section is broken (scenario setup). The tile must be a sagging fence ('w').
    /// </summary>
    public SparkingFence AddSparks(TilePos position)
    {
        if (!Map.InBounds(position) || Cover[position] != CoverType.WeakFence)
            throw new ArgumentException($"Sparks at {position} need a sagging fence tile ('w').", nameof(position));
        AddLight(new LightSource(position, HeightDm: 15, RadiusTiles: 3, IntensityPercent: 70));
        var sparks = new SparkingFence(position, _lights.Count - 1);
        _sparks.Add(sparks);
        return sparks;
    }

    /// <summary>Where bleeding actors have left blood, oldest first (what you see; what predators smell is in ScentMarks).</summary>
    public IReadOnlyList<BloodSpot> BloodSpots => _blood;

    /// <summary>Scent on the ground, oldest first: body scent and blood. Only the last few thousand are kept.</summary>
    public IReadOnlyList<ScentMark> ScentMarks => _scent;

    /// <summary>Ground vibrations from the last <see cref="NoiseMemoryTicks"/> ticks, oldest first.</summary>
    public IReadOnlyList<VibrationEvent> RecentVibrations => _vibrations;

    /// <summary>Rain, wind, fog and storm, and what they do to the senses now.</summary>
    public Weather Weather { get; }

    /// <summary>Ambient light now: the set level, or lightning's while a flash lasts.</summary>
    public int EffectiveAmbientPercent =>
        Weather.Flash(CurrentTick) ? Math.Max(_ambientLight, Weather.FlashAmbientPercent) : _ambientLight;

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

    /// <summary>Light level per tile; recomputed when lights, ambient light (lightning too), cover or doors change.</summary>
    public LightGrid Light
    {
        get
        {
            // Checked once per tick: the renderer asks for every tile, and a storm's flash check isn't free.
            if (_lightGrid is not null && _lightGridTick == CurrentTick)
                return _lightGrid;
            int ambient = EffectiveAmbientPercent;
            if (_lightGrid is null || _lightGridAmbient != ambient)
            {
                _lightGrid = LightGrid.Compute(Map, _lights, ambient);
                _lightGridAmbient = ambient;
            }
            _lightGridTick = CurrentTick;
            return _lightGrid;
        }
    }

    /// <summary>Sets the weather from now on (scenario setup, debug keys; not recorded in replays).</summary>
    public void SetWeather(WeatherSettings settings)
    {
        Weather.Set(settings, CurrentTick);
        _lightGrid = null; // a storm may start or stop mid-flash
        WeatherChanged?.Invoke(Weather.Settings);
    }

    /// <param name="onDeck">Start on the deck over <paramref name="position"/> (single-tile actors only).</param>
    /// <param name="loadout">Skills and starting gear; none if left out.</param>
    public Actor AddActor(string name, char glyph, TilePos position, Species species, IActorController controller,
        Direction facing = Direction.South, bool onDeck = false, Loadout? loadout = null)
    {
        var actor = new Actor(_actors.Count, name, glyph, position, species, facing, controller)
        {
            NextActTick = CurrentTick,
            OnDeck = onDeck,
            Skills = (loadout ?? Loadout.Empty).Skills,
        };
        actor.Health.LastUpdatedTick = CurrentTick;
        foreach (var (kind, count) in (loadout ?? Loadout.Empty).Items)
            actor.Inventory.Add(kind, count);
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

    /// <summary>Switches a light on or off. Lights are never removed, so their indices stay valid.</summary>
    public void SetLightOn(int index, bool on)
    {
        if (_lights[index].On == on)
            return;
        _lights[index] = _lights[index] with { On = on };
        _lightGrid = null;
    }

    /// <summary>Turns off lights whose time is up (burnt-out flares).</summary>
    private void ExpireLights()
    {
        for (int i = _lightExpiry.Count - 1; i >= 0; i--)
        {
            if (_lightExpiry[i].OffTick > CurrentTick)
                continue;
            SetLightOn(_lightExpiry[i].Light, false);
            _lightExpiry.RemoveAt(i);
        }
    }

    public void EmitNoise(TilePos position, int loudness, string kind, int? actorId = null)
    {
        _noises.Add(new NoiseEvent(CurrentTick, position, loudness, kind, actorId));
        _noises.RemoveAll(n => n.Tick < CurrentTick - NoiseMemoryTicks);
    }

    /// <summary>
    /// Shakes the ground at a tile with a blow of <paramref name="strength"/>: what's recorded is what
    /// the ground there carries (rock and decks more, soft ground less, mud less again).
    /// </summary>
    public void EmitVibration(TilePos position, int strength, int? actorId = null, bool onDeck = false)
    {
        if (strength <= 0 || !Map.InBounds(position))
            return;
        var cover = onDeck ? Covers.Deck : Map.CoverAt(position);
        int carried = strength * Weather.VibrationPercent(cover, outdoors: !Map.IsIndoor(position)) / 100;
        if (carried <= 0)
            return;
        _vibrations.Add(new VibrationEvent(CurrentTick, position, carried, actorId));
        _vibrations.RemoveAll(v => v.Tick < CurrentTick - NoiseMemoryTicks);
    }

    /// <summary>
    /// Leaves scent where an actor is: its body scent, and blood too if it's bleeding. Nothing gets out
    /// of an intact vehicle's cab.
    /// </summary>
    private void LayScent(Actor actor, TilePos at)
    {
        actor.LastScentTick = CurrentTick;
        if (VehicleAt(at) is { Breached: false })
            return;
        long wash = Weather.Wash(CurrentTick);
        if (actor.Species.ScentStrength > 0)
            AddScent(new ScentMark(at, CurrentTick, actor.Id, actor.Species.ScentStrength, Blood: false, wash));
        if (actor.Health.BleedingWounds > 0)
            AddScent(new ScentMark(at, CurrentTick, actor.Id, ScentRules.BloodStrength, Blood: true, wash));
    }

    private void AddScent(ScentMark mark)
    {
        _scent.Add(mark);
        if (_scent.Count > ScentRules.MaxMarks + ScentRules.TrimBatch)
            _scent.RemoveRange(0, ScentRules.TrimBatch);
    }

    /// <summary>Raises LightningStruck for every strike time has passed since the last call.</summary>
    private void AdvanceWeather()
    {
        if (CurrentTick <= _weatherTick)
            return;
        long from = _weatherTick;
        _weatherTick = CurrentTick;
        foreach (var strike in Weather.StrikesBetween(from, CurrentTick))
            LightningStruck?.Invoke(strike);
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

    /// <summary>An actor's senses as its wounds leave them (a hurt head blurs vision and dulls hearing).</summary>
    public SenseProfile SensesOf(Actor actor)
    {
        var senses = actor.Health.Adjust(actor.Species.Senses);
        int percent = Weather.SightRangePercent;
        return percent >= 100 ? senses : senses with { SightRangeTiles = senses.SightRangeTiles * percent / 100 };
    }

    /// <summary>
    /// Wounds a section. A red wound on a section that's already red kills; a yellow one on a yellow
    /// section makes it red; a yellow one on a red section changes nothing. Public for setup and debug;
    /// in play, bites, tail blows and falls call it.
    /// </summary>
    public void Injure(Actor actor, BodySection section, Severity severity, bool bleeding, string cause)
    {
        if (actor.IsDead || severity == Severity.Green)
            return;
        UpdateHealth(actor);
        if (actor.IsDead)
            return;

        var health = actor.Health;
        var current = health[section];
        if (severity == Severity.Red)
        {
            if (current.Severity == Severity.Red)
            {
                Kill(actor, $"{cause}: a second major wound to the {InjuryRules.Name(section)}");
                return;
            }
            health.Set(section, new Wound(Severity.Red, bleeding, CurrentTick));
        }
        else if (current.Severity == Severity.Red)
        {
            return;
        }
        else
        {
            var now = current.Severity == Severity.Yellow ? Severity.Red : Severity.Yellow;
            health.Set(section, new Wound(now, false, CurrentTick));
        }
        ActorInjured?.Invoke(actor, section, health[section].Severity, cause);
    }

    /// <summary>
    /// Brings bleeding and healing up to the current tick: blood loss grows with every bleeding wound
    /// and kills at the limit; yellow wounds left alone long enough heal to green.
    /// </summary>
    public void UpdateHealth(Actor actor)
    {
        var health = actor.Health;
        long elapsed = CurrentTick - health.LastUpdatedTick;
        if (actor.IsDead || elapsed <= 0)
            return;

        health.BloodLossTicks += elapsed * health.BleedingWounds;
        health.LastUpdatedTick = CurrentTick;
        if (health.BloodLossTicks >= InjuryRules.BleedOutTicks)
        {
            Kill(actor, "bled out");
            return;
        }

        foreach (var section in Enum.GetValues<BodySection>())
        {
            var wound = health[section];
            if (wound.Severity == Severity.Yellow && CurrentTick - wound.SinceTick >= InjuryRules.YellowHealTicks)
                health.Set(section, Wound.None);
        }
    }

    /// <summary>Picks a section for a blow by the hit weights, skipping any in <paramref name="exclude"/>.</summary>
    private BodySection RandomSection(ICollection<BodySection> exclude)
    {
        int total = InjuryRules.HitWeights.Where(w => !exclude.Contains(w.Section)).Sum(w => w.Weight);
        int roll = _injuryRng.NextInt(Math.Max(1, total));
        foreach (var (section, weight) in InjuryRules.HitWeights)
        {
            if (exclude.Contains(section))
                continue;
            if (roll < weight)
                return section;
            roll -= weight;
        }
        return BodySection.Torso;
    }

    /// <summary>Someone reached an exit: they leave the map (and their flashlight goes with them).</summary>
    private void Escape(Actor actor, ExitPoint exit)
    {
        actor.EscapedVia = exit;
        actor.EscapedTick = CurrentTick;
        Vacate(actor);
        if (actor.CarriedLight is { } light && _lights[light].On)
            SetLightOn(light, false);
        if (actor.Controller is PlayerController)
            _playerEscaped = true;
        ActorEscaped?.Invoke(actor, exit);
    }

    /// <summary>
    /// Live sparks crackle once per 1.5 s window that time has passed into; sparks whose section has
    /// been broken go dead (light off, silent).
    /// </summary>
    private void AdvanceSparks()
    {
        if (_sparks.Count == 0 || CurrentTick <= _sparkTick)
            return;
        long from = _sparkTick;
        _sparkTick = CurrentTick;
        bool crackle = from < 0 || CurrentTick / SparkingFence.CrackleTicks > from / SparkingFence.CrackleTicks;
        CheckSparks();
        if (!crackle)
            return;
        foreach (var sparks in _sparks)
        {
            if (sparks.Live)
                EmitNoise(sparks.Position, SparkingFence.CrackleLoudness, NoiseKinds.Sparks);
        }
    }

    /// <summary>Kills sparks whose tile is no longer a sagging fence.</summary>
    private void CheckSparks()
    {
        foreach (var sparks in _sparks)
        {
            if (!sparks.Live || Cover[sparks.Position] == CoverType.WeakFence)
                continue;
            sparks.Live = false;
            sparks.DiedTick = CurrentTick;
            SetLightOn(sparks.LightIndex, false);
            SparksDied?.Invoke(sparks);
        }
    }

    private void Kill(Actor actor, string cause)
    {
        if (actor.IsDead)
            return;
        actor.Health.DeathCause = cause;
        actor.Health.DiedTick = CurrentTick;
        Vacate(actor);
        if (actor.Controller is PlayerController)
            _playerDied = true;
        ActorDied?.Invoke(actor, cause);
    }

    /// <summary>A fall past the safe drop wounds a leg, and worse the further it goes.</summary>
    private void ApplyFall(Actor actor, int dropDm)
    {
        int excess = dropDm - actor.Movement.MaxDropDm;
        if (excess <= 0)
            return;
        var leg = _injuryRng.Chance(1, 2) ? BodySection.LeftLeg : BodySection.RightLeg;
        foreach (var (section, severity) in InjuryRules.FallWounds(excess, leg))
            Injure(actor, section, severity, bleeding: false, $"fell {Units.FormatMeters(dropDm)}");
    }

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

    /// <summary>
    /// Whether a height change is manageable: walked up to the walk limit, climbed up to the climb
    /// limit (if the actor climbs and its wounds allow), dropped down to the safe drop.
    /// </summary>
    private static string? HeightStepReason(Actor actor, int step)
    {
        if (step > actor.Movement.MaxStepUpDm
            && !(step <= actor.Movement.ClimbUpDm && !actor.IsLarge && actor.Health.CanClimb))
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
        if (!PeekLiving(out int id, out long tick))
            return StepResult.Idle;

        CurrentTick = tick;
        ExpireLights();
        AdvanceWeather();
        AdvanceSparks();
        var actor = _actors[id];
        UpdateHealth(actor);
        if (actor.IsGone)
        {
            _scheduler.TryDequeue(out _, out _);
            return StepResult.Acted;
        }
        if (CurrentTick - actor.LastScentTick >= ScentRules.IdleMarkTicks)
            LayScent(actor, actor.Position);

        var action = actor.Controller.Decide(actor, this);
        if (action is null)
            return StepResult.AwaitingInput;

        if (actor.Health.Concussed && _injuryRng.Chance(InjuryRules.BlackoutPercent, 100))
        {
            action = new WaitAction(InjuryRules.BlackoutTicks);
            ActorBlackedOut?.Invoke(actor);
        }

        _scheduler.TryDequeue(out _, out _);
        actor.Charging = false; // only a charging step sets it again
        int cost = Resolve(actor, action);
        if (!actor.IsLarge && !actor.IsGone && !actor.OnDeck && ExitAt(actor.Position) is { } exit)
            Escape(actor, exit);
        actor.NextActTick = tick + cost;
        _scheduler.Schedule(id, actor.NextActTick);
        ActionsResolved++;
        return StepResult.Acted;
    }

    /// <summary>The next actor due that's still alive; the dead are dropped from the schedule on the way.</summary>
    private bool PeekLiving(out int id, out long tick)
    {
        while (_scheduler.TryPeek(out id, out tick))
        {
            if (!_actors[id].IsGone)
                return true;
            _scheduler.TryDequeue(out _, out _);
        }
        return false;
    }

    /// <summary>Runs every turn due at or before the given tick, stopping early if input is needed.</summary>
    public void RunUntil(long tick)
    {
        while (PeekLiving(out _, out long next) && next <= tick)
        {
            if (Step() == StepResult.AwaitingInput)
                return;
        }
        if (tick > CurrentTick)
            CurrentTick = tick;
        ExpireLights();
        AdvanceWeather();
        AdvanceSparks();
    }

    /// <summary>
    /// Turn-based mode: runs everyone else until an actor needs input, or until an actor driven by a
    /// PlayerController has died (the run is over; StepResult.PlayerDied).
    /// </summary>
    public StepResult RunUntilInputNeeded(int maxActions = 100_000)
    {
        for (int i = 0; i < maxActions; i++)
        {
            if (_playerDied)
                return StepResult.PlayerDied;
            if (_playerEscaped)
                return StepResult.PlayerEscaped;
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
                .Add(actor.NextActTick)
                .Add(actor.Health.BloodLossTicks)
                .Add(actor.IsDead ? 1 : 0);
            foreach (var section in Enum.GetValues<BodySection>())
                hasher.Add((int)actor.Health[section].Severity).Add(actor.Health[section].Bleeding ? 1 : 0);
        }
        foreach (var vehicle in _vehicles)
        {
            hasher.Add(vehicle.Anchor.X)
                .Add(vehicle.Anchor.Y)
                .Add(vehicle.Integrity);
        }
        foreach (var platform in _platforms)
            hasher.Add(platform.Collapsed ? 1 : 0);
        foreach (var light in _lights)
            hasher.Add(light.Position.X).Add(light.Position.Y).Add(light.On ? 1 : 0);
        Weather.AddTo(hasher);
        foreach (var actor in _actors)
            hasher.Add(actor.EscapedTick ?? -1);
        foreach (var sparks in _sparks)
            hasher.Add(sparks.Live ? 1 : 0);
        hasher.Add(FenceBreachTick ?? -1);
        hasher.Add(_scent.Count).Add(_vibrations.Count);
        if (_scent.Count > 0)
            hasher.Add(_scent[^1].Tick).Add(_scent[^1].Position.X).Add(_scent[^1].Position.Y);
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
        MoveCarriedLight(actor);
    }

    /// <summary>Keeps a carried flashlight at its holder, pointing where they face.</summary>
    private void MoveCarriedLight(Actor actor)
    {
        if (actor.CarriedLight is not { } index)
            return;
        var light = _lights[index];
        if (light.Position == actor.Position && light.Facing == actor.Facing)
            return;
        _lights[index] = light with { Position = actor.Position, Facing = actor.Facing };
        if (light.On)
            _lightGrid = null;
    }

    private int Resolve(Actor actor, ActorAction action) => action switch
    {
        WaitAction wait => Math.Max(1, wait.Ticks),
        MoveAction move => actor.IsLarge ? TryMoveLarge(actor, move.Direction, move.Shove, move.Sprint) : TryMove(actor, move.Direction, move.Sprint),
        BiteAction bite => ResolveBite(actor, bite.Investigative),
        UseDoorAction door => UseDoor(actor, door.Direction),
        ClimbAction => Climb(actor),
        TreatAction treat => Treat(actor, treat.PatientId),
        ScreamAction => Scream(actor),
        OrderAction order => GiveOrder(actor, order.TargetId, order.Order),
        ThrowAction thrown => Throw(actor, thrown.Item, thrown.Target),
        ToggleLightAction => ToggleLight(actor),
        RappelAction rappel => Rappel(actor, rappel.Direction),
        _ => throw new ArgumentOutOfRangeException(nameof(action), $"Unknown action {action}."),
    };

    private static int Chebyshev(TilePos a, TilePos b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private int Bump(Actor actor, string reason)
    {
        EmitNoise(actor.Position, BumpLoudness, NoiseKinds.Bump, actor.Id);
        MoveBlocked?.Invoke(actor, reason);
        return BumpTicks;
    }

    private int TryMove(Actor actor, Direction direction, bool sprint)
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
        if (actor.Health.BleedingWounds > 0)
            DropBlood(actor.Position);
        Place(actor, to, direction, toDeck);
        actor.LastMovedTick = CurrentTick;
        LayScent(actor, to);

        int loudness = actor.Species.FootstepLoudness + cover.NoiseModifier + actor.Health.FootstepBonus;
        if (step > actor.Movement.MaxStepUpDm)
        {
            // A climb: hands and feet, slow and noisy, sprinting or not.
            EmitNoise(to, loudness + GearRules.ClimbLoudnessBonus, NoiseKinds.Scrambling, actor.Id);
            EmitVibration(to, actor.Species.VibrationStrength, actor.Id, toDeck);
            return GearRules.ClimbStepTicks(step, actor.Skills.Climbing) * actor.Health.ClimbCostPercent / 100;
        }

        bool running = sprint && actor.Health.CanSprint;
        EmitNoise(to, loudness + (running ? GearRules.SprintLoudnessBonus : 0), NoiseKinds.Footsteps, actor.Id);
        EmitVibration(to, actor.Species.VibrationStrength * (running ? ScentRules.SprintVibrationPercent : 100) / 100,
            actor.Id, toDeck);
        int cost = actor.Movement.CostTicks(step, direction.IsDiagonal()) * cover.MoveCostPercent / 100
            * actor.Health.StepCostPercent / 100;
        if (running)
            cost = cost * (actor.Health.SprintWinded ? GearRules.WindedSprintCostPercent : GearRules.SprintCostPercent) / 100;
        return Math.Max(1, cost);
    }

    /// <summary>
    /// Throws a flare toward a tile: it lands short of the target past the thrower's range, scatters
    /// with distance (less with skill), comes back off walls, then lights up and hisses for 90 s.
    /// </summary>
    private int Throw(Actor actor, ItemKind item, TilePos target)
    {
        if (item != ItemKind.Flare)
            return Bump(actor, "that isn't something to throw");
        if (!actor.Inventory.Has(item))
            return Bump(actor, "no flares left");
        int range = GearRules.ThrowRange(actor.Skills.Throwing, actor.Health);
        if (range <= 0)
            return Bump(actor, "both arms are useless");

        var from = actor.Position;
        int dx = target.X - from.X, dy = target.Y - from.Y;
        int distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
        if (distance > range)
        {
            dx = dx * range / distance;
            dy = dy * range / distance;
            distance = range;
        }
        int scatter = GearRules.Scatter(distance, actor.Skills.Throwing);
        if (scatter > 0)
        {
            dx += _gearRng.NextInt(-scatter, scatter + 1);
            dy += _gearRng.NextInt(-scatter, scatter + 1);
        }

        // Land on the map, and not inside a wall: walk back toward the thrower until it's open ground.
        var land = new TilePos(Math.Clamp(from.X + dx, 0, Map.Width - 1), Math.Clamp(from.Y + dy, 0, Map.Height - 1));
        int steps = Math.Max(Math.Abs(land.X - from.X), Math.Abs(land.Y - from.Y));
        for (int k = steps; k > 0 && Map.CoverAt(land).BlocksMovement; k--)
            land = new TilePos(from.X + (land.X - from.X) * (k - 1) / k, from.Y + (land.Y - from.Y) * (k - 1) / k);

        actor.Inventory.Use(item);
        actor.Facing = Directions.FromDelta(land.X - from.X, land.Y - from.Y);
        actor.LastMovedTick = CurrentTick;
        MoveCarriedLight(actor);

        _lights.Add(LightSource.Flare(land));
        _lightExpiry.Add((_lights.Count - 1, CurrentTick + GearRules.FlareBurnTicks));
        _lightGrid = null;
        EmitNoise(land, GearRules.FlareLoudness, NoiseKinds.Flare); // no actor: it's the flare that hisses
        ItemThrown?.Invoke(actor, item, land);
        return GearRules.ThrowTicks;
    }

    /// <summary>Switches the carried flashlight on (the first time, it joins the lights) or off.</summary>
    private int ToggleLight(Actor actor)
    {
        if (!actor.Inventory.Has(ItemKind.Flashlight))
            return Bump(actor, "no flashlight");
        if (actor.CarriedLight is { } index)
        {
            SetLightOn(index, !_lights[index].On);
        }
        else
        {
            _lights.Add(LightSource.Flashlight(actor.Position, actor.Facing));
            actor.CarriedLight = _lights.Count - 1;
            _lightGrid = null;
        }
        return GearRules.FlashlightToggleTicks;
    }

    /// <summary>
    /// Ropes down to the next tile's ground when the drop is too far to jump and the rope reaches.
    /// Hurt arms (or no training) make it a gamble: a slip falls part of the way. The rope stays tied.
    /// </summary>
    private int Rappel(Actor actor, Direction direction)
    {
        actor.Facing = direction;
        if (actor.IsLarge || !actor.Inventory.Has(ItemKind.Rope))
            return Bump(actor, "no rope");
        if (!actor.Health.CanRappel)
            return Bump(actor, "too badly hurt to rappel");

        var to = actor.Position.Offset(direction);
        if (!Map.InBounds(to))
            return Bump(actor, "the edge of the map");
        int drop = StandHeight(actor) - Terrain[to];
        if (drop <= actor.Movement.MaxDropDm)
            return Bump(actor, "no need for a rope here");
        if (drop > GearRules.RopeLengthDm)
            return Bump(actor, "the rope won't reach");
        if (Map.CoverAt(to).BlocksMovement)
            return Bump(actor, $"{Map.CoverAt(to).Name} is in the way at the bottom");
        if (ActorAt(to) is { } below)
            return Bump(actor, $"{below.Name} is in the way at the bottom");
        if (Map.Structures.HasDeck(to) && Map.Structures.DeckDm(to) < actor.Species.BodyHeightDm)
            return Bump(actor, "a platform is in the way at the bottom");

        var top = actor.Position;
        actor.Inventory.Use(ItemKind.Rope);
        _ropes.Add(new RopeSpot(top, to));
        bool slipped = _gearRng.Chance(GearRules.RappelFailPercent(actor.Skills.Rappelling, actor.Health.YellowArms), 100);
        Place(actor, to, direction, onDeck: false);
        actor.LastMovedTick = CurrentTick;
        LayScent(actor, to);
        EmitNoise(to, GearRules.RappelLoudness, NoiseKinds.Rope, actor.Id);
        if (slipped)
        {
            int fallen = drop * _gearRng.NextInt(30, 101) / 100;
            ActorFell?.Invoke(actor, fallen);
            ApplyFall(actor, fallen);
            CryOut(actor);
        }
        Rappelled?.Invoke(actor, slipped);
        return GearRules.RappelTicks(drop, actor.Skills.Rappelling);
    }

    private void DropBlood(TilePos at)
    {
        _blood.Add(new BloodSpot(at, CurrentTick));
        if (_blood.Count > InjuryRules.MaxBloodSpots)
            _blood.RemoveAt(0);
    }

    /// <summary>
    /// First aid on the worst red wound, on yourself or on someone next to you at the same level:
    /// the treater's kit, a working arm of theirs, and their skill (or luck).
    /// </summary>
    private int Treat(Actor actor, int? patientId)
    {
        var patient = actor;
        if (patientId is { } id && id != actor.Id)
        {
            if (id < 0 || id >= _actors.Count)
                return Bump(actor, "nobody there");
            patient = _actors[id];
            if (patient.IsDead)
                return Bump(actor, $"{patient.Name} is beyond help");
            if (patient.HasEscaped)
                return Bump(actor, $"{patient.Name} is gone");
            if (Chebyshev(actor.Position, patient.Position) > 1 || patient.OnDeck != actor.OnDeck)
                return Bump(actor, $"{patient.Name} is out of reach");
        }

        var health = patient.Health;
        if (health.WorstUntreated() is not { } section)
            return Bump(actor, patient == actor ? "nothing needs first aid" : $"{patient.Name} needs no first aid");
        if (!actor.Health.CanTreat)
            return Bump(actor, "both arms are useless");
        if (!actor.Inventory.Has(ItemKind.FirstAidKit))
            return Bump(actor, "no first-aid kit");

        int skill = actor.Skills.FirstAid;
        bool worked = _injuryRng.Chance(InjuryRules.TreatSuccessPercent(skill), 100);
        if (worked)
        {
            actor.Inventory.Use(ItemKind.FirstAidKit);
            health.Set(section, new Wound(Severity.Yellow, false, CurrentTick));
        }
        EmitNoise(actor.Position, InjuryRules.TreatLoudness, NoiseKinds.FirstAid, actor.Id);
        ActorTreated?.Invoke(actor, patient, section, worked);
        return InjuryRules.TreatTicks(skill);
    }

    /// <summary>A scream on purpose: as loud as one from a bite, and any predator in earshot knows what it means.</summary>
    private int Scream(Actor actor)
    {
        if (actor.Species.ScreamLoudness <= 0)
            return Bump(actor, "can't scream");
        EmitNoise(actor.Position, actor.Species.ScreamLoudness, NoiseKinds.Scream, actor.Id);
        ActorScreamed?.Invoke(actor);
        return ScreamTicks;
    }

    /// <summary>
    /// Tell someone within earshot what to do. It's said aloud (a little noise) and becomes their
    /// standing order; whether they follow it is up to them.
    /// </summary>
    private int GiveOrder(Actor actor, int targetId, NpcOrder order)
    {
        if (targetId < 0 || targetId >= _actors.Count || targetId == actor.Id)
            return Bump(actor, "nobody to tell");
        var target = _actors[targetId];
        if (target.IsGone)
            return Bump(actor, $"{target.Name} can't hear you");
        if (Chebyshev(actor.Position, target.Position) > OrderRange)
            return Bump(actor, $"{target.Name} is too far away to hear you");

        EmitNoise(actor.Position, TalkLoudness, NoiseKinds.Talking, actor.Id);
        target.Order = new StandingOrder(order, actor.Id, CurrentTick);
        OrderGiven?.Invoke(actor, target, order);
        return OrderTicks;
    }

    /// <summary>Climbs the ladder on the actor's tile, up to the deck or down to the ground.</summary>
    private int Climb(Actor actor)
    {
        var p = actor.Position;
        if (!actor.Species.ClimbsLadders || actor.IsLarge)
            return Bump(actor, "it can't climb");
        if (!Map.Structures.HasLadder(p) || !Map.Structures.HasDeck(p))
            return Bump(actor, "there's no ladder here");
        if (!actor.Health.CanClimb)
            return Bump(actor, "too badly hurt to climb");

        bool up = !actor.OnDeck;
        if (ActorAt(p, onDeck: up) is { } inTheWay)
            return Bump(actor, $"{inTheWay.Name} is in the way {(up ? "at the top" : "at the bottom")}");
        if (!up && Map.CoverAt(p).BlocksMovement)
            return Bump(actor, $"{Map.CoverAt(p).Name} is at the bottom");

        Place(actor, p, actor.Facing, up);
        actor.LastMovedTick = CurrentTick;
        EmitNoise(p, ClimbLoudness, NoiseKinds.Climbing, actor.Id);
        return Math.Max(1, Map.Structures.DeckDm(p) * actor.Movement.ClimbTicksPerDm * actor.Health.ClimbCostPercent / 100);
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

    /// <param name="charge">Charge if it can: only a straight step that shoves nothing and smashes nothing goes at a charge.</param>
    private int TryMoveLarge(Actor actor, Direction direction, bool allowShove, bool charge = false)
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
                bool broken = _injuryRng.Chance(InjuryRules.TailBlowRedPercent, 100);
                Injure(victim, RandomSection(Array.Empty<BodySection>()), broken ? Severity.Red : Severity.Yellow,
                    bleeding: false, $"struck by the {actor.Name}'s tail");
                CryOut(victim);
            }
        }

        var anchorCover = Map.CoverAt(anchor);
        bool charging = charge && !turning && shove is null && smashed == 0 && actor.Movement.ChargeTicks > 0;
        actor.Charging = charging;
        EmitNoise(anchor, actor.Species.FootstepLoudness + anchorCover.NoiseModifier + (charging ? GearRules.SprintLoudnessBonus : 0),
            NoiseKinds.Footsteps, actor.Id);
        EmitVibration(anchor, actor.Species.VibrationStrength
            * (turning ? ScentRules.TurnVibrationPercent : charging ? ScentRules.SprintVibrationPercent : 100) / 100, actor.Id);
        if (!turning)
            LayScent(actor, anchor);
        if (smashed > 0)
        {
            EmitNoise(anchor, SmashLoudness, NoiseKinds.Crashing, actor.Id);
            EmitVibration(anchor, ScentRules.SmashVibration, actor.Id);
        }

        int cost = turning
            ? actor.Movement.TurnTicks
            : (charging
                ? actor.Movement.ChargeCostTicks(heightStep, facing.IsDiagonal())
                : actor.Movement.CostTicks(heightStep, facing.IsDiagonal())) * anchorCover.MoveCostPercent / 100;
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
        var occupants = _actors.Where(a => !a.IsLarge && !a.OnDeck && !a.IsGone && oldSet.Contains(a.Position)).ToList();
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
        EmitVibration(vehicle.Anchor, ScentRules.ShoveVibration, by.Id);
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
        // Jaws snap what's weak enough (a sagging fence); vehicles take damage instead (below).
        foreach (var tile in coverBitten)
            TryBreak(tile, actor.Species.BiteDamage, actor);

        foreach (var hit in hits)
        {
            ActorBitten?.Invoke(actor, hit.Victim);
            var wounded = new List<BodySection>();
            for (int i = 0; i < actor.Species.BiteWounds && !hit.Victim.IsDead; i++)
            {
                var section = RandomSection(wounded);
                wounded.Add(section);
                Injure(hit.Victim, section, Severity.Red, bleeding: true, $"bitten by the {actor.Name}");
            }
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
        if (victim.Species.ScreamLoudness > 0 && !victim.IsDead)
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
        if (type is CoverType.Fence or CoverType.WeakFence)
            FenceBreachTick ??= CurrentTick;
        CoverBroken?.Invoke(tile, type, by);
        if (type == CoverType.Support && PlatformAt(tile) is { Collapsed: false } platform)
            CheckCollapse(platform, by);
        if (type == CoverType.WeakFence)
            CheckSparks();
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

        var tiles = new HashSet<TilePos>(platform.Tiles);
        foreach (var body in _actors)
        {
            if (body.IsDead && body.OnDeck && tiles.Contains(body.Position))
                body.OnDeck = false; // bodies come down with it
        }

        var fell = new List<Actor>();
        foreach (var (rider, fromDm) in riders)
        {
            rider.OnDeck = false;
            rider.Position = LandingSpot(rider.Position);
            rider.Body = rider.Species.Body.Footprint(rider.Position, Compass16.Of(rider.Facing));
            Occupy(rider);
            fell.Add(rider);
            int drop = Math.Max(0, fromDm - Terrain[rider.Position]);
            ActorFell?.Invoke(rider, drop);
            ApplyFall(rider, drop);
            CryOut(rider);
        }

        StructureChanges++;
        _lightGrid = null;
        EmitNoise(platform.Tiles[platform.Tiles.Count / 2], CollapseLoudness, NoiseKinds.Collapse, by?.Id);
        EmitVibration(platform.Tiles[platform.Tiles.Count / 2], ScentRules.CollapseVibration, by?.Id);
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
