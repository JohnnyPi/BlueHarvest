// Generated for P3a from the simulation's events (see claude/contracts.md section 4). One typed payload per
// event: the legacy C# events on Simulation are raised from these, after the request that made them commits.
using BlueHarvest.Content;
using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.Injuries;
using BlueHarvest.Sim.Lighting;
using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.Power;
using BlueHarvest.Sim.World;
using BlueHarvest.Sim.World.Generation;

namespace BlueHarvest.Sim.Events;

/// <summary><c>actor.bite-resolved</c> (<see cref="Simulation.Bit"/>): After every bite, hit or miss.</summary>
public sealed record BitPayload(Actor Biter, BiteResult Result) : SimEventPayload
{
    public const string TypeName = "actor.bite-resolved";

    public override string Type => TypeName;

    public override Actor? Who => Biter;
}

/// <summary><c>actor.bitten</c> (<see cref="Simulation.ActorBitten"/>): For each actor caught in a bite: (biter, victim).</summary>
public sealed record ActorBittenPayload(Actor Biter, Actor Victim) : SimEventPayload
{
    public const string TypeName = "actor.bitten";

    public override string Type => TypeName;

    public override Actor? Who => Biter;

    protected override void AddSubjects(List<string> subjects) { Add(subjects, Victim); }
}

/// <summary><c>actor.blacked-out</c> (<see cref="Simulation.ActorBlackedOut"/>): A concussed actor blacks out for a few seconds.</summary>
public sealed record ActorBlackedOutPayload(Actor Actor) : SimEventPayload
{
    public const string TypeName = "actor.blacked-out";

    public override string Type => TypeName;

    public override Actor? Who => Actor;
}

/// <summary><c>actor.died</c> (<see cref="Simulation.ActorDied"/>): An actor dies: (actor, cause). Its body stays where it fell.</summary>
public sealed record ActorDiedPayload(Actor Actor, string Cause) : SimEventPayload
{
    public const string TypeName = "actor.died";

    public override string Type => TypeName;

    public override Actor? Who => Actor;
}

/// <summary><c>actor.escaped</c> (<see cref="Simulation.ActorEscaped"/>): Someone steps onto an exit and leaves the map.</summary>
public sealed record ActorEscapedPayload(Actor Actor, ExitPoint Exit) : SimEventPayload
{
    public const string TypeName = "actor.escaped";

    public override string Type => TypeName;

    public override Actor? Who => Actor;
}

/// <summary><c>actor.fell</c> (<see cref="Simulation.ActorFell"/>): An actor falls: (actor, height fallen in decimeters).</summary>
public sealed record ActorFellPayload(Actor Actor, int HeightDm) : SimEventPayload
{
    public const string TypeName = "actor.fell";

    public override string Type => TypeName;

    public override Actor? Who => Actor;
}

/// <summary><c>actor.injured</c> (<see cref="Simulation.ActorInjured"/>): A section is wounded: (actor, section, its severity now, cause).</summary>
public sealed record ActorInjuredPayload(Actor Actor, BodySection Section, Severity Severity, string Cause) : SimEventPayload
{
    public const string TypeName = "actor.injured";

    public override string Type => TypeName;

    public override Actor? Who => Actor;
}

/// <summary><c>actor.move-blocked</c> (<see cref="Simulation.MoveBlocked"/>): An actor tries to move and can't, with a short reason ("too steep to climb (2.0 m)").</summary>
public sealed record MoveBlockedPayload(Actor Actor, string Reason) : SimEventPayload
{
    public const string TypeName = "actor.move-blocked";

    public override string Type => TypeName;

    public override Actor? Who => Actor;
}

/// <summary><c>actor.order-given</c> (<see cref="Simulation.OrderGiven"/>): An order is given and heard: (who gave it, who got it, the order).</summary>
public sealed record OrderGivenPayload(Actor From, Actor To, NpcOrder Order) : SimEventPayload
{
    public const string TypeName = "actor.order-given";

    public override string Type => TypeName;

    public override Actor? Who => From;

    protected override void AddSubjects(List<string> subjects) { Add(subjects, To); }
}

/// <summary><c>actor.rappelled</c> (<see cref="Simulation.Rappelled"/>): After a rappel: (actor, whether it slipped and fell part of the way).</summary>
public sealed record RappelledPayload(Actor Actor, bool Slipped) : SimEventPayload
{
    public const string TypeName = "actor.rappelled";

    public override string Type => TypeName;

    public override Actor? Who => Actor;
}

/// <summary><c>actor.screamed</c> (<see cref="Simulation.ActorScreamed"/>): Someone cries out on purpose (panicking): (who).</summary>
public sealed record ActorScreamedPayload(Actor Actor) : SimEventPayload
{
    public const string TypeName = "actor.screamed";

    public override string Type => TypeName;

    public override Actor? Who => Actor;
}

/// <summary><c>actor.struck</c> (<see cref="Simulation.ActorStruck"/>): A large body's head or tail sweeps into another actor: (striker, victim).</summary>
public sealed record ActorStruckPayload(Actor Striker, Actor Victim) : SimEventPayload
{
    public const string TypeName = "actor.struck";

    public override string Type => TypeName;

    public override Actor? Who => Striker;

    protected override void AddSubjects(List<string> subjects) { Add(subjects, Victim); }
}

/// <summary><c>actor.treated</c> (<see cref="Simulation.ActorTreated"/>): After an attempt at first aid: (who gave it, who got it, section treated, whether it worked).</summary>
public sealed record ActorTreatedPayload(Actor Medic, Actor Patient, BodySection Section, bool Worked) : SimEventPayload
{
    public const string TypeName = "actor.treated";

    public override string Type => TypeName;

    public override Actor? Who => Medic;

    protected override void AddSubjects(List<string> subjects) { Add(subjects, Patient); }
}

/// <summary><c>fire.burned</c> (<see cref="Simulation.Burned"/>): Someone was burnt.</summary>
public sealed record BurnedPayload(Actor Actor, Severity Severity) : SimEventPayload
{
    public const string TypeName = "fire.burned";

    public override string Type => TypeName;

    public override Actor? Who => Actor;
}

/// <summary><c>fire.doused</c> (<see cref="Simulation.FireDoused"/>): Someone used an extinguisher, putting out this many burning tiles.</summary>
public sealed record FireDousedPayload(Actor Actor, int TilesPutOut) : SimEventPayload
{
    public const string TypeName = "fire.doused";

    public override string Type => TypeName;

    public override Actor? Who => Actor;
}

/// <summary><c>fire.fires-out</c> (<see cref="Simulation.FiresOut"/>): The last fire on the map went out.</summary>
public sealed record FiresOutPayload() : SimEventPayload
{
    public const string TypeName = "fire.fires-out";

    public override string Type => TypeName;
}

/// <summary><c>fire.fuel-poured</c> (<see cref="Simulation.FuelPoured"/>): Someone poured fuel on these tiles.</summary>
public sealed record FuelPouredPayload(Actor Actor, IReadOnlyList<TilePos> Tiles) : SimEventPayload
{
    public const string TypeName = "fire.fuel-poured";

    public override string Type => TypeName;

    public override Actor? Who => Actor;
}

/// <summary><c>fire.kindled</c> (<see cref="Simulation.Kindled"/>): Someone held a flare to a tile: whether it caught.</summary>
public sealed record KindledPayload(Actor Actor, TilePos At, bool Caught) : SimEventPayload
{
    public const string TypeName = "fire.kindled";

    public override string Type => TypeName;

    public override Actor? Who => Actor;
}

/// <summary><c>fire.lightning-hit-ground</c> (<see cref="Simulation.LightningHitGround"/>): Lightning hit the ground on the map.</summary>
public sealed record LightningHitGroundPayload(TilePos At) : SimEventPayload
{
    public const string TypeName = "fire.lightning-hit-ground";

    public override string Type => TypeName;
}

/// <summary><c>fire.started</c> (<see cref="Simulation.FireStarted"/>): A fire took hold from a source (not by spreading): where, and from what.</summary>
public sealed record FireStartedPayload(TilePos At, string Source) : SimEventPayload
{
    public const string TypeName = "fire.started";

    public override string Type => TypeName;
}

/// <summary><c>fire.tanks-burst</c> (<see cref="Simulation.TanksBurst"/>): A facility's fuel tanks went up.</summary>
public sealed record TanksBurstPayload(TilePos At) : SimEventPayload
{
    public const string TypeName = "fire.tanks-burst";

    public override string Type => TypeName;
}

/// <summary><c>item.container-opened</c> (<see cref="Simulation.ContainerOpened"/>): Someone opened (true) or shut (false) a container.</summary>
public sealed record ContainerOpenedPayload(Actor Actor, Item Container, bool Opened) : SimEventPayload
{
    public const string TypeName = "item.container-opened";

    public override string Type => TypeName;

    public override Actor? Who => Actor;

    protected override void AddSubjects(List<string> subjects) { Add(subjects, Container); }
}

/// <summary><c>item.container-unlocked</c> (<see cref="Simulation.ContainerUnlocked"/>): A locked container was opened with a key or keycard (the third item).</summary>
public sealed record ContainerUnlockedPayload(Actor Actor, Item Container, Item Key) : SimEventPayload
{
    public const string TypeName = "item.container-unlocked";

    public override string Type => TypeName;

    public override Actor? Who => Actor;

    protected override void AddSubjects(List<string> subjects) { Add(subjects, Container); Add(subjects, Key); }
}

/// <summary><c>item.dropped</c> (<see cref="Simulation.ItemDropped"/>): Someone put something down where they stand.</summary>
public sealed record ItemDroppedPayload(Actor Actor, Item Item) : SimEventPayload
{
    public const string TypeName = "item.dropped";

    public override string Type => TypeName;

    public override Actor? Who => Actor;

    protected override void AddSubjects(List<string> subjects) { Add(subjects, Item); }
}

/// <summary><c>item.items-combined</c> (<see cref="Simulation.ItemsCombined"/>): Two carried items went together (moved: rounds loaded; made: what a recipe made).</summary>
public sealed record ItemsCombinedPayload(Actor Actor, Combination Combination, int Moved, Item? Made) : SimEventPayload
{
    public const string TypeName = "item.items-combined";

    public override string Type => TypeName;

    public override Actor? Who => Actor;

    protected override void AddSubjects(List<string> subjects) { Add(subjects, Made); }
}

/// <summary><c>item.light-died</c> (<see cref="Simulation.LightDied"/>): An actor's flashlight died: its batteries ran flat (or were taken out) while it was on.</summary>
public sealed record LightDiedPayload(Actor Actor) : SimEventPayload
{
    public const string TypeName = "item.light-died";

    public override string Type => TypeName;

    public override Actor? Who => Actor;
}

/// <summary><c>item.lock-forced</c> (<see cref="Simulation.LockForced"/>): A locked container was forced open.</summary>
public sealed record LockForcedPayload(Actor Actor, Item Container) : SimEventPayload
{
    public const string TypeName = "item.lock-forced";

    public override string Type => TypeName;

    public override Actor? Who => Actor;

    protected override void AddSubjects(List<string> subjects) { Add(subjects, Container); }
}

/// <summary><c>item.picked-up</c> (<see cref="Simulation.ItemPickedUp"/>): Someone picked something up (from the ground, a container or a body).</summary>
public sealed record ItemPickedUpPayload(Actor Actor, Item Item) : SimEventPayload
{
    public const string TypeName = "item.picked-up";

    public override string Type => TypeName;

    public override Actor? Who => Actor;

    protected override void AddSubjects(List<string> subjects) { Add(subjects, Item); }
}

/// <summary><c>item.stowed</c> (<see cref="Simulation.ItemStowed"/>): Someone put something into a container (the second item).</summary>
public sealed record ItemStowedPayload(Actor Actor, Item Item, Item Container) : SimEventPayload
{
    public const string TypeName = "item.stowed";

    public override string Type => TypeName;

    public override Actor? Who => Actor;

    protected override void AddSubjects(List<string> subjects) { Add(subjects, Item); Add(subjects, Container); }
}

/// <summary><c>item.taken-out</c> (<see cref="Simulation.ItemTakenOut"/>): Something was taken out of what it was in (the second item), or rounds unloaded from it.</summary>
public sealed record ItemTakenOutPayload(Actor Actor, Item Item, Item From) : SimEventPayload
{
    public const string TypeName = "item.taken-out";

    public override string Type => TypeName;

    public override Actor? Who => Actor;

    protected override void AddSubjects(List<string> subjects) { Add(subjects, Item); Add(subjects, From); }
}

/// <summary><c>item.thrown</c> (<see cref="Simulation.ItemThrown"/>): Something is thrown: (thrower, what, where it landed).</summary>
public sealed record ItemThrownPayload(Actor Actor, ContentId Kind, TilePos LandedAt) : SimEventPayload
{
    public const string TypeName = "item.thrown";

    public override string Type => TypeName;

    public override Actor? Who => Actor;
}

/// <summary><c>item.used</c> (<see cref="Simulation.ItemUsed"/>): Something carried was used: eaten, drunk, applied, read; the amount it used up.</summary>
public sealed record ItemUsedPayload(Actor Actor, Item Item, ItemUse Use, int UsedUp) : SimEventPayload
{
    public const string TypeName = "item.used";

    public override string Type => TypeName;

    public override Actor? Who => Actor;

    protected override void AddSubjects(List<string> subjects) { Add(subjects, Item); }
}

/// <summary><c>knowledge.feature-found</c> (<see cref="Simulation.FeatureFound"/>): A survivor finds a ruin, a wreck or a cave they didn't know of.</summary>
public sealed record FeatureFoundPayload(Actor Actor, FoundFeature Feature) : SimEventPayload
{
    public const string TypeName = "knowledge.feature-found";

    public override string Type => TypeName;

    public override Actor? Who => Actor;
}

/// <summary><c>knowledge.map-charted</c> (<see cref="Simulation.MapCharted"/>): A survivor with map knowledge picks up a park map: the sheet it charts, and whether that sheet is new to them (false: they'd charted it already, and it adds nothing).</summary>
public sealed record MapChartedPayload(Actor Actor, Item Map, ChartedSheet Sheet, bool IsNew) : SimEventPayload
{
    public const string TypeName = "knowledge.map-charted";

    public override string Type => TypeName;

    public override Actor? Who => Actor;

    protected override void AddSubjects(List<string> subjects) { Add(subjects, Map); }
}

/// <summary><c>knowledge.park-map-read</c> (<see cref="Simulation.ParkMapRead"/>): On a survivor's first read of a park map, with what it turned up: a wreck, ruin or cave marked on it (found "from a map"), or null. Later reads raise nothing.</summary>
public sealed record ParkMapReadPayload(Actor Actor, Item Map, FoundFeature? Feature) : SimEventPayload
{
    public const string TypeName = "knowledge.park-map-read";

    public override string Type => TypeName;

    public override Actor? Who => Actor;

    protected override void AddSubjects(List<string> subjects) { Add(subjects, Map); }
}

/// <summary><c>knowledge.site-found</c> (<see cref="Simulation.SiteFound"/>): The first time a survivor enters a site (an index into the island's sites).</summary>
public sealed record SiteFoundPayload(Actor Actor, int Site) : SimEventPayload
{
    public const string TypeName = "knowledge.site-found";

    public override string Type => TypeName;

    public override Actor? Who => Actor;
}

/// <summary><c>light.item-died</c> (<see cref="Simulation.ItemLightDied"/>): A lit item went out by itself: batteries flat, glow faded, torch burnt down (the actor holding it, if anyone).</summary>
public sealed record ItemLightDiedPayload(Actor? Holder, Item Light) : SimEventPayload
{
    public const string TypeName = "light.item-died";

    public override string Type => TypeName;

    public override Actor? Who => Holder;

    protected override void AddSubjects(List<string> subjects) { Add(subjects, Light); }
}

/// <summary><c>light.item-switched</c> (<see cref="Simulation.ItemLightSwitched"/>): Someone switched a light on or off, cracked a glow stick or lit a torch (true: it's lit now).</summary>
public sealed record ItemLightSwitchedPayload(Actor Actor, Item Light, bool Lit) : SimEventPayload
{
    public const string TypeName = "light.item-switched";

    public override string Type => TypeName;

    public override Actor? Who => Actor;

    protected override void AddSubjects(List<string> subjects) { Add(subjects, Light); }
}

/// <summary><c>needs.ate-kill</c> (<see cref="Simulation.AteKill"/>): Someone with needs ate a kill (the creature, its kill).</summary>
public sealed record AteKillPayload(Actor Actor, Actor Kill) : SimEventPayload
{
    public const string TypeName = "needs.ate-kill";

    public override string Type => TypeName;

    public override Actor? Who => Actor;

    protected override void AddSubjects(List<string> subjects) { Add(subjects, Kill); }
}

/// <summary><c>needs.blacked-out</c> (<see cref="Simulation.NeedBlackedOut"/>): Someone blacked out from thirst or exhaustion.</summary>
public sealed record NeedBlackedOutPayload(Actor Actor, NeedKind Need) : SimEventPayload
{
    public const string TypeName = "needs.blacked-out";

    public override string Type => TypeName;

    public override Actor? Who => Actor;
}

/// <summary><c>needs.changed</c> (<see cref="Simulation.NeedChanged"/>): A need moved to another stage (actor, need, new stage, old stage).</summary>
public sealed record NeedChangedPayload(Actor Actor, NeedKind Need, int Stage, int WasStage) : SimEventPayload
{
    public const string TypeName = "needs.changed";

    public override string Type => TypeName;

    public override Actor? Who => Actor;
}

/// <summary><c>needs.fell-asleep</c> (<see cref="Simulation.FellAsleep"/>): Someone fell asleep.</summary>
public sealed record FellAsleepPayload(Actor Actor) : SimEventPayload
{
    public const string TypeName = "needs.fell-asleep";

    public override string Type => TypeName;

    public override Actor? Who => Actor;
}

/// <summary><c>needs.woke-up</c> (<see cref="Simulation.WokeUp"/>): Someone woke up by themselves (why: "rested", "a noise: footsteps", "pain", "thirst", "hunger").</summary>
public sealed record WokeUpPayload(Actor Actor, string Reason) : SimEventPayload
{
    public const string TypeName = "needs.woke-up";

    public override string Type => TypeName;

    public override Actor? Who => Actor;
}

/// <summary><c>power.door-forced</c> (<see cref="Simulation.DoorForced"/>): Someone forced a motorised door that had no power.</summary>
public sealed record DoorForcedPayload(Actor Actor, TilePos At) : SimEventPayload
{
    public const string TypeName = "power.door-forced";

    public override string Type => TypeName;

    public override Actor? Who => Actor;
}

/// <summary><c>power.keycard-used</c> (<see cref="Simulation.KeycardUsed"/>): Someone's keycard opened a security door (at the tile).</summary>
public sealed record KeycardUsedPayload(Actor Actor, Item Card, TilePos At) : SimEventPayload
{
    public const string TypeName = "power.keycard-used";

    public override string Type => TypeName;

    public override Actor? Who => Actor;

    protected override void AddSubjects(List<string> subjects) { Add(subjects, Card); }
}

/// <summary><c>power.power-changed</c> (<see cref="Simulation.PowerChanged"/>): Something on the network changed by itself, or right after a switch was thrown.</summary>
public sealed record PowerChangedPayload(PowerEvent Change) : SimEventPayload
{
    public const string TypeName = "power.power-changed";

    public override string Type => TypeName;
}

/// <summary><c>power.power-operated</c> (<see cref="Simulation.PowerOperated"/>): Someone worked a piece of equipment: who, which, and what came of it.</summary>
public sealed record PowerOperatedPayload(Actor Actor, PowerDevice Device, PowerResult Result) : SimEventPayload
{
    public const string TypeName = "power.power-operated";

    public override string Type => TypeName;

    public override Actor? Who => Actor;
}

/// <summary><c>power.ride-ended</c> (<see cref="Simulation.RideEnded"/>): Someone got out of an elevator or a cable car at the end of a ride.</summary>
public sealed record RideEndedPayload(Actor Actor) : SimEventPayload
{
    public const string TypeName = "power.ride-ended";

    public override string Type => TypeName;

    public override Actor? Who => Actor;
}

/// <summary><c>power.shocked</c> (<see cref="Simulation.Shocked"/>): Someone (or something) touched a live fence at this tile.</summary>
public sealed record ShockedPayload(Actor Actor, TilePos At) : SimEventPayload
{
    public const string TypeName = "power.shocked";

    public override string Type => TypeName;

    public override Actor? Who => Actor;
}

/// <summary><c>power.sparks-died</c> (<see cref="Simulation.SparksDied"/>): A sparking fence goes dead because its section was broken.</summary>
public sealed record SparksDiedPayload(SparkingFence Fence) : SimEventPayload
{
    public const string TypeName = "power.sparks-died";

    public override string Type => TypeName;
}

/// <summary><c>room.actor-entered-room</c> (<see cref="Simulation.ActorEnteredRoom"/>): After an actor's move takes it into a room, with the room. Not raised for an actor's first placing.</summary>
public sealed record ActorEnteredRoomPayload(Actor Actor, IndexedRoom Room) : SimEventPayload
{
    public const string TypeName = "room.actor-entered-room";

    public override string Type => TypeName;

    public override Actor? Who => Actor;
}

/// <summary><c>room.actor-left-room</c> (<see cref="Simulation.ActorLeftRoom"/>): After an actor's move takes it out of a room, with the room it left: into a corridor, outdoors, or (just before <see cref="ActorEnteredRoom"/>) straight into the next room.</summary>
public sealed record ActorLeftRoomPayload(Actor Actor, IndexedRoom Room) : SimEventPayload
{
    public const string TypeName = "room.actor-left-room";

    public override string Type => TypeName;

    public override Actor? Who => Actor;
}

/// <summary><c>structure.cover-broken</c> (<see cref="Simulation.CoverBroken"/>): Cover is smashed or bitten through: (tile, what it was, who did it).</summary>
public sealed record CoverBrokenPayload(TilePos At, CoverType Cover, Actor? By) : SimEventPayload
{
    public const string TypeName = "structure.cover-broken";

    public override string Type => TypeName;

    public override Actor? Who => By;
}

/// <summary><c>structure.platform-collapsed</c> (<see cref="Simulation.PlatformCollapsed"/>): A platform comes down: (platform, who smashed its last support, who was on it).</summary>
public sealed record PlatformCollapsedPayload(Platform Platform, Actor? By, IReadOnlyList<Actor> Riders) : SimEventPayload
{
    public const string TypeName = "structure.platform-collapsed";

    public override string Type => TypeName;

    public override Actor? Who => By;

    protected override void AddSubjects(List<string> subjects) { Add(subjects, Riders); }
}

/// <summary><c>vehicle.damaged</c> (<see cref="Simulation.VehicleDamaged"/>): A vehicle loses integrity: (vehicle, by whom, how much).</summary>
public sealed record VehicleDamagedPayload(Vehicle Vehicle, Actor By, int Damage) : SimEventPayload
{
    public const string TypeName = "vehicle.damaged";

    public override string Type => TypeName;

    public override Actor? Who => By;
}

/// <summary><c>vehicle.shoved</c> (<see cref="Simulation.VehicleShoved"/>): A vehicle is shoved a tile: (vehicle, by whom).</summary>
public sealed record VehicleShovedPayload(Vehicle Vehicle, Actor By) : SimEventPayload
{
    public const string TypeName = "vehicle.shoved";

    public override string Type => TypeName;

    public override Actor? Who => By;
}

/// <summary><c>water.changed</c> (<see cref="Simulation.WaterChanged"/>): High and low water, and rivers on the map rising, flooding and falling.</summary>
public sealed record WaterChangedPayload(WaterEvent Change) : SimEventPayload
{
    public const string TypeName = "water.changed";

    public override string Type => TypeName;
}

/// <summary><c>water.drank-water</c> (<see cref="Simulation.DrankWater"/>): Someone drank from a stream or pool (at the tile).</summary>
public sealed record DrankWaterPayload(Actor Actor, TilePos At) : SimEventPayload
{
    public const string TypeName = "water.drank-water";

    public override string Type => TypeName;

    public override Actor? Who => Actor;
}

/// <summary><c>water.filled</c> (<see cref="Simulation.WaterFilled"/>): Someone filled a bottle or canteen from a stream or pool.</summary>
public sealed record WaterFilledPayload(Actor Actor, Item Container) : SimEventPayload
{
    public const string TypeName = "water.filled";

    public override string Type => TypeName;

    public override Actor? Who => Actor;

    protected override void AddSubjects(List<string> subjects) { Add(subjects, Container); }
}

/// <summary><c>water.moved</c> (<see cref="Simulation.WaterMoved"/>): The tide, a flood or a fall changed tiles on the map: the front end redraws its water.</summary>
public sealed record WaterMovedPayload() : SimEventPayload
{
    public const string TypeName = "water.moved";

    public override string Type => TypeName;
}

/// <summary><c>water.swept</c> (<see cref="Simulation.Swept"/>): Someone was carried a tile downstream by the current.</summary>
public sealed record SweptPayload(Actor Actor, TilePos To) : SimEventPayload
{
    public const string TypeName = "water.swept";

    public override string Type => TypeName;

    public override Actor? Who => Actor;
}

/// <summary><c>weather.changed</c> (<see cref="Simulation.WeatherChanged"/>): The weather is set.</summary>
public sealed record WeatherChangedPayload(WeatherSettings Settings) : SimEventPayload
{
    public const string TypeName = "weather.changed";

    public override string Type => TypeName;
}

/// <summary><c>weather.lightning-struck</c> (<see cref="Simulation.LightningStruck"/>): As time passes a lightning strike: the flash is now; its thunder follows.</summary>
public sealed record LightningStruckPayload(LightningStrike Strike) : SimEventPayload
{
    public const string TypeName = "weather.lightning-struck";

    public override string Type => TypeName;
}

/// <summary><c>window.window-shifted</c> (<see cref="Simulation.WindowShifted"/>): After <see cref="ShiftWindow"/>, with how far the map scrolled in tiles, so the front end can move its camera and whatever it remembers about tiles (fog of war) by the same amount.</summary>
public sealed record WindowShiftedPayload(int Dx, int Dy) : SimEventPayload
{
    public const string TypeName = "window.window-shifted";

    public override string Type => TypeName;
}
