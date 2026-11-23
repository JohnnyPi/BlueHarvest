using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Injuries;
using BlueHarvest.Sim.Lighting;
using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.Power;
using BlueHarvest.Sim.World;
using BlueHarvest.Sim.World.Generation;
using Xunit;
using BlueHarvest.Sim.ContentIntegration;

namespace BlueHarvest.Sim.Tests;

/// <summary>
/// World effects step 16: what power does. Live paddock fences, security and motorised doors,
/// elevators and cable cars, red emergency lights, and floodlights on their own panels.
/// </summary>
public class PowerUseGridTests
{
    /// <summary>One plant-fed facility (a fence on it) and an outstation on a line from it.</summary>
    private sealed class Net
    {
        public readonly PowerGrid Grid = new(seed: 3);
        public readonly PowerSite Hub, Out;
        public readonly Panel HubPanel;
        public readonly Feeder HubToOut, Fence;

        public Net()
        {
            Hub = Grid.AddSite("genetics complex", "genetics", 0, 0, 60, 100);
            Out = Grid.AddSite("survey station", "ley", 2000, 0, 20, 3);
            HubPanel = Grid.AddDevice(new Panel(Grid.NextDeviceId, Hub.Id, "the switch panel", Footprint.Around(0, 0, 2)));
            Plant plant = Grid.AddDevice(new Plant(Grid.NextDeviceId, Hub.Id, "the turbine controls", Footprint.Around(10, 0, 2), nuclear: false));
            Grid.AddDevice(new Panel(Grid.NextDeviceId, Out.Id, "the switch panel", Footprint.Around(2000, 0, 2)));
            HubToOut = Grid.AddFeeder(Hub.Id, Out.Id, Out.Name);
            Fence = Grid.AddFeeder(Hub.Id, -1, "Tyrannosaur paddock");
            foreach (PowerSite s in Grid.Sites) PowerGrid.SetBoard(s, on: true, autoBackup: false);
            PowerGrid.SetPlant(plant, PlantState.Running);
        }

        public void Settle(int sun = 100) => Grid.Settle(sun, (_, _) => WeatherSettings.Clear);
        public void Step(long minute, int sun) => Grid.Step(minute, sun, (_, _) => WeatherSettings.Clear);
    }

    [Fact]
    public void RoomLights_FollowThePower_EmergencyLights_TheLackOfIt()
    {
        var net = new Net();
        PowerLamp room = net.Grid.AddLamp(net.Out.Id, 2000, 5, 25, 6, 70);
        PowerLamp red = net.Grid.AddLamp(net.Out.Id, 2002, 5, 22, 5, 30, LampKind.Emergency);
        net.Settle();
        Assert.True(room.Lit);
        Assert.False(red.Lit);

        PowerGrid.SetBreaker(net.HubToOut, BreakerState.Open);
        net.Settle();
        Assert.False(net.Out.Powered);
        Assert.False(room.Lit);
        Assert.True(red.Lit);
    }

    [Fact]
    public void Floodlights_OnTheGrid_ShineOnlyAfterDusk()
    {
        var net = new Net();
        PowerLamp flood = net.Grid.AddLamp(net.Hub.Id, 40, 40, 60, 14, 75, LampKind.Flood);
        net.Settle(sun: 100);
        Assert.False(flood.Lit);
        net.Settle(sun: 0);
        Assert.True(flood.Lit);
        PowerGrid.SetBoard(net.Hub, on: false, autoBackup: false);
        net.Settle(sun: 0);
        Assert.False(flood.Lit);
    }

    [Fact]
    public void ASolarFloodlight_ChargesByDay_ShinesWithoutTheGrid_AndRunsDown()
    {
        var net = new Net();
        PowerGrid.SetBoard(net.Hub, on: false, autoBackup: false); // the facility is dark: the floodlight doesn't care
        PowerLamp flood = net.Grid.AddLamp(net.Hub.Id, 40, 40, 60, 14, 75, LampKind.Flood);
        PowerGrid.SetStandalone(flood, panelW: 150, capacityWh: 1500, chargeWh: 0, broken: false);

        net.Settle(sun: 0);
        Assert.False(flood.Lit); // flat battery

        long minute = 0;
        for (int i = 0; i < 60; i++) net.Step(minute++, sun: 100);
        Assert.Equal(120, flood.ChargeWh); // 150 W of panel in full sun: 2 Wh a minute
        Assert.False(flood.Lit);            // and it's day

        net.Step(minute++, sun: 0);
        Assert.True(flood.Lit);
        int nights = 1;
        while (flood.Lit && nights < 100) { net.Step(minute++, sun: 0); nights++; }
        Assert.InRange(nights, 39, 42); // 120 Wh drawn at 3 Wh a minute: about 40 minutes of light
        Assert.Equal(0, flood.ChargeWh);
    }

    [Fact]
    public void ABrokenSolarFloodlight_NeverShines()
    {
        var net = new Net();
        PowerLamp flood = net.Grid.AddLamp(net.Hub.Id, 40, 40, 60, 14, 75, LampKind.Flood);
        PowerGrid.SetStandalone(flood, panelW: 150, capacityWh: 1500, chargeWh: 1500, broken: true);
        net.Settle(sun: 0);
        Assert.False(flood.Lit);
    }

    [Fact]
    public void SecurityDoors_HoldWithPowerAndLocks_AndFailSafe()
    {
        var net = new Net();
        PowerDoor door = net.Grid.AddDoor(net.Hub.Id, 5, 5, DoorKind.SecurityLock, "Server hall");
        Assert.True(net.Hub.HasSecurityDoors);
        net.Settle();
        Assert.True(net.Grid.DoorLocked(door));

        Assert.Contains(net.Grid.Choices(net.HubPanel.Id), c => c.Op == PowerOp.ReleaseLocks);
        Assert.True(net.Grid.Operate(net.HubPanel.Id, PowerOp.ReleaseLocks, -1, 0, 0).Done);
        Assert.False(net.Grid.DoorLocked(door));
        Assert.Contains(net.Grid.Choices(net.HubPanel.Id), c => c.Op == PowerOp.EngageLocks);
        Assert.True(net.Grid.Operate(net.HubPanel.Id, PowerOp.EngageLocks, -1, 0, 0).Done);
        Assert.True(net.Grid.DoorLocked(door));

        // the power dies: the maglocks let go
        Assert.True(net.Grid.Operate(net.HubPanel.Id, PowerOp.SiteOff, -1, 0, 0).Done);
        net.Settle();
        Assert.False(net.Grid.DoorLocked(door));
    }

    [Fact]
    public void AFacilityWithoutSecurityDoors_OffersNoLockSwitch()
    {
        var net = new Net();
        net.Settle();
        Assert.DoesNotContain(net.Grid.Choices(net.HubPanel.Id), c => c.Op is PowerOp.EngageLocks or PowerOp.ReleaseLocks);
    }

    [Fact]
    public void ARide_TakesTheShaftsAndTheLine_AndNeedsPowerAtBothEnds()
    {
        var net = new Net();
        var a = net.Grid.AddDevice(new Transport(net.Grid.NextDeviceId, net.Hub.Id, "the elevator", Footprint.Around(30, 30, 2),
            TransportKind.Monorail, "Underground monorail", "Station at genetics complex", alongM: 0, shaftM: 20));
        var b = net.Grid.AddDevice(new Transport(net.Grid.NextDeviceId, net.Out.Id, "the elevator", Footprint.Around(2030, 30, 2),
            TransportKind.Monorail, "Underground monorail", "Station at survey station", alongM: 3000, shaftM: 30));
        net.Settle();

        // 2000 at the doors + 50 m of shafts at 2 m/s + 3 km at 15 m/s
        Assert.Equal(2000 + 2500 + 20000, PowerGrid.RideTicks(a, b));
        Assert.Equal(b, Assert.Single(net.Grid.Destinations(a)));
        PowerChoice ride = Assert.Single(net.Grid.Choices(a.Id), c => c.Op == PowerOp.Ride);
        Assert.True(ride.Enabled);
        Assert.True(net.Grid.Operate(a.Id, PowerOp.Ride, b.Id, 0, 0).Done);

        PowerGrid.SetBreaker(net.HubToOut, BreakerState.Open);
        net.Settle();
        Assert.NotNull(net.Grid.RideBlock(a, b));
        Assert.False(Assert.Single(net.Grid.Choices(a.Id), c => c.Op == PowerOp.Ride).Enabled);
        Assert.False(net.Grid.Operate(a.Id, PowerOp.Ride, b.Id, 0, 0).Done);
        Assert.False(net.Grid.Operate(a.Id, PowerOp.Ride, a.Id, 0, 0).Done); // not to where you are
    }

    [Fact]
    public void ACableCar_RunsOnItsWinchEndAlone()
    {
        var net = new Net();
        var bottom = net.Grid.AddDevice(new Transport(net.Grid.NextDeviceId, net.Hub.Id, "the cable car", Footprint.Around(30, 30, 2),
            TransportKind.CableCar, "Summit lift", "Station at genetics complex", alongM: 0, shaftM: 0));
        var top = net.Grid.AddDevice(new Transport(net.Grid.NextDeviceId, net.Hub.Id, "the cable car", Footprint.Around(900, 900, 2),
            TransportKind.CableCar, "Summit lift", "Station at the summit", alongM: 1500, shaftM: 0));
        PowerGrid.SetBreaker(net.HubToOut, BreakerState.Open); // irrelevant to the lift
        net.Settle();
        Assert.Null(net.Grid.RideBlock(top, bottom));
        Assert.Equal(2000 + 1500 * 100 / 5, PowerGrid.RideTicks(top, bottom));
    }
}

/// <summary>Power in the simulation, on a small hand-made map whose tiles are island metres.</summary>
public class PowerUseSimulationTests
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

    /// <summary>
    /// A 40×40 map with a fence along y = 20 (x 5–35) and a door at (10, 10) in a wall along y = 10; a
    /// plant-fed site around it whose fence feeder runs that fence line.
    /// </summary>
    private sealed class Rig
    {
        public readonly WorldMap Map = new(40, 40);
        public readonly PowerGrid Grid = new(seed: 9);
        public readonly PowerSite Site;
        public readonly Panel Panel;
        public readonly Feeder Fence;
        public readonly TilePos Door = new(10, 10);
        public Simulation Sim = null!;

        public Rig(DoorKind? door = null, bool powered = true, bool fenceLive = true)
        {
            for (int x = 5; x <= 35; x++)
                Map.Cover[new TilePos(x, 20)] = CoverType.Fence;
            for (int x = 0; x < 40; x++)
                Map.Cover[new TilePos(x, 10)] = CoverType.Wall;
            Map.Cover[Door] = CoverType.Door;
            Map.Structures.SetDoor(Door, DoorState.Closed);

            Site = Grid.AddSite("test compound", "genetics", 20, 5, 30, 50);
            Panel = Grid.AddDevice(new Panel(Grid.NextDeviceId, Site.Id, "the switch panel", Footprint.Around(30, 5, 1)));
            Plant plant = Grid.AddDevice(new Plant(Grid.NextDeviceId, Site.Id, "the turbine controls", Footprint.Around(35, 5, 1), nuclear: false));
            Fence = Grid.AddFeeder(Site.Id, -1, "Test paddock");
            Grid.AddFenceLine(Fence.Id, "Test paddock", [5, 20, 35, 20]);
            if (door is { } kind)
                Grid.AddDoor(Site.Id, Door.X, Door.Y, kind, kind == DoorKind.SecurityLock ? "Server hall" : "Vehicle bays");
            PowerGrid.SetBoard(Site, on: powered, autoBackup: false);
            PowerGrid.SetPlant(plant, PlantState.Running);
            if (!fenceLive)
                PowerGrid.SetBreaker(Fence, BreakerState.Open);
        }

        public Simulation Start(int hour = 12)
        {
            Sim = new Simulation(Map, seed: 1);
            Sim.SetTimeOfDay(hour);
            Sim.UsePower(Grid, 0, 0);
            return Sim;
        }
    }

    [Fact]
    public void ALiveFence_HoldsTheRex_AndBitesIt_UntilItsBreakerIsOpened()
    {
        var rig = new Rig();
        var sim = rig.Start();
        Assert.True(sim.IsLiveFence(new TilePos(20, 20)));
        Assert.False(sim.IsLiveFence(new TilePos(20, 21)));

        var drive = new PlayerController();
        var rex = sim.AddActor("Rex", 'T', new TilePos(20, 30), Species.Rex, drive, Direction.North);
        var shocked = new List<Actor>();
        sim.Shocked += (a, _) => shocked.Add(a);
        void Drive(int moves)
        {
            for (int i = 0; i < moves; i++)
            {
                drive.Pending = new MoveAction(Direction.North);
                sim.RunUntilInputNeeded();
            }
        }
        sim.RunUntilInputNeeded();
        Drive(12);
        Assert.All(Enumerable.Range(5, 31), x => Assert.Equal(CoverType.Fence, sim.Cover[new TilePos(x, 20)]));
        Assert.Contains(rex, shocked);
        Assert.True(rex.Position.Y > 20);

        // someone opens the fence's breaker: it's just wire again, and the Rex goes through
        sim.AddActor("Sparky", 's', new TilePos(30, 5), Species.Human, new Once(new OperateAction(rig.Panel.Id, PowerOp.OpenLine, rig.Fence.Id)));
        Drive(12);
        Assert.False(sim.IsLiveFence(new TilePos(20, 20)));
        Assert.Contains(Enumerable.Range(17, 7), x => sim.Cover[new TilePos(x, 20)] == CoverType.BrokenFence);
    }

    [Fact]
    public void WalkingIntoALiveFence_Hurts_ADeadOneJustStopsYou()
    {
        var rig = new Rig();
        var sim = rig.Start();
        var you = sim.AddActor("You", '@', new TilePos(15, 21), Species.Human, new Once(new MoveAction(Direction.North)));
        sim.RunUntil(sim.CurrentTick + 200);
        Assert.True(you.Health[BodySection.LeftArm].Severity == Severity.Yellow || you.Health[BodySection.RightArm].Severity == Severity.Yellow);
        Assert.Contains(sim.RecentNoises, n => n.Kind == NoiseKinds.ElectricCrack);

        var dead = new Rig(fenceLive: false); // its breaker is open: the main switch alone doesn't kill a fence
        var quiet = dead.Start();
        var other = quiet.AddActor("Other", 'o', new TilePos(15, 21), Species.Human, new Once(new MoveAction(Direction.North)));
        quiet.RunUntil(quiet.CurrentTick + 200);
        Assert.False(quiet.IsLiveFence(new TilePos(15, 20)));
        Assert.Equal(Severity.Green, other.Health[BodySection.LeftArm].Severity);
        Assert.Equal(Severity.Green, other.Health[BodySection.RightArm].Severity);
        Assert.Equal(new TilePos(15, 21), other.Position);
    }

    [Fact]
    public void ASecurityDoor_IsLockedWhilePowered_AndOpensWhenThePowerIsCut()
    {
        var rig = new Rig(DoorKind.SecurityLock);
        var sim = rig.Start();
        Assert.Equal(DoorState.Locked, sim.Map.Structures.DoorAt(rig.Door));

        sim.AddActor("Sparky", 's', new TilePos(30, 5), Species.Human, new Once(new OperateAction(rig.Panel.Id, PowerOp.SiteOff)));
        sim.RunUntil(sim.CurrentTick + 100);
        Assert.False(rig.Site.Powered);
        Assert.Equal(DoorState.Closed, sim.Map.Structures.DoorAt(rig.Door));
    }

    [Fact]
    public void ReleasingTheLocks_AtThePanel_UnlocksThemWithThePowerOn()
    {
        var rig = new Rig(DoorKind.SecurityLock);
        var sim = rig.Start();
        sim.AddActor("Sparky", 's', new TilePos(30, 5), Species.Human, new Once(new OperateAction(rig.Panel.Id, PowerOp.ReleaseLocks)));
        sim.RunUntil(sim.CurrentTick + 100);
        Assert.True(rig.Site.Powered);
        Assert.Equal(DoorState.Closed, sim.Map.Structures.DoorAt(rig.Door));
    }

    [Fact]
    public void AMotorDoor_SlidesWithPower_AndHasToBeForcedWithout()
    {
        long Open(bool powered, out Simulation sim, out bool forced)
        {
            var rig = new Rig(DoorKind.Motor, powered);
            sim = rig.Start();
            bool wasForced = false;
            sim.DoorForced += (_, _) => wasForced = true;
            var you = sim.AddActor("You", '@', new TilePos(10, 11), Species.Human, new Once(new MoveAction(Direction.North)));
            long start = sim.CurrentTick;
            sim.RunUntil(start + 1);
            forced = wasForced;
            Assert.Equal(DoorState.Open, sim.Map.Structures.DoorAt(rig.Door));
            return you.NextActTick - start;
        }

        long quick = Open(true, out Simulation lit, out bool forcedLit);
        Assert.False(forcedLit);
        Assert.Contains(lit.RecentNoises, n => n.Kind == NoiseKinds.DoorSliding);

        long slow = Open(false, out Simulation dark, out bool forcedDark);
        Assert.True(forcedDark);
        Assert.Contains(dark.RecentNoises, n => n.Kind == NoiseKinds.DoorForced);
        Assert.True(slow >= Simulation.ForcedDoorTicks);
        Assert.True(slow > quick);
    }

    [Fact]
    public void EmergencyLights_AreRedLight_OnWhenThePowerIsOff()
    {
        var rig = new Rig(powered: false);
        rig.Grid.AddLamp(rig.Site.Id, 25, 5, heightDm: 22, radiusTiles: 5, intensityPercent: 30, LampKind.Emergency);
        rig.Grid.AddLamp(rig.Site.Id, 15, 5, heightDm: 25, radiusTiles: 6, intensityPercent: 70);
        var sim = rig.Start(hour: 0);
        LightSource red = Assert.Single(sim.Lights, l => l.Red);
        Assert.True(red.On);
        Assert.DoesNotContain(sim.Lights, l => l.On && !l.Red);
        Assert.True(sim.Light.RedPercentAt(new TilePos(25, 6)) >= 50, "the light by the emergency lamp is mostly red");
        Assert.InRange(sim.Light.LevelAt(new TilePos(25, 6)), 1, 60); // and dim
        Assert.Equal(0, sim.Light.RedPercentAt(new TilePos(15, 2)));
    }

    [Fact]
    public void ARide_ToAStopOnTheMap_EndsThere_AfterTheRidesTime()
    {
        var rig = new Rig();
        var from = rig.Grid.AddDevice(new Transport(rig.Grid.NextDeviceId, rig.Site.Id, "the elevator", new Footprint(2, 2, 8, 8),
            TransportKind.Monorail, "Underground monorail", "Station A", alongM: 0, shaftM: 10));
        var to = rig.Grid.AddDevice(new Transport(rig.Grid.NextDeviceId, rig.Site.Id, "the elevator", new Footprint(30, 30, 36, 36),
            TransportKind.Monorail, "Underground monorail", "Station B", alongM: 600, shaftM: 10));
        var sim = rig.Start();
        var ended = new List<Actor>();
        sim.RideEnded += ended.Add;
        var you = sim.AddActor("You", '@', new TilePos(4, 4), Species.Human, new Once(new OperateAction(from.Id, PowerOp.Ride, to.Id)));
        Assert.True(sim.GiveItem(you, sim.CreateItem(CoreItems.Keycard, tag: "monorail", level: 1))); // the monorail's access key (step 21)
        long start = sim.CurrentTick;
        sim.RunUntil(start + 1);
        Assert.True(you.PendingRide.HasValue);
        Assert.Equal((31L, 31L), you.PendingRide.Value);
        Assert.Equal(start + PowerGrid.RideTicks(from, to), you.NextActTick);

        TilePos there = sim.CompleteRide(you);
        Assert.Equal(new TilePos(31, 31), there);
        Assert.Equal(there, you.Position);
        Assert.False(you.LeftBehind);
        Assert.Null(you.PendingRide);
        Assert.Equal(you, Assert.Single(ended));
    }
}

/// <summary>What the generated island's power is wired to.</summary>
public class PowerUsePlanTests
{
    [Theory]
    [InlineData("Default")]
    [InlineData("SmallNoCliffs")]
    public void EveryPaddock_OnAFenceFeeder_HasItsFenceLine(string island)
    {
        IslandWorld world = TestIslands.World(island);
        PowerGrid grid = PowerPlan.Build(world, runSeed: 1);
        foreach (Feeder f in grid.Feeders.Where(f => f.ToFence && world.Works.Paddocks.Any(p => p.Name == f.ToName)))
            Assert.Contains(grid.FenceLines, l => l.FeederId == f.Id && l.Poly.Length >= 6);
    }

    [Fact]
    public void TheFacilities_HaveSecurityAndMotorDoors()
    {
        PowerGrid grid = PowerPlan.Build(TestIslands.World("Default"), runSeed: 1);
        Assert.Contains(grid.Doors, d => d.Kind == DoorKind.SecurityLock);
        Assert.Contains(grid.Doors, d => d.Kind == DoorKind.Motor);
        Assert.All(grid.Doors.Where(d => d.Kind == DoorKind.SecurityLock), d => Assert.True(grid.Sites[d.SiteId].HasSecurityDoors));
        // no door twice
        Assert.Equal(grid.Doors.Count, grid.Doors.Select(d => (d.X, d.Y)).Distinct().Count());
    }

    [Fact]
    public void EmergencyLights_AreFewerThanRoomLights_AndEveryFacilityHasSome()
    {
        PowerGrid grid = PowerPlan.Build(TestIslands.World("Default"), runSeed: 1);
        foreach (PowerSite site in grid.Sites)
        {
            var mine = grid.Lamps.Where(l => l.SiteId == site.Id && l.InCompound).ToList();
            int rooms = mine.Count(l => l.Kind == LampKind.Room), reds = mine.Count(l => l.Kind == LampKind.Emergency);
            if (rooms == 0)
                continue;
            Assert.InRange(reds, 1, rooms - 1 >= 1 ? rooms - 1 : 1);
            Assert.Equal(4, mine.Count(l => l.Kind == LampKind.Flood));
        }
    }

    [Fact]
    public void SomeFloodlights_RunOnTheirOwnPanels()
    {
        PowerGrid grid = PowerPlan.Build(TestIslands.World("Default"), runSeed: 1);
        var floods = grid.Lamps.Where(l => l.Kind == LampKind.Flood).ToList();
        Assert.Contains(floods, l => l.Standalone);
        Assert.Contains(floods, l => !l.Standalone);
        Assert.All(floods.Where(l => l.Standalone), l => Assert.InRange(l.ChargeWh, 0, l.CapacityWh));
    }

    [Fact]
    public void EveryStation_HasARide_ToTheOtherStopsOnItsLine()
    {
        IslandWorld world = TestIslands.World("Default");
        PowerGrid grid = PowerPlan.Build(world, runSeed: 1);
        var rides = grid.Devices.OfType<Transport>().ToList();
        if (world.Works.Monorail is { Stations.Count: >= 2 })
            Assert.True(rides.Count(t => t.Kind == TransportKind.Monorail) >= 2, "the monorail stations have no elevators");
        foreach (Transport t in rides)
        {
            Assert.NotEmpty(grid.Destinations(t));
            Assert.All(grid.Destinations(t), d => Assert.True(PowerGrid.RideTicks(t, d) > PowerGrid.BoardingTicks));
        }
    }
}
