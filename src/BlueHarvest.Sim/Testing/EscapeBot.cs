using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Navigation;
using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim.Testing;

/// <summary>The testbed's escape options, one scripted player per option for batch runs.</summary>
public enum EscapePlan : byte
{
    /// <summary>Sit tight in the jeep and keep still.</summary>
    StayInVehicle,

    /// <summary>Sprint for the jungle trail, deep in the dense jungle the Rex can't enter.</summary>
    RunToJungle,

    /// <summary>Sprint for the floor hatch in the south building, opening its door on the way.</summary>
    RunToBuilding,

    /// <summary>Through the gate to the ledge, rope down the retaining wall, out through the culvert.</summary>
    RappelToCulvert,

    /// <summary>Throw a flare away from the route to draw the Rex, then walk (quietly) to the jungle trail.</summary>
    FlareThenJungle,

    /// <summary>Up the watchtower ladder, 6 m up (above the Rex's reach until it knocks the tower down), and wait.</summary>
    ClimbWatchtower,
}

public static class EscapePlans
{
    public const string JungleExit = "jungle trail";
    public const string BuildingExit = "floor hatch";
    public const string CulvertExit = "culvert";

    public static IReadOnlyList<EscapePlan> All { get; } = Enum.GetValues<EscapePlan>();

    /// <summary>Plain words for reports: "Run to the jungle".</summary>
    public static string Describe(this EscapePlan plan) => plan switch
    {
        EscapePlan.StayInVehicle => "Stay in the vehicle",
        EscapePlan.RunToJungle => "Run to the jungle",
        EscapePlan.RunToBuilding => "Run to the building",
        EscapePlan.RappelToCulvert => "Rappel to the culvert",
        EscapePlan.FlareThenJungle => "Flare, then the jungle",
        EscapePlan.ClimbWatchtower => "Climb the watchtower",
        _ => plan.ToString(),
    };

    /// <summary>Short name for CSV files and the command line: "jungle".</summary>
    public static string Key(this EscapePlan plan) => plan switch
    {
        EscapePlan.StayInVehicle => "vehicle",
        EscapePlan.RunToJungle => "jungle",
        EscapePlan.RunToBuilding => "building",
        EscapePlan.RappelToCulvert => "rappel",
        EscapePlan.FlareThenJungle => "flare",
        EscapePlan.ClimbWatchtower => "tower",
        _ => plan.ToString().ToLowerInvariant(),
    };

    /// <summary>Whether the plan ends at an exit (the others count on hiding until the Rex gives up).</summary>
    public static bool UsesExit(this EscapePlan plan) =>
        plan is not (EscapePlan.StayInVehicle or EscapePlan.ClimbWatchtower);
}

/// <summary>
/// A scripted player for batch runs: follows one escape plan without thinking. It uses the map's
/// layout directly (where the exits, the ledge and the ladder are), as a player who knows the
/// testbed would; for the flare plan it also reads where the Rex is, to know when to throw again.
/// It patches up a bleeding wound when it has a kit. It never perceives or panics: the point is to
/// measure each option, not the bot.
/// </summary>
public sealed class EscapeBot : IActorController
{
    /// <summary>Throw another flare when the Rex is this close and the last throw was this long ago.</summary>
    public const int FlareRethrowTiles = 15;
    public const int FlareRethrowTicks = 1_000;

    private const int MaxTreatTries = 3;

    private readonly EscapePlan _plan;
    private GoToController? _goTo;
    private TilePos _goTarget;
    private bool _rappelled;
    private int _rappelTries;
    private int _treatTries;
    private long _lastThrowTick = long.MinValue / 2;

    public EscapeBot(EscapePlan plan) => _plan = plan;

    public EscapePlan Plan => _plan;

    /// <summary>What it's doing, for logs: "heading for the culvert".</summary>
    public string Status { get; private set; } = "starting";

    public ActorAction? Decide(Actor self, Simulation sim)
    {
        if (self.Health.BleedingWounds > 0 && self.Inventory.Has(ItemKind.FirstAidKit)
            && self.Health.CanTreat && _treatTries < MaxTreatTries)
        {
            _treatTries++;
            Status = "patching up a wound";
            return new TreatAction();
        }

        return _plan switch
        {
            EscapePlan.StayInVehicle => Hold("keeping still in the vehicle"),
            EscapePlan.RunToJungle => ToExit(self, sim, EscapePlans.JungleExit, sprint: true),
            EscapePlan.RunToBuilding => ToExit(self, sim, EscapePlans.BuildingExit, sprint: true),
            EscapePlan.RappelToCulvert => Rappel(self, sim),
            EscapePlan.FlareThenJungle => Flare(self, sim) ?? ToExit(self, sim, EscapePlans.JungleExit, sprint: false),
            EscapePlan.ClimbWatchtower => Tower(self, sim),
            _ => Hold("waiting"),
        };
    }

    private ActorAction Hold(string status)
    {
        Status = status;
        return new WaitAction(100);
    }

    private ActorAction ToExit(Actor self, Simulation sim, string name, bool sprint)
    {
        var exit = sim.Exits.FirstOrDefault(e => e.Name == name);
        if (exit is null)
            return Hold($"no exit called '{name}' on this map");
        Status = $"heading for the {name}";
        return Travel(self, sim, exit.Position, sprint);
    }

    /// <summary>Walk or sprint along a path to a tile (replanning as needed).</summary>
    private ActorAction Travel(Actor self, Simulation sim, TilePos target, bool sprint)
    {
        if (_goTo is null || _goTarget != target)
        {
            _goTo = new GoToController(() => target, tolerance: 0);
            _goTarget = target;
        }
        var action = _goTo.Decide(self, sim) ?? new WaitAction(self.Movement.StepTicks);
        return sprint && action is MoveAction move ? move with { Sprint = true } : action;
    }

    private ActorAction Rappel(Actor self, Simulation sim)
    {
        var exit = sim.Exits.FirstOrDefault(e => e.Name == EscapePlans.CulvertExit);
        if (exit is null)
            return Hold("no culvert on this map");
        // Down once it stands as low as the culvert (the ledge and the track are 8 m higher).
        if (_rappelled || sim.Terrain[self.Position] <= sim.Terrain[exit.Position])
        {
            _rappelled = true;
            Status = "down the wall, heading for the culvert";
            return Travel(self, sim, exit.Position, sprint: true);
        }
        if (RappelPoint(sim, exit.Position) is not { } point)
            return Hold("no place to rope down near the culvert");
        if (self.Position == point.From && !self.OnDeck)
        {
            if (++_rappelTries > 3)
                return ToExit(self, sim, EscapePlans.JungleExit, sprint: true); // can't rope down (no rope, hurt arms): run instead
            Status = "roping down the retaining wall";
            return new RappelAction(point.Direction);
        }
        Status = "heading for the ledge above the culvert";
        return Travel(self, sim, point.From, sprint: true);
    }

    /// <summary>
    /// The ledge tile to rope down from: the one whose landing (one step over the edge) is nearest the
    /// culvert, with a drop more than a safe fall and no more than a rope reaches. Scans a small
    /// square around the exit, in a fixed order, so it's the same every run.
    /// </summary>
    public static (TilePos From, Direction Direction)? RappelPoint(Simulation sim, TilePos exit)
    {
        (TilePos, Direction)? best = null;
        int bestDistance = int.MaxValue;
        for (int y = exit.Y - 6; y <= exit.Y + 6; y++)
        {
            for (int x = exit.X - 6; x <= exit.X + 6; x++)
            {
                var from = new TilePos(x, y);
                if (!sim.Map.InBounds(from) || sim.Map.CoverAt(from).BlocksMovement)
                    continue;
                foreach (var direction in Directions.Cardinal)
                {
                    var to = from.Offset(direction);
                    if (!sim.Map.InBounds(to) || sim.Map.CoverAt(to).BlocksMovement)
                        continue;
                    int drop = sim.Terrain[from] - sim.Terrain[to];
                    if (drop <= Species.Human.Movement.MaxDropDm || drop > GearRules.RopeLengthDm)
                        continue;
                    int distance = Math.Max(Math.Abs(to.X - exit.X), Math.Abs(to.Y - exit.Y));
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = (from, direction);
                    }
                }
            }
        }
        return best;
    }

    /// <summary>
    /// Throws a flare when it's time: the first turn, then again whenever the Rex comes within
    /// 15 tiles, at most every 10 s, while flares last. Null when it isn't time.
    /// </summary>
    private ActorAction? Flare(Actor self, Simulation sim)
    {
        if (!self.Inventory.Has(ItemKind.Flare) || sim.CurrentTick - _lastThrowTick < FlareRethrowTicks)
            return null;
        bool first = _lastThrowTick < 0;
        var rex = sim.Actors.FirstOrDefault(a => a.IsLarge && !a.IsGone);
        bool close = rex is not null
            && Math.Max(Math.Abs(rex.Position.X - self.Position.X), Math.Abs(rex.Position.Y - self.Position.Y)) <= FlareRethrowTiles;
        if (!first && !close)
            return null;
        var exit = sim.Exits.FirstOrDefault(e => e.Name == EscapePlans.JungleExit);
        int range = GearRules.ThrowRange(self.Skills.Throwing, self.Health);
        if (range <= 0)
            return null;
        _lastThrowTick = sim.CurrentTick;
        Status = "throwing a flare away from the route";
        return new ThrowAction(ItemKind.Flare, LureTarget(sim, self.Position, exit?.Position ?? self.Position, range));
    }

    /// <summary>The spot at full throwing range, in whichever of the 8 directions lands farthest from the exit.</summary>
    public static TilePos LureTarget(Simulation sim, TilePos from, TilePos exit, int range)
    {
        var best = from;
        long bestDistance = -1;
        foreach (var direction in Directions.All)
        {
            var (dx, dy) = direction.Delta();
            var target = new TilePos(
                Math.Clamp(from.X + dx * range, 0, sim.Map.Width - 1),
                Math.Clamp(from.Y + dy * range, 0, sim.Map.Height - 1));
            long d2 = (long)(target.X - exit.X) * (target.X - exit.X) + (long)(target.Y - exit.Y) * (target.Y - exit.Y);
            if (d2 > bestDistance)
            {
                bestDistance = d2;
                best = target;
            }
        }
        return best;
    }

    private ActorAction Tower(Actor self, Simulation sim)
    {
        if (self.OnDeck)
            return Hold("up the watchtower, keeping still");
        if (Ladder(sim) is not { } ladder)
            return Hold("no watchtower on this map");
        if (self.Position == ladder)
        {
            Status = "climbing the watchtower ladder";
            return new ClimbAction();
        }
        Status = "heading for the watchtower";
        return Travel(self, sim, ladder, sprint: true);
    }

    /// <summary>The first ladder on the map, scanning rows top to bottom.</summary>
    private static TilePos? Ladder(Simulation sim)
    {
        for (int y = 0; y < sim.Map.Height; y++)
        {
            for (int x = 0; x < sim.Map.Width; x++)
            {
                if (sim.Map.Structures.HasLadder(new TilePos(x, y)))
                    return new TilePos(x, y);
            }
        }
        return null;
    }
}
