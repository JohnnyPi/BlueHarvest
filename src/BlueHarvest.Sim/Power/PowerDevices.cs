namespace BlueHarvest.Sim.Power;

/// <summary>Where a site's power is coming from right now.</summary>
public enum PowerSource : byte { None, Grid, Generator, Solar, Battery }

public enum GeneratorState : byte
{
    /// <summary>Stopped and ready to start, if it has fuel.</summary>
    Off,
    Running,
    /// <summary>Won't start until someone works on it (a timed repair with a skill roll).</summary>
    NeedsRepair,
    /// <summary>Seized solid: needs parts, which nobody can bring yet.</summary>
    Seized,
}

public enum PlantState : byte
{
    Running,
    /// <summary>Coming up to speed; it feeds the grid when the count reaches zero.</summary>
    Starting,
    /// <summary>Tripped offline: a geothermal turbine restarts in minutes.</summary>
    Tripped,
    /// <summary>In safe shutdown: a reactor takes an hour to bring back.</summary>
    Shutdown,
}

public enum BreakerState : byte { Closed, Open, Tripped }

/// <summary>Something done to a piece of equipment. Which ones make sense depends on the device (see PowerGrid.Choices).</summary>
public enum PowerOp : byte
{
    SiteOn,
    SiteOff,
    /// <summary>Close the breaker of a line (the argument is the feeder id).</summary>
    CloseLine,
    OpenLine,
    BackupAuto,
    BackupManual,
    Start,
    Stop,
    Repair,
    OpenValve,
    CloseValve,
    InverterOn,
    InverterOff,
    Connect,
    Disconnect,
    Restart,
    Shutdown,
    /// <summary>Lock the facility's security doors (they hold while it has power).</summary>
    EngageLocks,
    /// <summary>Release the facility's security doors.</summary>
    ReleaseLocks,
    /// <summary>Ride an elevator or cable car to another stop (the argument is the destination's device id).</summary>
    Ride,
    /// <summary>Fill a carried jerry can from a site's fuel tanks.</summary>
    FillCan,
    /// <summary>Pour a carried jerry can into a generator's day tank.</summary>
    Refuel,
}

/// <summary>What a facility lamp is for.</summary>
public enum LampKind : byte
{
    /// <summary>A ceiling light: on while its facility has power.</summary>
    Room,
    /// <summary>A dim red light on its own battery: on while its facility has no power.</summary>
    Emergency,
    /// <summary>A floodlight on a pole: dusk to dawn, on the facility's power or on its own panel and battery.</summary>
    Flood,
}

/// <summary>What kind of powered door a doorway holds.</summary>
public enum DoorKind : byte
{
    /// <summary>A maglock: locked while its facility has power and its locks are engaged; it fails safe, opening when the power dies.</summary>
    SecurityLock,
    /// <summary>A motorised door: slides open with power; without, it has to be forced, slowly and loudly.</summary>
    Motor,
}

public enum TransportKind : byte { Monorail, CableCar }

/// <summary>A rectangle in island metres (inclusive). Devices are worked from inside it or next to it.</summary>
public readonly record struct Footprint(int X0, int Y0, int X1, int Y1)
{
    public static Footprint Around(int x, int y, int half) => new(x - half, y - half, x + half, y + half);

    public bool Contains(long x, long y) => x >= X0 && x <= X1 && y >= Y0 && y <= Y1;

    /// <summary>Inside, or within <paramref name="margin"/> tiles of an edge.</summary>
    public bool Near(long x, long y, int margin = 1) => x >= X0 - margin && x <= X1 + margin && y >= Y0 - margin && y <= Y1 + margin;

    public (int X, int Y) Centre => ((X0 + X1) / 2, (Y0 + Y1) / 2);
}

/// <summary>A piece of power equipment at a facility (or out on a line), placed in island metres.</summary>
public abstract class PowerDevice
{
    protected PowerDevice(int id, int siteId, string name, Footprint footprint)
    {
        Id = id; SiteId = siteId; Name = name; Footprint = footprint;
    }

    public int Id { get; }

    /// <summary>The site it belongs to; −1 for a fault out on a line.</summary>
    public int SiteId { get; }

    /// <summary>"the backup generator", "the switch panel": lower case, for sentences.</summary>
    public string Name { get; }

    public Footprint Footprint { get; }
}

/// <summary>
/// The site's switchboard: switches the whole site on or off, holds the breakers of the lines that
/// leave the site, and the transfer switch that starts the backup generator when the grid fails.
/// </summary>
public sealed class Panel(int id, int siteId, string name, Footprint footprint) : PowerDevice(id, siteId, name, footprint);

/// <summary>A diesel backup generator with its own day tank, topped up from the yard tanks when their valve is open.</summary>
public sealed class Generator(int id, int siteId, string name, Footprint footprint, int kw, int tankCentilitres)
    : PowerDevice(id, siteId, name, footprint)
{
    public int Kw { get; } = kw;
    public int TankCentilitres { get; } = tankCentilitres;
    public GeneratorState State { get; internal set; }
    public int FuelCentilitres { get; internal set; }
    /// <summary>Started by the transfer switch (it stops it again when the grid comes back), not by hand.</summary>
    public bool AutoStarted { get; internal set; }
}

/// <summary>The yard fuel tanks: they feed the day tanks of the site's generator while the valve is open.</summary>
public sealed class FuelStore(int id, int siteId, string name, Footprint footprint, int capacityLitres)
    : PowerDevice(id, siteId, name, footprint)
{
    public int CapacityLitres { get; } = capacityLitres;
    public int Litres { get; internal set; }
    public bool ValveOpen { get; internal set; }
}

/// <summary>A battery bank with an inverter. Any source charges it; it carries the site when nothing else can.</summary>
public sealed class BatteryBank(int id, int siteId, string name, Footprint footprint, int capacityWh)
    : PowerDevice(id, siteId, name, footprint)
{
    public int CapacityWh { get; } = capacityWh;
    public int ChargeWh { get; internal set; }
    public bool InverterOn { get; internal set; }
    public int ChargePercent => CapacityWh == 0 ? 0 : (int)((long)ChargeWh * 100 / CapacityWh);
}

/// <summary>Solar panels on a roof, worked from their charge controller. Output follows the sun and the cloud.</summary>
public sealed class SolarArray(int id, int siteId, string name, Footprint footprint, int peakW)
    : PowerDevice(id, siteId, name, footprint)
{
    public int PeakW { get; } = peakW;
    /// <summary>How much of its rating ten years of dirt and storms have left it, percent.</summary>
    public int ConditionPercent { get; internal set; } = 100;
    public bool Connected { get; internal set; }
    /// <summary>What it is giving now, in watts.</summary>
    public int OutputW { get; internal set; }
}

/// <summary>A power station's turbine or reactor, worked from its control room. It feeds the grid when running.</summary>
public sealed class Plant(int id, int siteId, string name, Footprint footprint, bool nuclear)
    : PowerDevice(id, siteId, name, footprint)
{
    public bool Nuclear { get; } = nuclear;
    public PlantState State { get; internal set; }
    public int StartMinutesLeft { get; internal set; }
    /// <summary>Game minutes from a restart until it feeds the grid.</summary>
    public int StartMinutes => Nuclear ? 60 : 10;
}

/// <summary>Where an overhead line has come down; worked on the spot. Repairing it puts the line back up.</summary>
public sealed class LineFault(int id, string name, Footprint footprint, int feederId) : PowerDevice(id, -1, name, footprint)
{
    public int FeederId { get; } = feederId;
}

/// <summary>
/// A line between two sites, or from a site to a paddock fence. Its breaker sits at the
/// <see cref="From"/> site's panel. Power flows either way along it (the tie cable between the two
/// plants' networks only makes sense that way).
/// </summary>
public sealed class Feeder(int id, int from, int to, string toName, bool underground)
{
    public int Id { get; } = id;
    public int From { get; } = from;
    /// <summary>The site at the far end, or −1 for a paddock fence.</summary>
    public int To { get; } = to;
    /// <summary>The far end's name: a site, or a paddock ("Tyrannosaur paddock").</summary>
    public string ToName { get; } = toName;
    /// <summary>A buried cable: storms don't trip it and it can't come down.</summary>
    public bool Underground { get; } = underground;
    public BreakerState Breaker { get; internal set; }
    public bool Down { get; internal set; }
    public int? FaultDevice { get; internal set; }
    public bool Carries => !Down && Breaker == BreakerState.Closed;
    public bool ToFence => To < 0;
}

/// <summary>
/// A light at a facility or a station, in island metres. Whether it is on (<see cref="Lit"/>) depends on
/// its kind: room lights follow the power, emergency lights come on without it, floodlights shine from
/// dusk to dawn on the facility's power or (<see cref="Standalone"/>) their own panel and battery.
/// </summary>
public sealed class PowerLamp(int id, int siteId, int x, int y, int heightDm, int radiusTiles, int intensityPercent, LampKind kind, bool inCompound)
{
    public int Id { get; } = id;
    /// <summary>The site whose power it runs on (for a station, the site that powers the station).</summary>
    public int SiteId { get; } = siteId;
    public int X { get; } = x;
    public int Y { get; } = y;
    public int HeightDm { get; } = heightDm;
    public int RadiusTiles { get; } = radiusTiles;
    public int IntensityPercent { get; } = intensityPercent;
    public LampKind Kind { get; } = kind;
    /// <summary>Inside its site's compound (false for transit stations).</summary>
    public bool InCompound { get; } = inCompound;

    /// <summary>A floodlight with its own solar panel and battery, not on the facility's power.</summary>
    public bool Standalone { get; internal set; }
    /// <summary>Broken: the bulb, the panel or the battery is gone. Never lights.</summary>
    public bool Broken { get; internal set; }
    public int ChargeWh { get; internal set; }
    public int CapacityWh { get; internal set; }
    public int PanelW { get; internal set; }
    /// <summary>What a standalone floodlight draws while lit, W.</summary>
    public const int FloodDrawW = 150;

    /// <summary>On now.</summary>
    public bool Lit { get; internal set; }
}

/// <summary>A powered doorway at a facility or a station, in island metres.</summary>
/// <summary>A powered door. A security door's <paramref name="Level"/> is the keycard level that opens it (2 or 3).</summary>
public sealed record PowerDoor(int Id, int SiteId, int X, int Y, DoorKind Kind, string Room, int Level = 2);

/// <summary>A paddock fence line fed by a fence feeder: a closed polyline in island metres.</summary>
public sealed record FenceLine(int FeederId, string Paddock, int[] Poly);

/// <summary>
/// An elevator down to the monorail, or a cable car's cabin hall: worked from inside its room, it
/// takes you to another stop on the same line. It runs on <see cref="PowerDevice.SiteId"/>'s power.
/// </summary>
public sealed class Transport(int id, int siteId, string name, Footprint footprint, TransportKind kind, string line, string stop, int alongM, int shaftM)
    : PowerDevice(id, siteId, name, footprint)
{
    public TransportKind Kind { get; } = kind;
    /// <summary>The line's name: every transport on it can reach every other.</summary>
    public string Line { get; } = line;
    /// <summary>The stop's name ("Station at Genetics complex").</summary>
    public string Stop { get; } = stop;
    /// <summary>How far along the line the stop is, m.</summary>
    public int AlongM { get; } = alongM;
    /// <summary>The elevator shaft from the surface to the platform, m (0 for a cable car).</summary>
    public int ShaftM { get; } = shaftM;
}

/// <summary>A facility on the power network.</summary>
public sealed class PowerSite(int id, string name, string kind, int x, int y, int radius, int loadKw)
{
    public int Id { get; } = id;
    public string Name { get; } = name;
    /// <summary>The facility kind's key ("genetics", "comms"…).</summary>
    public string Kind { get; } = kind;
    public int X { get; } = x;
    public int Y { get; } = y;
    /// <summary>Compound radius, m.</summary>
    public int Radius { get; } = radius;
    /// <summary>What the site draws with everything on, kW.</summary>
    public int LoadKw { get; } = loadKw;

    /// <summary>The main switch: off, nothing in the buildings gets power whatever is running.</summary>
    public bool BoardOn { get; internal set; }
    /// <summary>The transfer switch on auto starts the backup generator when the grid goes, and stops it when it returns.</summary>
    public bool AutoBackup { get; internal set; }
    /// <summary>The security doors' locks are engaged: they hold while the site has power.</summary>
    public bool LocksEngaged { get; internal set; } = true;
    /// <summary>Whether the site has any security doors (the panel offers the lock switch only then).</summary>
    public bool HasSecurityDoors { get; internal set; }

    public int? PanelId { get; internal set; }
    public int? GeneratorId { get; internal set; }
    public int? FuelId { get; internal set; }
    public int? BatteryId { get; internal set; }
    public int? SolarId { get; internal set; }
    public int? PlantId { get; internal set; }

    /// <summary>The grid reaches the site's switchyard.</summary>
    public bool Energised { get; internal set; }
    /// <summary>Some source could carry the site's load now (whether or not its main switch is on).</summary>
    public bool BusLive { get; internal set; }
    /// <summary>The buildings have power: the lights are on.</summary>
    public bool Powered { get; internal set; }
    public PowerSource Source { get; internal set; }

    public bool Contains(long x, long y) => Math.Abs(x - X) <= Radius && Math.Abs(y - Y) <= Radius;
}

/// <summary>One thing that can be done at a device now, for the right-click menu.</summary>
public sealed record PowerChoice(PowerOp Op, int Arg, string Text, string? Hint = null, bool Enabled = true);

/// <summary>What came of working a device: what to tell the player, how long it took, and how loud it was.</summary>
/// <summary>
/// What working a device did. <paramref name="CanLitres"/>: litres into the operator's jerry can
/// (negative: out of it); <paramref name="PartsUsed"/>: sets of spare parts used up.
/// </summary>
public sealed record PowerResult(bool Done, string Message, int Ticks, int Loudness = 0, string NoiseKind = "",
    int CanLitres = 0, int PartsUsed = 0);

/// <summary>
/// What the person working a device has on them that matters to it: a tool kit (repairs go better and
/// faster), spare parts (for a seized generator), a jerry can (litres in it, and how much it holds;
/// no can if <paramref name="CanCapacity"/> is 0), and the monorail access key.
/// </summary>
public sealed record OperatorKit(bool ToolKit = false, int SpareParts = 0, int CanLitres = 0, int CanCapacity = 0, bool MonorailKey = false)
{
    public bool HasCan => CanCapacity > 0;
}

public enum PowerEventKind : byte
{
    SiteLit, SiteDark, GeneratorStarted, GeneratorStopped, GeneratorRanDry, BreakerTripped, PlantRunning, PlantStopped, BatteryFlat,
}

/// <summary>Something that changed on the network by itself as time passed (or right after a switch).</summary>
public sealed record PowerEvent(PowerEventKind Kind, int SiteId, string Text);
