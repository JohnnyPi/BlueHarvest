using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Power;
using BlueHarvest.Sim.World;
using Xunit;

namespace BlueHarvest.Sim.Tests;

/// <summary>Step 21: items in the power and fire systems: the jerry can, the tool kit, spare parts, keycards, the lighter, the extinguisher.</summary>
public class ItemSystemsTests
{
    private sealed class Once(ActorAction action) : IActorController
    {
        private bool _done;

        public ActorAction? Decide(Actor self, Simulation sim)
        {
            if (_done) return new WaitAction(100);
            _done = true;
            return action;
        }
    }

    /// <summary>One powered compound at the map's corner: a turbine, a switch panel, a generator and its fuel tanks.</summary>
    private sealed class Compound
    {
        public readonly PowerGrid Grid = new(seed: 4);
        public readonly PowerSite Site;
        public readonly Generator Generator;
        public readonly FuelStore Fuel;

        public Compound()
        {
            Site = Grid.AddSite("test compound", "genetics", 20, 20, 30, 50);
            Grid.AddDevice(new Panel(Grid.NextDeviceId, Site.Id, "the switch panel", Footprint.Around(30, 5, 1)));
            Plant plant = Grid.AddDevice(new Plant(Grid.NextDeviceId, Site.Id, "the turbine controls", Footprint.Around(35, 5, 1), nuclear: false));
            Generator = Grid.AddDevice(new Generator(Grid.NextDeviceId, Site.Id, "the backup generator", Footprint.Around(5, 5, 1), kw: 48, tankCentilitres: 10_000));
            Fuel = Grid.AddDevice(new FuelStore(Grid.NextDeviceId, Site.Id, "the fuel tanks", Footprint.Around(10, 5, 1), capacityLitres: 1000));
            PowerGrid.SetGenerator(Generator, GeneratorState.Off, 5000);
            PowerGrid.SetFuel(Fuel, 1000, valveOpen: false);
            PowerGrid.SetBoard(Site, on: true, autoBackup: false);
            PowerGrid.SetPlant(plant, PlantState.Running);
            Grid.Settle(100, (_, _) => WeatherSettings.Clear);
        }
    }

    private static readonly OperatorKit EmptyCan = new(CanLitres: 0, CanCapacity: 20);

    // ---------- the jerry can at the grid ----------

    [Fact]
    public void AJerryCan_FillsAtTheTanks_AndRefuelsAGenerator()
    {
        var c = new Compound();
        Assert.Contains(c.Grid.Choices(c.Fuel.Id, EmptyCan), x => x.Op == PowerOp.FillCan && x.Enabled);
        Assert.DoesNotContain(c.Grid.Choices(c.Fuel.Id), x => x.Op == PowerOp.FillCan); // no kit: no can to fill

        PowerResult fill = c.Grid.Operate(c.Fuel.Id, PowerOp.FillCan, -1, 0, 0, EmptyCan);
        Assert.True(fill.Done);
        Assert.Equal(20, fill.CanLitres);
        Assert.Equal(980, c.Fuel.Litres);
        Assert.Equal(600, fill.Ticks); // 300 ticks for each 10 L

        var full = new OperatorKit(CanLitres: 20, CanCapacity: 20);
        PowerResult refuel = c.Grid.Operate(c.Generator.Id, PowerOp.Refuel, -1, 0, 0, full);
        Assert.True(refuel.Done);
        Assert.Equal(-20, refuel.CanLitres);
        Assert.Equal(7000, c.Generator.FuelCentilitres);
        Assert.False(c.Grid.Operate(c.Generator.Id, PowerOp.Refuel, -1, 0, 0, EmptyCan).Done);
    }

    [Fact]
    public void ARefuel_StopsWhenTheDayTankIsFull()
    {
        var c = new Compound();
        PowerGrid.SetGenerator(c.Generator, GeneratorState.Off, 9500); // room for 5 L
        PowerResult refuel = c.Grid.Operate(c.Generator.Id, PowerOp.Refuel, -1, 0, 0, new OperatorKit(CanLitres: 20, CanCapacity: 20));
        Assert.Equal(-5, refuel.CanLitres);
        Assert.Equal(10_000, c.Generator.FuelCentilitres);
    }

    // ---------- repairs ----------

    [Fact]
    public void AToolKit_MakesRepairsLikelierAndQuicker()
    {
        var kit = new OperatorKit(ToolKit: true);
        Assert.Equal(65, PowerGrid.RepairChanceWith(0, kit));
        Assert.Equal(45, PowerGrid.RepairChanceWith(0, null));
        Assert.Equal(95, PowerGrid.RepairChanceWith(3, kit)); // capped
        Assert.Equal(PowerGrid.RepairTicks * 2 / 3, PowerGrid.RepairTicksWith(PowerGrid.RepairTicks, kit));

        var c = new Compound();
        PowerGrid.SetGenerator(c.Generator, GeneratorState.NeedsRepair, 5000);
        Assert.False(c.Grid.Operate(c.Generator.Id, PowerOp.Repair, -1, 0, roll: 50).Done); // 50 isn't under 45
        PowerResult withKit = c.Grid.Operate(c.Generator.Id, PowerOp.Repair, -1, 0, roll: 50, kit);
        Assert.True(withKit.Done);
        Assert.Equal(PowerGrid.RepairTicks * 2 / 3, withKit.Ticks);
        Assert.Equal(GeneratorState.Off, c.Generator.State);
    }

    [Fact]
    public void SpareParts_FreeASeizedGenerator_ForRepair()
    {
        var c = new Compound();
        PowerGrid.SetGenerator(c.Generator, GeneratorState.Seized, 5000);
        Assert.False(Assert.Single(c.Grid.Choices(c.Generator.Id, new OperatorKit()), x => x.Op == PowerOp.Repair).Enabled);
        Assert.False(c.Grid.Operate(c.Generator.Id, PowerOp.Repair, -1, 0, 0, new OperatorKit()).Done);

        var parts = new OperatorKit(SpareParts: 1);
        Assert.True(Assert.Single(c.Grid.Choices(c.Generator.Id, parts), x => x.Op == PowerOp.Repair).Enabled);
        PowerResult fitted = c.Grid.Operate(c.Generator.Id, PowerOp.Repair, -1, 0, 0, parts);
        Assert.True(fitted.Done);
        Assert.Equal(1, fitted.PartsUsed);
        Assert.Equal(GeneratorState.NeedsRepair, c.Generator.State);
    }

    // ---------- the monorail key ----------

    [Fact]
    public void TheMonorail_WantsItsAccessKey()
    {
        var c = new Compound();
        var a = c.Grid.AddDevice(new Transport(c.Grid.NextDeviceId, c.Site.Id, "the elevator", new Footprint(2, 30, 8, 36),
            TransportKind.Monorail, "Underground monorail", "Station A", alongM: 0, shaftM: 10));
        var b = c.Grid.AddDevice(new Transport(c.Grid.NextDeviceId, c.Site.Id, "the elevator", new Footprint(30, 30, 36, 36),
            TransportKind.Monorail, "Underground monorail", "Station B", alongM: 600, shaftM: 10));
        c.Grid.Settle(100, (_, _) => WeatherSettings.Clear);

        Assert.Null(c.Grid.RideBlock(a, b));
        Assert.Contains("access key", c.Grid.RideBlock(a, b, hasKey: false)!);
        Assert.False(Assert.Single(c.Grid.Choices(a.Id, new OperatorKit()), x => x.Op == PowerOp.Ride).Enabled);
        Assert.False(c.Grid.Operate(a.Id, PowerOp.Ride, b.Id, 0, 0, new OperatorKit()).Done);
        Assert.True(c.Grid.Operate(a.Id, PowerOp.Ride, b.Id, 0, 0, new OperatorKit(MonorailKey: true)).Done);
    }

    // ---------- in the simulation ----------

    private static (Simulation Sim, Compound C, PlayerController Drive, Actor You) AtTheCompound(TilePos at)
    {
        var c = new Compound();
        var sim = new Simulation(new WorldMap(40, 40), seed: 2);
        sim.SetTimeOfDay(12);
        sim.UsePower(c.Grid, 0, 0);
        var drive = new PlayerController();
        var you = sim.AddActor("You", '@', at, Species.Human, drive, Direction.East);
        sim.RunUntilInputNeeded();
        return (sim, c, drive, you);
    }

    private static void Do(Simulation sim, PlayerController drive, ActorAction action)
    {
        drive.Pending = action;
        sim.RunUntilInputNeeded();
    }

    [Fact]
    public void InTheSim_TheCanFills_AndEmptiesIntoTheGenerator_AndPartsAreUsedUp()
    {
        var (sim, c, drive, you) = AtTheCompound(new TilePos(10, 5)); // at the tanks
        var can = sim.CreateItem(ItemKind.JerryCan, amount: 0);
        Assert.True(sim.GiveItem(you, can));
        Assert.Contains(sim.PowerChoices(you, c.Fuel.Id), x => x.Op == PowerOp.FillCan && x.Enabled);

        Do(sim, drive, new OperateAction(c.Fuel.Id, PowerOp.FillCan));
        Assert.Equal(20, can.Amount);
        Assert.Equal(980, c.Fuel.Litres);

        // walk over to the generator (5 tiles west) and pour it in
        for (int i = 0; i < 5; i++)
            Do(sim, drive, new MoveAction(Direction.West));
        Do(sim, drive, new OperateAction(c.Generator.Id, PowerOp.Refuel));
        Assert.Equal(0, can.Amount);
        Assert.Equal(7000, c.Generator.FuelCentilitres);

        PowerGrid.SetGenerator(c.Generator, GeneratorState.Seized, 7000);
        Assert.True(sim.GiveItem(you, sim.CreateItem(ItemKind.SpareParts)));
        Do(sim, drive, new OperateAction(c.Generator.Id, PowerOp.Repair));
        Assert.Equal(GeneratorState.NeedsRepair, c.Generator.State);
        Assert.False(you.Inventory.Has(ItemKind.SpareParts));
    }

    /// <summary>A wall along row 10 with a security door at (10,10), the compound's power on and its locks engaged.</summary>
    private static (Simulation Sim, PlayerController Drive, Actor You, TilePos Door) AtASecurityDoor(int level)
    {
        var c = new Compound();
        var map = new WorldMap(40, 40);
        for (int x = 0; x < 40; x++)
            map.Cover[new TilePos(x, 10)] = CoverType.Wall;
        var door = new TilePos(10, 10);
        map.Cover[door] = CoverType.Door;
        map.Structures.SetDoor(door, DoorState.Closed);
        c.Grid.AddDoor(c.Site.Id, door.X, door.Y, DoorKind.SecurityLock, "Server hall", level);
        c.Grid.Settle(100, (_, _) => WeatherSettings.Clear);
        var sim = new Simulation(map, seed: 2);
        sim.SetTimeOfDay(12);
        sim.UsePower(c.Grid, 0, 0);
        var drive = new PlayerController();
        var you = sim.AddActor("You", '@', new TilePos(10, 11), Species.Human, drive, Direction.North);
        sim.RunUntilInputNeeded();
        Assert.Equal(DoorState.Locked, sim.Map.Structures.DoorAt(door));
        return (sim, drive, you, door);
    }

    [Fact]
    public void AKeycard_OpensItsFacilitysSecurityDoor_AtItsLevel()
    {
        var (sim, drive, you, door) = AtASecurityDoor(level: 2);
        Do(sim, drive, new MoveAction(Direction.North));
        Assert.Equal(DoorState.Locked, sim.Map.Structures.DoorAt(door)); // no card

        Assert.True(sim.GiveItem(you, sim.CreateItem(ItemKind.Keycard, tag: "another compound", level: 3)));
        Do(sim, drive, new MoveAction(Direction.North));
        Assert.Equal(DoorState.Locked, sim.Map.Structures.DoorAt(door)); // someone else's card

        Assert.True(sim.GiveItem(you, sim.CreateItem(ItemKind.Keycard, tag: "test compound", level: 2)));
        var used = new List<Item>();
        sim.KeycardUsed += (_, card, _) => used.Add(card);
        Do(sim, drive, new MoveAction(Direction.North));
        Assert.Equal(DoorState.Open, sim.Map.Structures.DoorAt(door));
        Assert.Single(used);
        Do(sim, drive, new MoveAction(Direction.North));
        Assert.Equal(door, you.Position); // through
    }

    [Fact]
    public void ALevelTwoCard_DoesntOpenALevelThreeDoor()
    {
        var (sim, drive, you, door) = AtASecurityDoor(level: 3);
        Assert.True(sim.GiveItem(you, sim.CreateItem(ItemKind.Keycard, tag: "test compound", level: 2)));
        Do(sim, drive, new MoveAction(Direction.North));
        Assert.Equal(DoorState.Locked, sim.Map.Structures.DoorAt(door));
    }

    [Fact]
    public void WithoutTheKey_TheMonorailWontGo()
    {
        var c = new Compound();
        var from = c.Grid.AddDevice(new Transport(c.Grid.NextDeviceId, c.Site.Id, "the elevator", new Footprint(2, 2, 8, 8),
            TransportKind.Monorail, "Underground monorail", "Station A", alongM: 0, shaftM: 10));
        var to = c.Grid.AddDevice(new Transport(c.Grid.NextDeviceId, c.Site.Id, "the elevator", new Footprint(30, 30, 36, 36),
            TransportKind.Monorail, "Underground monorail", "Station B", alongM: 600, shaftM: 10));
        c.Grid.Settle(100, (_, _) => WeatherSettings.Clear);
        var sim = new Simulation(new WorldMap(40, 40), seed: 1);
        sim.SetTimeOfDay(12);
        sim.UsePower(c.Grid, 0, 0);
        var results = new List<PowerResult>();
        sim.PowerOperated += (_, _, r) => results.Add(r);
        var you = sim.AddActor("You", '@', new TilePos(4, 4), Species.Human, new Once(new OperateAction(from.Id, PowerOp.Ride, to.Id)));
        sim.RunUntil(sim.CurrentTick + 1000);
        PowerResult refused = Assert.Single(results);
        Assert.False(refused.Done);
        Assert.Contains("access key", refused.Message);
        Assert.Null(you.PendingRide);
    }

    // ---------- fire ----------

    /// <summary>A calm, dry lawn; with <paramref name="floorBut"/>, bare floor (which won't burn) everywhere but those tiles.</summary>
    private static (Simulation Sim, PlayerController Drive, Actor You) OnGrass(IReadOnlyCollection<TilePos>? floorBut = null)
    {
        var map = new WorldMap(20, 20);
        if (floorBut is not null)
            for (int y = 0; y < 20; y++)
                for (int x = 0; x < 20; x++)
                    if (!floorBut.Contains(new TilePos(x, y)))
                        map.Cover[new TilePos(x, y)] = CoverType.Floor;
        var sim = new Simulation(map, seed: 3);
        sim.SetWeather(WeatherSettings.Clear);
        var drive = new PlayerController();
        var you = sim.AddActor("You", '@', new TilePos(5, 5), Species.Human, drive, Direction.East);
        sim.RunUntilInputNeeded();
        return (sim, drive, you);
    }

    [Fact]
    public void ALighter_LightsFuel_ButNotALawn_AndUsesItself()
    {
        var (sim, drive, you) = OnGrass();
        var lighter = sim.CreateItem(ItemKind.Lighter);
        Assert.True(sim.GiveItem(you, lighter));
        Assert.Equal(CoverType.Grass, sim.Cover[new TilePos(6, 5)]);

        Do(sim, drive, new IgniteAction(Direction.East, ItemKind.Lighter));
        Assert.Null(sim.FireAt(new TilePos(6, 5))); // 30 + 40 is nowhere near
        sim.SpillFuel(new TilePos(6, 5));
        Do(sim, drive, new IgniteAction(Direction.East, ItemKind.Lighter));

        Assert.NotNull(sim.FireAt(new TilePos(6, 5)));
        Assert.Equal(38, lighter.Amount);
        Assert.Empty(sim.GroundFlares); // a flame, not a flare
    }

    [Fact]
    public void AnExtinguisher_PutsOutItsCone_AndTheGroundWontCatchForAWhile()
    {
        var (sim, drive, you) = OnGrass();
        var extinguisher = sim.CreateItem(ItemKind.FireExtinguisher);
        Assert.True(sim.GiveItem(you, extinguisher));
        var cone = Simulation.ExtinguisherCone(you.Position, Direction.East);
        Assert.Equal(new[] { new TilePos(6, 5), new TilePos(6, 4), new TilePos(6, 6), new TilePos(7, 5) }, cone);
        foreach (var p in cone)
        {
            sim.SpillFuel(p);
            Assert.True(sim.TryIgnite(p, FireRules.LighterHeat, "a test"));
        }
        var behind = new TilePos(4, 5);
        sim.SpillFuel(behind);
        Assert.True(sim.TryIgnite(behind, FireRules.LighterHeat, "a test"));

        Do(sim, drive, new ExtinguishAction(Direction.East));

        Assert.All(cone, p => Assert.Null(sim.FireAt(p)));
        Assert.NotNull(sim.FireAt(behind)); // not where it wasn't aimed
        Assert.Equal(5, extinguisher.Amount);
        sim.SpillFuel(cone[0]);
        Assert.False(sim.TryIgnite(cone[0], 100, "a test")); // quenched
    }

    [Fact]
    public void PouredFuel_MakesAFuse()
    {
        var trail = Enumerable.Range(6, 4).Select(x => new TilePos(x, 5)).ToList();
        var (sim, drive, you) = OnGrass(floorBut: trail.Append(new TilePos(10, 5)).ToList());
        var can = sim.CreateItem(ItemKind.JerryCan, amount: 12);
        Assert.True(sim.GiveItem(you, can));

        Do(sim, drive, new PourFuelAction(Direction.East));
        Assert.All(trail, p => Assert.True(sim.FuelAt(p)));
        Assert.False(sim.FuelAt(new TilePos(10, 5)));
        Assert.Equal(4, can.Amount);

        // light the near end: the fire runs along the trail
        Assert.True(sim.TryIgnite(trail[0], FireRules.LighterHeat, "a test"));
        var burned = new HashSet<TilePos>();
        for (int i = 0; i < 20; i++)
        {
            Do(sim, drive, new WaitAction(100));
            foreach (var p in trail)
                if (sim.FireAt(p) is not null)
                    burned.Add(p);
        }
        Assert.Equal(trail.Count, burned.Count);
    }
}
