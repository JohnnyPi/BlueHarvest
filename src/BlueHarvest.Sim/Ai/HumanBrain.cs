using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.Injuries;
using BlueHarvest.Sim.Navigation;
using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim.Ai;

/// <summary>What a person is doing, for display and for tests.</summary>
public enum Mood : byte
{
    /// <summary>Nothing to worry about yet.</summary>
    Calm,

    /// <summary>Something is out there, not close enough to act on.</summary>
    Wary,

    /// <summary>Under cover and keeping still.</summary>
    Hiding,

    /// <summary>Making for cover.</summary>
    SeekingShelter,

    /// <summary>Keeping perfectly still because the threat sees motion.</summary>
    Frozen,

    /// <summary>Lost their nerve: running and screaming.</summary>
    Panicking,

    /// <summary>Going to give first aid to someone bleeding.</summary>
    Helping,

    /// <summary>Following an order to keep close.</summary>
    Following,

    /// <summary>Following an order to stay put.</summary>
    Staying,
}

/// <summary>
/// A person's mind, on the same pipeline as the Rex: perceive, remember, decide. Their memory sorts
/// others into allies and threats; fear builds from how near and certain the threats are, screams
/// close by, and their own wounds. In order they: panic (run and scream) once fear passes their
/// panic threshold; obey an order; freeze if they know the Rex hunts by motion; get to shelter and
/// keep still once afraid; go and help a bleeding companion if brave enough and carrying a kit;
/// otherwise wait. They never read the world beyond their senses, except for things in plain sight
/// up close (an ally they're looking at is visibly bleeding; which tiles are cover).
/// </summary>
public sealed class HumanBrain : IActorController, IWindowShiftListener
{
    /// <summary>Threats this far away (tiles) add nothing to fear.</summary>
    public const int FearRange = 40;

    /// <summary>Fear at which people head for cover.</summary>
    public const int ShelterFear = 30;

    /// <summary>Fear at which they sprint to cover rather than walk.</summary>
    public const int HurryFear = 60;

    /// <summary>Those who know about motion vision freeze when a threat is this close (tiles).</summary>
    public const int FreezeRange = 20;

    /// <summary>A panic ends once fear falls this far below the threshold.</summary>
    public const int PanicHysteresis = 15;

    /// <summary>Fear fades by this much per second without new cause.</summary>
    public const int FearFadePerSecond = 3;

    /// <summary>A panicking person screams on this share of their turns.</summary>
    public const int PanicScreamPercent = 15;

    /// <summary>The most fear a scream heard close by adds, fading over 3 s and with distance.</summary>
    public const int ScreamFear = 25;

    /// <summary>Selfish people (above this) ignore orders once they're afraid.</summary>
    public const int SelfishnessToRefuse = 60;

    private const long NeverPlanned = long.MinValue / 2;
    private const int ShelterReplanTicks = 1000;

    private readonly Rng _rng;
    private readonly GoToController _goToShelter;
    private readonly GoToController _goToPerson;
    private TilePos _shelterGoal;
    private TilePos _personGoal;
    private long _lastThinkTick = long.MinValue;
    private int _fearPeak;
    private long _fearPeakTick;
    private long _shelterPlannedTick = NeverPlanned;
    private TilePos? _shelter;

    /// <param name="rng">Its own stream, for screams and anything else left to chance.</param>
    /// <summary>The streamed map scrolled: what they remember and where they were heading move with it.</summary>
    public void OnWindowShifted(int dx, int dy, WorldMap map)
    {
        TilePos Moved(TilePos p) => new(Math.Clamp(p.X - dx, 0, map.Width - 1), Math.Clamp(p.Y - dy, 0, map.Height - 1));
        Memory.Shift(dx, dy, map);
        _shelterGoal = Moved(_shelterGoal);
        _personGoal = Moved(_personGoal);
        if (_shelter is { } shelter)
        {
            var moved = new TilePos(shelter.X - dx, shelter.Y - dy);
            _shelter = map.InBounds(moved) ? moved : null; // a shelter left behind is forgotten
            if (_shelter is null) _shelterPlannedTick = NeverPlanned;
        }
    }

    public HumanBrain(Traits traits, AiProfile profile, Rng rng)
    {
        Traits = traits;
        Memory = new ContactMemory(profile);
        _rng = rng;
        _goToShelter = new GoToController(() => _shelterGoal, 0);
        _goToPerson = new GoToController(() => _personGoal, 1);
    }

    public Traits Traits { get; }

    public ContactMemory Memory { get; }

    /// <summary>0-100.</summary>
    public int Fear { get; private set; }

    public bool Panicking { get; private set; }

    public Mood Mood { get; private set; }

    /// <summary>The threat that worries it most, if any.</summary>
    public Contact? Threat { get; private set; }

    /// <summary>Set when it decided not to obey the order it was given.</summary>
    public bool RefusingOrder { get; private set; }

    public ActorAction? Decide(Actor self, Simulation sim)
    {
        long now = sim.CurrentTick;
        long since = _lastThinkTick == long.MinValue ? now - Simulation.NoiseMemoryTicks : _lastThinkTick;
        _lastThinkTick = now;

        Memory.Observe(sim, self, PerceptionSystem.Perceive(sim, self, since));
        Memory.Forget(now);
        UpdateFear(self, now);

        if (Panicking)
            return Panic(self, sim);

        var order = self.Order;
        RefusingOrder = order is not null && Fear >= ShelterFear && Traits.Selfishness > SelfishnessToRefuse;
        if (order is not null && !RefusingOrder)
        {
            switch (order.Order)
            {
                case NpcOrder.Stay:
                    Mood = Mood.Staying;
                    return Still(self);
                case NpcOrder.Follow when FollowAction(self, sim, order) is { } follow:
                    return follow;
                case NpcOrder.Hide:
                    return TakeCover(self, sim, now, hurry: Fear >= HurryFear);
            }
        }

        if (Threat is { } threat && Traits.KnowsMotionVision
            && Chebyshev(self.Position, threat.Estimate) <= FreezeRange)
        {
            Mood = Mood.Frozen;
            return Still(self);
        }

        if (Fear >= ShelterFear)
            return TakeCover(self, sim, now, hurry: Fear >= HurryFear);

        if (HelpAction(self, sim) is { } help)
            return help;

        Mood = Threat is null ? Mood.Calm : Mood.Wary;
        return Still(self);
    }

    /// <summary>A short summary for the HUD, e.g. "hiding (fear 42)".</summary>
    public string Describe()
    {
        string mood = Mood switch
        {
            Mood.SeekingShelter => "running for cover",
            Mood.Panicking => "PANICKING",
            _ => Mood.ToString().ToLowerInvariant(),
        };
        return RefusingOrder ? $"{mood}, ignoring orders (fear {Fear})" : $"{mood} (fear {Fear})";
    }

    /// <summary>
    /// Fear now: the worst threat (confidence × nearness), plus screams heard close by in the last
    /// 3 s, plus 15 per red wound and 10 more if bleeding. It jumps up at once but fades slowly.
    /// Panic starts at the threshold and ends a little below it.
    /// </summary>
    private void UpdateFear(Actor self, long now)
    {
        var profile = Memory.Profile;
        Threat = null;
        int threatFear = 0;
        int screamFear = 0;
        foreach (var contact in Memory.All)
        {
            int distance = Chebyshev(self.Position, contact.Estimate);
            int nearness = Math.Max(0, 100 - distance * 100 / FearRange);
            if (contact.Class == ContactClass.Threat)
            {
                int fear = contact.Confidence(now, profile) * nearness / 100;
                if (fear > threatFear)
                {
                    threatFear = fear;
                    Threat = contact;
                }
            }
            if (contact.LastScreamTick > now - 300 && contact.Key != ContactKey.ForActor(self.Id))
                screamFear = Math.Max(screamFear, ScreamFear * nearness / 100);
        }

        var health = self.Health;
        int woundFear = Enum.GetValues<BodySection>().Count(s => health[s].Severity == Severity.Red) * 15
            + (health.BleedingWounds > 0 ? 10 : 0);

        int target = Math.Min(100, threatFear + screamFear + woundFear);
        int faded = Math.Max(0, _fearPeak - (int)((now - _fearPeakTick) * FearFadePerSecond / Ticks.PerSecond));
        if (target >= faded)
        {
            // Fades from the last time something frightened it this much, so turn length doesn't matter.
            _fearPeak = target;
            _fearPeakTick = now;
        }
        Fear = Math.Max(target, faded);

        if (Fear >= Traits.PanicThreshold)
            Panicking = true;
        else if (Fear < Traits.PanicThreshold - PanicHysteresis)
            Panicking = false;
    }

    /// <summary>Run from the threat at a sprint, screaming now and then. Leaves any vehicle or companion behind.</summary>
    private ActorAction Panic(Actor self, Simulation sim)
    {
        Mood = Mood.Panicking;
        if (_rng.Chance(PanicScreamPercent, 100))
            return new ScreamAction();
        var away = Threat?.Estimate ?? ScreamSource();
        return away is { } danger
            ? FleeStep(self, sim, danger) ?? Still(self)
            : Still(self);
    }

    private TilePos? ScreamSource()
    {
        Contact? latest = null;
        foreach (var contact in Memory.All)
        {
            if (contact.LastScreamTick != long.MinValue && (latest is null || contact.LastScreamTick > latest.LastScreamTick))
                latest = contact;
        }
        return latest?.Estimate;
    }

    /// <summary>
    /// The step that puts the most distance between it and the danger, cover breaking ties; null if
    /// every open step leads closer (cornered).
    /// </summary>
    private static ActorAction? FleeStep(Actor self, Simulation sim, TilePos danger)
    {
        Direction? best = null;
        int bestScore = Chebyshev(self.Position, danger) * 10 - 1;
        foreach (var direction in Directions.All)
        {
            if (!sim.CanMove(self, direction))
                continue;
            var to = self.Position.Offset(direction);
            int score = Chebyshev(to, danger) * 10 + (IsShelter(sim, to) ? 5 : 0);
            if (score > bestScore)
            {
                best = direction;
                bestScore = score;
            }
        }
        return best is { } d ? new MoveAction(d, Sprint: true) : null;
    }

    /// <summary>Under cover: keep still. Otherwise make for the nearest cover away from the threat.</summary>
    private ActorAction TakeCover(Actor self, Simulation sim, long now, bool hurry)
    {
        if (IsShelter(sim, self.Position))
        {
            Mood = Mood.Hiding;
            _shelter = null;
            _shelterPlannedTick = NeverPlanned;
            return Still(self);
        }

        // Plans at most every 10 s (the search isn't cheap), sooner if the cover it chose is gone.
        Mood = Mood.SeekingShelter;
        if (now - _shelterPlannedTick > ShelterReplanTicks || (_shelter is { } goal && !IsShelter(sim, goal)))
        {
            _shelter = FindShelter(self, sim);
            _shelterPlannedTick = now;
        }
        if (_shelter is not { } shelter)
            return Threat is { } threat ? FleeStep(self, sim, threat.Estimate) ?? Still(self) : Still(self);

        _shelterGoal = shelter;
        var action = _goToShelter.Decide(self, sim);
        return action is MoveAction move && hurry ? move with { Sprint = true } : action ?? Still(self);
    }

    /// <summary>
    /// The nearest cover by travel time that isn't right next to the threat: a roofed tile, an
    /// unbroken vehicle, dense jungle. Null if none is reachable within a modest search.
    /// </summary>
    private TilePos? FindShelter(Actor self, Simulation sim)
    {
        var danger = Threat?.Estimate;
        bool Good(TilePos t) => IsShelter(sim, t) && (danger is not { } d || Chebyshev(t, d) >= 6);
        var path = Pathfinder.FindPath(sim, self, Good, self.Position, slack: Math.Max(sim.Map.Width, sim.Map.Height), maxNodes: 6000);
        if (path is null)
            return null;
        return path.Count == 0 ? self.Position : Pathfinder.Trace(self, path)[^1].Anchor;
    }

    /// <summary>Cover a person can hide in: indoors, inside a vehicle that's still whole, or dense jungle.</summary>
    public static bool IsShelter(Simulation sim, TilePos p) =>
        sim.Map.InBounds(p)
        && (sim.Map.IsIndoor(p)
            || sim.VehicleAt(p) is { Breached: false }
            || sim.Cover[p] == CoverType.DenseJungle);

    /// <summary>Keep within a couple of tiles of whoever gave the order (they know where their leader is).</summary>
    private ActorAction? FollowAction(Actor self, Simulation sim, StandingOrder order)
    {
        var leader = sim.Actors[order.FromActorId];
        if (leader.IsGone)
            return null;
        Mood = Mood.Following;
        int distance = Chebyshev(self.Position, leader.Position);
        if (distance <= 2)
            return Still(self);
        _personGoal = leader.Position;
        var action = _goToPerson.Decide(self, sim);
        return action is MoveAction move && distance > 5 ? move with { Sprint = true } : action;
    }

    /// <summary>
    /// The brave go to a companion they can see bleeding and patch them up, if they carry a kit and
    /// aren't too frightened themselves.
    /// </summary>
    private ActorAction? HelpAction(Actor self, Simulation sim)
    {
        if (Traits.Courage < 50 || Fear >= 50 || !self.Inventory.Has(ItemKind.FirstAidKit))
            return null;

        Actor? patient = null;
        int nearest = int.MaxValue;
        foreach (var contact in Memory.All)
        {
            if (contact.Class != ContactClass.Ally || contact.Key.Source != ContactSource.Actor || contact.Cue != "seen")
                continue;
            var ally = sim.Actors[contact.Key.Id];
            int distance = Chebyshev(self.Position, ally.Position);
            if (ally.IsGone || ally.Health.BleedingWounds == 0 || distance > 12 || distance >= nearest)
                continue;
            patient = ally;
            nearest = distance;
        }
        if (patient is null)
            return null;

        Mood = Mood.Helping;
        if (nearest <= 1 && patient.OnDeck == self.OnDeck)
            return new TreatAction(patient.Id);
        _personGoal = patient.Position;
        return _goToPerson.Decide(self, sim) ?? Still(self);
    }

    private static ActorAction Still(Actor self) => new WaitAction(self.Movement.StepTicks);

    private static int Chebyshev(TilePos a, TilePos b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}
