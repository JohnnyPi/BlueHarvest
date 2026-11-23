using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.Power;
using BlueHarvest.Sim.World;
using BlueHarvest.Sim.World.Generation;
using Xunit;

namespace BlueHarvest.Sim.Tests;

/// <summary>
/// World effects step 3: the power network's rules, on a small hand-built network: a geothermal
/// plant, a genetics-style hub (generator, yard fuel, battery bank, a paddock fence) and a solar
/// survey station at the end of the line.
/// </summary>
public class PowerGridTests
{
    private sealed class Net
    {
        public readonly PowerGrid Grid = new(seed: 7);
        public readonly PowerSite PlantSite, Hub, Station;
        public readonly Plant Turbine;
        public readonly Generator PlantGen, HubGen;
        public readonly FuelStore HubFuel;
        public readonly BatteryBank HubBattery, StationBattery;
        public readonly SolarArray StationSolar;
        public readonly Feeder PlantToHub, HubToStation, Fence;
        public readonly int PlantPanel, HubPanel;

        public Net()
        {
            PlantSite = Grid.AddSite("geothermal plant", "geothermal", 0, 0, 50, 40);
            Hub = Grid.AddSite("genetics complex", "genetics", 1000, 0, 60, 120);
            Station = Grid.AddSite("survey station", "ley", 2000, 0, 20, 3);

            PlantPanel = Grid.AddDevice(new Panel(Grid.NextDeviceId, PlantSite.Id, "the switch panel", Footprint.Around(0, 0, 2))).Id;
            Turbine = Grid.AddDevice(new Plant(Grid.NextDeviceId, PlantSite.Id, "the turbine controls", Footprint.Around(0, 0, 2), nuclear: false));
            PlantGen = Grid.AddDevice(new Generator(Grid.NextDeviceId, PlantSite.Id, "the backup generator", Footprint.Around(10, 0, 2), kw: 48, tankCentilitres: 30_000));

            HubPanel = Grid.AddDevice(new Panel(Grid.NextDeviceId, Hub.Id, "the switch panel", Footprint.Around(1000, 0, 2))).Id;
            HubGen = Grid.AddDevice(new Generator(Grid.NextDeviceId, Hub.Id, "the backup generator", Footprint.Around(1010, 0, 2), kw: 144, tankCentilitres: 40_000));
            HubFuel = Grid.AddDevice(new FuelStore(Grid.NextDeviceId, Hub.Id, "the fuel tanks", Footprint.Around(1020, 0, 4), capacityLitres: 18_000));
            HubBattery = Grid.AddDevice(new BatteryBank(Grid.NextDeviceId, Hub.Id, "the battery bank", Footprint.Around(1030, 0, 2), capacityWh: 200_000));

            Grid.AddDevice(new Panel(Grid.NextDeviceId, Station.Id, "the switch panel", Footprint.Around(2000, 0, 2)));
            StationBattery = Grid.AddDevice(new BatteryBank(Grid.NextDeviceId, Station.Id, "the battery bank", Footprint.Around(2000, 0, 2), capacityWh: 24_000));
            StationSolar = Grid.AddDevice(new SolarArray(Grid.NextDeviceId, Station.Id, "the solar charge controller", Footprint.Around(2000, 0, 2), peakW: 6000));

            PlantToHub = Grid.AddFeeder(PlantSite.Id, Hub.Id, Hub.Name);
            HubToStation = Grid.AddFeeder(Hub.Id, Station.Id, Station.Name);
            Fence = Grid.AddFeeder(Hub.Id, -1, "Tyrannosaur paddock");

            // everything working, the plant running, the backups standing by on manual, nothing charged
            foreach (PowerSite s in Grid.Sites) PowerGrid.SetBoard(s, on: true, autoBackup: false);
            PowerGrid.SetPlant(Turbine, PlantState.Running);
            PowerGrid.SetGenerator(PlantGen, GeneratorState.Off, 0);
            PowerGrid.SetGenerator(HubGen, GeneratorState.Off, 0);
            PowerGrid.SetFuel(HubFuel, 0, valveOpen: false);
            PowerGrid.SetBattery(HubBattery, 0, inverterOn: true);
            PowerGrid.SetBattery(StationBattery, 0, inverterOn: true);
            PowerGrid.SetSolar(StationSolar, 100, connected: false);
        }

        public List<PowerEvent> Settle(int sun = 100, WeatherSettings? weather = null) =>
            Grid.Settle(sun, (_, _) => weather ?? WeatherSettings.Clear);

        public List<PowerEvent> Step(long minute, int sun = 100, WeatherSettings? weather = null) =>
            Grid.Step(minute, sun, (_, _) => weather ?? WeatherSettings.Clear);

        public PowerResult Do(PowerDevice device, PowerOp op, int arg = -1, int electronics = 0, int roll = 0) =>
            Grid.Operate(device.Id, op, arg, electronics, roll);

        public PowerResult Do(int deviceId, PowerOp op, int arg = -1) => Grid.Operate(deviceId, op, arg, 0, 0);
    }

    [Fact]
    public void TheGrid_ReachesEverythingThroughClosedStandingLines()
    {
        var net = new Net();
        net.Settle();
        Assert.All(net.Grid.Sites, s => Assert.Equal(PowerSource.Grid, s.Source));
        Assert.All(net.Grid.Sites, s => Assert.True(s.Powered));
        Assert.True(net.Grid.FenceLive(net.Fence));

        // open the hub's breaker for the station: only the station goes
        net.Do(net.HubPanel, PowerOp.OpenLine, net.HubToStation.Id);
        List<PowerEvent> events = net.Settle();
        Assert.False(net.Station.Powered);
        Assert.True(net.Hub.Powered);
        Assert.Contains(events, e => e.Kind == PowerEventKind.SiteDark && e.SiteId == net.Station.Id);
    }

    [Fact]
    public void ADownedLine_CutsTheGrid_UntilSomeoneRepairsIt()
    {
        var net = new Net();
        PowerGrid.SetBreaker(net.PlantToHub, BreakerState.Closed, down: true);
        LineFault fault = net.Grid.AddDevice(new LineFault(net.Grid.NextDeviceId, "the downed line", Footprint.Around(500, 0, 3), net.PlantToHub.Id));
        net.Settle();
        Assert.False(net.Hub.Powered);
        Assert.False(net.Station.Powered); // downstream of the break

        Assert.False(net.Do(fault, PowerOp.Repair, roll: 99).Done); // a bad roll: still down
        Assert.True(net.PlantToHub.Down);
        PowerResult fixedIt = net.Do(fault, PowerOp.Repair, roll: 10);
        Assert.True(fixedIt.Done);
        Assert.Equal(PowerGrid.LineRepairTicks, fixedIt.Ticks);
        net.Settle();
        Assert.True(net.Hub.Powered);
        Assert.True(net.Station.Powered);
    }

    [Theory]
    [InlineData(0, 45)]
    [InlineData(1, 70)]
    [InlineData(2, 90)]
    [InlineData(5, 90)]
    public void Repairs_TakeMoreOftenWithElectronics(int skill, int chance) => Assert.Equal(chance, PowerGrid.RepairChance(skill));

    [Fact]
    public void OnAutomatic_TheBackupStartsWhenTheGridFails_AndStopsWhenItReturns()
    {
        var net = new Net();
        PowerGrid.SetBoard(net.Hub, on: true, autoBackup: true);
        PowerGrid.SetGenerator(net.HubGen, GeneratorState.Off, 20_000);
        net.Settle();
        Assert.Equal(GeneratorState.Off, net.HubGen.State); // the grid is up

        net.Do(net.Turbine, PowerOp.Shutdown);
        List<PowerEvent> events = net.Step(1);
        Assert.Equal(GeneratorState.Running, net.HubGen.State);
        Assert.True(net.HubGen.AutoStarted);
        Assert.Equal(PowerSource.Generator, net.Hub.Source);
        Assert.True(net.Hub.Powered);
        Assert.False(net.Station.Powered); // a generator doesn't feed the grid
        Assert.Contains(events, e => e.Kind == PowerEventKind.GeneratorStarted);

        PowerGrid.SetPlant(net.Turbine, PlantState.Running);
        net.Step(2);
        Assert.Equal(GeneratorState.Off, net.HubGen.State);
        Assert.Equal(PowerSource.Grid, net.Hub.Source);
    }

    [Fact]
    public void AGenerator_BurnsFuel_AndDiesWhenTheDayTankIsDry()
    {
        var net = new Net();
        PowerGrid.SetPlant(net.Turbine, PlantState.Tripped);
        net.Do(net.HubBattery, PowerOp.InverterOff); // or the batteries it charged would carry on when it stops
        PowerGrid.SetGenerator(net.HubGen, GeneratorState.Off, 300); // 3 L: five minutes at 120 kW (0.6 L a minute)
        Assert.True(net.Do(net.HubGen, PowerOp.Start).Done);
        net.Settle();
        Assert.True(net.Hub.Powered);
        for (long m = 1; m <= 4; m++)
        {
            net.Step(m);
            Assert.True(net.Hub.Powered, $"dark after {m} minutes");
        }
        Assert.Equal(60, net.HubGen.FuelCentilitres);
        List<PowerEvent> events = net.Step(5);
        Assert.Equal(GeneratorState.Off, net.HubGen.State);
        Assert.False(net.Hub.Powered);
        Assert.Contains(events, e => e.Kind == PowerEventKind.GeneratorRanDry);
        Assert.False(net.Do(net.HubGen, PowerOp.Start).Done);
    }

    [Fact]
    public void TheYardTanks_TopUpTheDayTank_WhileTheValveIsOpen()
    {
        var net = new Net();
        PowerGrid.SetFuel(net.HubFuel, 100, valveOpen: false);
        net.Step(1);
        Assert.Equal(0, net.HubGen.FuelCentilitres);
        net.Do(net.HubFuel, PowerOp.OpenValve);
        net.Step(2);
        Assert.Equal(PowerGrid.TransferCentilitresPerMinute, net.HubGen.FuelCentilitres);
        Assert.Equal(80, net.HubFuel.Litres);
    }

    [Fact]
    public void Solar_CarriesASmallStationByDay_AndChargesItsBatteriesForTheNight()
    {
        var net = new Net();
        net.Do(net.Grid.Device<Panel>(net.HubPanel), PowerOp.OpenLine, net.HubToStation.Id);
        net.Do(net.StationSolar, PowerOp.Connect);
        net.Settle();
        Assert.Equal(PowerSource.Solar, net.Station.Source);
        Assert.Equal(6000, net.StationSolar.OutputW);
        // an hour of sun: 3 kW spare, 50 Wh a minute into the batteries
        for (long m = 1; m <= 60; m++) net.Step(m);
        Assert.Equal(3000, net.StationBattery.ChargeWh);

        // night: the batteries carry the 3 kW for exactly an hour
        for (long m = 61; m <= 119; m++)
        {
            net.Step(m, sun: 0);
            Assert.Equal(PowerSource.Battery, net.Station.Source);
        }
        List<PowerEvent> events = net.Step(120, sun: 0);
        Assert.False(net.Station.Powered);
        Assert.Contains(events, e => e.Kind == PowerEventKind.BatteryFlat);
    }

    [Fact]
    public void Cloud_CutsTheSolar()
    {
        var net = new Net();
        net.Do(net.Grid.Device<Panel>(net.HubPanel), PowerOp.OpenLine, net.HubToStation.Id);
        net.Do(net.StationSolar, PowerOp.Connect);
        net.Settle(weather: new WeatherSettings(CloudPercent: 100));
        Assert.Equal(1800, net.StationSolar.OutputW); // 30% under full cloud
        Assert.False(net.Station.Powered);          // not enough for 3 kW, and the batteries are flat
    }

    [Fact]
    public void TheMainSwitch_TurnsTheBuildingsOff_WithPowerStillThere()
    {
        var net = new Net();
        net.Settle();
        PowerResult off = net.Do(net.HubPanel, PowerOp.SiteOff);
        Assert.True(off.Done);
        Assert.True(off.Loudness > 0);
        List<PowerEvent> events = net.Settle();
        Assert.False(net.Hub.Powered);
        Assert.True(net.Hub.BusLive);
        Assert.True(net.Station.Powered); // the line runs on through the switchyard
        Assert.Contains(events, e => e.Kind == PowerEventKind.SiteDark && e.SiteId == net.Hub.Id);
        net.Do(net.HubPanel, PowerOp.SiteOn);
        net.Settle();
        Assert.True(net.Hub.Powered);
    }

    [Fact]
    public void ATrippedTurbine_NeedsPowerOnSiteToRestart()
    {
        var net = new Net();
        PowerGrid.SetPlant(net.Turbine, PlantState.Tripped);
        net.Settle();
        Assert.False(net.PlantSite.BusLive);
        Assert.False(net.Do(net.Turbine, PowerOp.Restart).Done);
        Assert.DoesNotContain(net.Grid.Choices(net.Turbine.Id), c => c.Enabled);

        // the black start: the plant's own generator first
        PowerGrid.SetGenerator(net.PlantGen, GeneratorState.Off, 10_000);
        net.Do(net.PlantGen, PowerOp.Start);
        net.Settle();
        Assert.True(net.PlantSite.BusLive);
        Assert.True(net.Do(net.Turbine, PowerOp.Restart).Done);
        Assert.Equal(PlantState.Starting, net.Turbine.State);
        for (long m = 1; m <= 9; m++)
        {
            net.Step(m);
            Assert.Equal(PlantState.Starting, net.Turbine.State);
            Assert.False(net.Hub.Powered);
        }
        List<PowerEvent> events = net.Step(10);
        Assert.Equal(PlantState.Running, net.Turbine.State);
        Assert.True(net.Hub.Powered);
        Assert.True(net.Station.Powered);
        Assert.Contains(events, e => e.Kind == PowerEventKind.PlantRunning);
    }

    [Fact]
    public void AStartUp_FailsIfThePlantLosesItsOwnPower()
    {
        var net = new Net();
        PowerGrid.SetPlant(net.Turbine, PlantState.Tripped);
        PowerGrid.SetGenerator(net.PlantGen, GeneratorState.Off, 10_000);
        net.Do(net.PlantGen, PowerOp.Start);
        net.Settle();
        net.Do(net.Turbine, PowerOp.Restart);
        net.Do(net.PlantGen, PowerOp.Stop);
        net.Settle();
        List<PowerEvent> events = net.Step(1);
        Assert.Equal(PlantState.Tripped, net.Turbine.State);
        Assert.Contains(events, e => e.Kind == PowerEventKind.PlantStopped);
    }

    [Fact]
    public void AReactor_TakesAnHourToComeUp()
    {
        var reactor = new Plant(0, 0, "the reactor controls", Footprint.Around(0, 0, 1), nuclear: true);
        Assert.Equal(60, reactor.StartMinutes);
        Assert.Equal(10, new Plant(0, 0, "the turbine controls", Footprint.Around(0, 0, 1), nuclear: false).StartMinutes);
    }

    [Fact]
    public void Storms_TripOverheadLines_TheSameWayEveryTime()
    {
        long FirstTrip(Net net)
        {
            for (long m = 1; m <= 10_000; m++)
            {
                net.Step(m, weather: WeatherSettings.TestbedStorm);
                if (net.PlantToHub.Breaker == BreakerState.Tripped || net.HubToStation.Breaker == BreakerState.Tripped)
                    return m;
            }
            return -1;
        }
        long a = FirstTrip(new Net()), b = FirstTrip(new Net());
        Assert.True(a > 0, "no breaker tripped in a week of storm");
        Assert.Equal(a, b);
    }

    [Fact]
    public void ClearWeather_TripsNothing()
    {
        var net = new Net();
        for (long m = 1; m <= 3000; m++) net.Step(m);
        Assert.All(net.Grid.Feeders, f => Assert.Equal(BreakerState.Closed, f.Breaker));
    }

    [Fact]
    public void TheTieCable_LetsOnePlantFeedTheOthersNetwork()
    {
        var net = new Net();
        // a second network: a dead reactor with a site of its own
        PowerSite reactorSite = net.Grid.AddSite("nuclear plant", "nuclear", 5000, 0, 50, 80);
        PowerSite lab = net.Grid.AddSite("research lab", "lab", 4000, 0, 30, 25);
        net.Grid.AddDevice(new Panel(net.Grid.NextDeviceId, reactorSite.Id, "the switch panel", Footprint.Around(5000, 0, 2)));
        Plant reactor = net.Grid.AddDevice(new Plant(net.Grid.NextDeviceId, reactorSite.Id, "the reactor controls", Footprint.Around(5000, 0, 2), nuclear: true));
        int labPanel = net.Grid.AddDevice(new Panel(net.Grid.NextDeviceId, lab.Id, "the switch panel", Footprint.Around(4000, 0, 2))).Id;
        net.Grid.AddFeeder(reactorSite.Id, lab.Id, lab.Name);
        Feeder tie = net.Grid.AddFeeder(net.Station.Id, lab.Id, lab.Name, underground: true);
        PowerGrid.SetBoard(reactorSite, on: true, autoBackup: false);
        PowerGrid.SetBoard(lab, on: true, autoBackup: false);
        PowerGrid.SetPlant(reactor, PlantState.Shutdown);
        PowerGrid.SetBreaker(tie, BreakerState.Open);
        net.Settle();
        Assert.False(lab.Powered);

        // the tie's breaker can be worked from either end
        Assert.Contains(net.Grid.Choices(labPanel), c => c.Op == PowerOp.CloseLine && c.Arg == tie.Id);
        Assert.True(net.Do(labPanel, PowerOp.CloseLine, tie.Id).Done);
        net.Settle();
        Assert.True(lab.Powered);
        Assert.True(reactorSite.Powered); // fed backwards up its own line
        Assert.True(reactorSite.BusLive);

        // storms never trip a buried cable
        for (long m = 1; m <= 4000; m++) net.Step(m, weather: WeatherSettings.TestbedStorm);
        Assert.Equal(BreakerState.Closed, tie.Breaker);
    }

    [Fact]
    public void TheFence_FollowsItsSite_AndItsBreaker()
    {
        var net = new Net();
        PowerGrid.SetPlant(net.Turbine, PlantState.Tripped);
        net.Settle();
        Assert.False(net.Grid.FenceLive(net.Fence));
        PowerGrid.SetGenerator(net.HubGen, GeneratorState.Off, 10_000);
        net.Do(net.HubGen, PowerOp.Start);
        net.Settle();
        Assert.True(net.Grid.FenceLive(net.Fence)); // the genetics generator keeps the fences live
        net.Do(net.HubPanel, PowerOp.OpenLine, net.Fence.Id);
        Assert.False(net.Grid.FenceLive(net.Fence));
    }

    [Fact]
    public void Generators_ThatNeedWork_CanBeRepaired_ButSeizedOnesCant()
    {
        var net = new Net();
        PowerGrid.SetGenerator(net.HubGen, GeneratorState.NeedsRepair, 10_000);
        Assert.False(net.Do(net.HubGen, PowerOp.Start).Done);
        Assert.False(net.Do(net.HubGen, PowerOp.Repair, roll: 50).Done); // 45% untrained
        Assert.True(net.Do(net.HubGen, PowerOp.Repair, electronics: 1, roll: 50).Done);
        Assert.Equal(GeneratorState.Off, net.HubGen.State);
        Assert.True(net.Do(net.HubGen, PowerOp.Start).Done);

        PowerGrid.SetGenerator(net.PlantGen, GeneratorState.Seized, 10_000);
        Assert.False(net.Do(net.PlantGen, PowerOp.Repair, electronics: 5, roll: 0).Done);
        Assert.All(net.Grid.Choices(net.PlantGen.Id), c => Assert.False(c.Enabled));
    }

    [Fact]
    public void ThePanel_OffersItsBreakers_AndResetsTrippedOnes()
    {
        var net = new Net();
        PowerGrid.SetBreaker(net.HubToStation, BreakerState.Tripped);
        var choices = net.Grid.Choices(net.HubPanel).ToList();
        Assert.Contains(choices, c => c.Op == PowerOp.SiteOff);
        Assert.Contains(choices, c => c.Op == PowerOp.CloseLine && c.Arg == net.HubToStation.Id && c.Text.StartsWith("Reset"));
        Assert.Contains(choices, c => c.Op == PowerOp.OpenLine && c.Arg == net.Fence.Id);
        Assert.Contains(choices, c => c.Op == PowerOp.BackupAuto);
        // a breaker from another panel can't be worked here
        Assert.False(net.Do(net.PlantPanel, PowerOp.CloseLine, net.HubToStation.Id).Done);
    }

    [Fact]
    public void TheSameSteps_GiveTheSameNetwork()
    {
        ulong Run()
        {
            var net = new Net();
            PowerGrid.SetBoard(net.Hub, on: true, autoBackup: true);
            PowerGrid.SetGenerator(net.HubGen, GeneratorState.Off, 5000);
            PowerGrid.SetFuel(net.HubFuel, 500, valveOpen: true);
            net.Do(net.StationSolar, PowerOp.Connect);
            net.Do(net.Turbine, PowerOp.Shutdown);
            for (long m = 1; m <= 600; m++) net.Step(m, sun: (int)(m % 100), weather: m % 7 == 0 ? WeatherSettings.TestbedStorm : null);
            var hasher = new Core.StateHasher();
            net.Grid.AddTo(hasher);
            return hasher.Value;
        }
        Assert.Equal(Run(), Run());
    }
}

/// <summary>The island's network as PowerPlan builds it from the generated facilities.</summary>
public class PowerPlanTests
{
    private static readonly HashSet<string> Majors = ["genetics", "court", "geothermal", "outstation", "depot", "docks", "airstrip", "orchard", "choir", "drowned", "yard"];

    [Theory]
    [InlineData("Default")]
    [InlineData("SmallNoCliffs")]
    public void EverySite_HasAPanel_AndAtLeastTwoWaysBack(string island)
    {
        IslandWorld world = TestIslands.World(island);
        PowerGrid grid = PowerPlan.Build(world, runSeed: 1);
        Assert.Equal(world.Works.Sites.Select(s => s.Name).Distinct().Count(), grid.Sites.Count);
        foreach (PowerSite site in grid.Sites)
        {
            Assert.NotNull(site.PanelId);
            int own = PowerPlan.OwnWaysBack(grid, site);
            // the grid is always one way; every site has one of its own, the majors two
            Assert.True(own >= (Majors.Contains(site.Kind) ? 2 : 1), $"{site.Name} ({site.Kind}) has {own} way(s) back of its own");
            Assert.NotEqual("nuclear", site.Kind); // no nuclear plant (D-9)
            if (site.Kind == "geothermal")
            {
                Assert.NotNull(site.PlantId);
                Assert.NotNull(site.GeneratorId); // for the black start
            }
        }
    }

    [Theory]
    [InlineData("Default", 1UL)]
    [InlineData("Default", 2UL)]
    [InlineData("SmallNoCliffs", 1UL)]
    public void SomeSites_AreStillPoweredAtTheStart(string island, ulong runSeed)
    {
        PowerGrid grid = PowerPlan.Build(TestIslands.World(island), runSeed);
        grid.Settle(sunPercent: 100, (_, _) => WeatherSettings.Clear);
        Assert.True(grid.Sites.Count(s => s.Powered) >= 2, "fewer than two facilities have power by day");
        // and something can be switched off: a lit site with a panel
        Assert.Contains(grid.Sites, s => s.Powered && grid.Choices(s.PanelId!.Value).Any(c => c.Op == PowerOp.SiteOff));

        PowerGrid night = PowerPlan.Build(TestIslands.World(island), runSeed);
        night.Settle(sunPercent: 0, (_, _) => WeatherSettings.Clear);
        Assert.True(night.Sites.Count(s => s.Powered) >= 1, "nothing has power at night");
    }

    [Fact]
    public void TheLines_FollowTheGeneratedPowerTree_FromTheOnePlant()
    {
        IslandWorld world = TestIslands.World("Default");
        PowerGrid grid = PowerPlan.Build(world, runSeed: 1);
        // the geothermal plant is the grid's one source (no nuclear plant, D-9), so there's no tie cable between two plants
        Assert.Equal(1, grid.Sites.Count(s => grid.PlantOf(s) is not null));
        Assert.Equal("geothermal", grid.Sites.Single(s => grid.PlantOf(s) is not null).Kind);
        Assert.Equal(world.Works.Power.Count, grid.Feeders.Count);
        Assert.Equal(0, grid.Feeders.Count(f => f.Underground));
        foreach (Feeder f in grid.Feeders.Where(f => f.Down))
            Assert.NotNull(f.FaultDevice);
        Assert.All(grid.Feeders.Where(f => f.Underground), f => Assert.Equal(BreakerState.Open, f.Breaker));
    }

    [Fact]
    public void LampsAndEquipment_SitInsideTheirCompounds()
    {
        PowerGrid grid = PowerPlan.Build(TestIslands.World("Default"), runSeed: 1);
        foreach (PowerLamp lamp in grid.Lamps.Where(l => l.InCompound))
            Assert.True(grid.Sites[lamp.SiteId].Contains(lamp.X, lamp.Y), $"a lamp of the {grid.Sites[lamp.SiteId].Name} is outside it");
        // (elevators and cable cars stand at stations outside the compounds they run on)
        foreach (PowerDevice d in grid.Devices.Where(d => d.SiteId >= 0 && d is not Transport))
        {
            var (x, y) = d.Footprint.Centre;
            Assert.True(grid.Sites[d.SiteId].Contains(x, y), $"{d.Name} of the {grid.Sites[d.SiteId].Name} is outside it");
        }
        Assert.All(grid.Sites, s => Assert.Contains(grid.Lamps, l => l.SiteId == s.Id));
    }

    [Fact]
    public void TheSameRun_FindsTheSameNetwork_AndAnotherRunADifferentOne()
    {
        IslandWorld world = TestIslands.World("Default");
        ulong Hash(ulong run)
        {
            var hasher = new Core.StateHasher();
            PowerPlan.Build(world, run).AddTo(hasher);
            return hasher.Value;
        }
        Assert.Equal(Hash(1), Hash(1));
        Assert.NotEqual(Hash(1), Hash(2));
    }
}

/// <summary>The network inside a simulation on a window of the island: lamps, working equipment, generator noise.</summary>
public class SimulationPowerTests
{
    private const string Island = "SmallNoCliffs";

    /// <summary>Does one thing on its first turn, then holds still.</summary>
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

    private static (Simulation Sim, IslandWindow Window, PowerGrid Grid, PowerSite Site) AtGenetics()
    {
        IslandWorld world = TestIslands.World(Island);
        Site genetics = world.Works.Sites.First(t => t.Kind == "genetics");
        // six chunks: the compound (about 75 m each way) fits with room to spare wherever its centre falls
        var window = new IslandWindow(world, TestIslands.Chunker(Island), genetics.X, genetics.Y, 6);
        var sim = new Simulation(TestContent.NewSession(), window.Map, seed: 5);
        sim.SetTimeOfDay(12);
        PowerGrid grid = PowerPlan.Build(world, runSeed: 1);
        sim.UsePower(grid, window.OriginX, window.OriginY);
        return (sim, window, grid, grid.Sites.First(s => s.Kind == "genetics"));
    }

    /// <summary>A tile inside a device's room, just in from its corner (always floor).</summary>
    private static TilePos InsideOf(Simulation sim, PowerDevice device) =>
        sim.FromIsland(device.Footprint.X0 + 1, device.Footprint.Y0 + 1);

    [Fact]
    public void TheSitesLamps_AreLightsInTheWindow_OnWithTheSite()
    {
        var (sim, _, grid, site) = AtGenetics();
        var lamps = grid.Lamps.Where(l => l.SiteId == site.Id && l.InCompound).ToList();
        Assert.NotEmpty(lamps);
        foreach (PowerLamp lamp in lamps)
        {
            TilePos tile = sim.FromIsland(lamp.X, lamp.Y);
            Assert.True(sim.Map.InBounds(tile));
            Assert.Contains(sim.Lights, l => l.Position == tile && l.RadiusTiles == lamp.RadiusTiles
                && l.IntensityPercent == lamp.IntensityPercent && l.On == lamp.Lit && l.Red == (lamp.Kind == LampKind.Emergency));
        }
        // by day: room lights with the power, emergency lights without it, floodlights off
        Assert.All(lamps.Where(l => l.Kind == LampKind.Room), l => Assert.Equal(site.Powered, l.Lit));
        Assert.All(lamps.Where(l => l.Kind == LampKind.Emergency), l => Assert.Equal(!site.Powered, l.Lit));
        Assert.All(lamps.Where(l => l.Kind == LampKind.Flood), l => Assert.False(l.Lit));
    }

    [Fact]
    public void ThrowingTheMainSwitch_PutsTheLightsOut_AndBackOn()
    {
        var (sim, _, grid, site) = AtGenetics();
        PowerDevice panel = grid.Devices[site.PanelId!.Value];
        // make sure there's power to switch: the grid's state varies by run, so give the site its generator
        if (grid.GeneratorOf(site) is { } gen)
        {
            PowerGrid.SetGenerator(gen, GeneratorState.Running, gen.TankCentilitres);
            PowerGrid.SetBoard(site, on: true, autoBackup: false);
            grid.Settle(100, (_, _) => WeatherSettings.Clear);
        }
        Assert.True(site.BusLive);
        sim.AddActor("Sparky", 's', InsideOf(sim, panel), Species.Human, new Once(new OperateAction(panel.Id, PowerOp.SiteOff)));
        var said = new List<string>();
        sim.PowerOperated += (_, _, r) => said.Add(r.Message);
        sim.RunUntil(sim.CurrentTick + 100); // the switch acts as the turn begins; the noise is still fresh
        Assert.False(site.Powered);
        Assert.Single(said);
        PowerLamp roomLamp = grid.Lamps.First(l => l.SiteId == site.Id && l.Kind == LampKind.Room);
        TilePos lampTile = sim.FromIsland(roomLamp.X, roomLamp.Y);
        Assert.DoesNotContain(sim.Lights, l => l.Position == lampTile && l.On && !l.Red);
        // the emergency lights take over
        Assert.Contains(sim.Lights, l => l.On && l.Red);
        Assert.Contains(sim.RecentNoises, n => n.Kind == "a heavy switch");
    }

    [Fact]
    public void WorkingEquipment_FromTooFarAway_DoesNothing()
    {
        var (sim, _, grid, site) = AtGenetics();
        PowerDevice panel = grid.Devices[site.PanelId!.Value];
        bool before = site.BoardOn;
        TilePos outside = sim.FromIsland(site.X + site.Radius + 5, site.Y);
        var result = new List<PowerResult>();
        sim.PowerOperated += (_, _, r) => result.Add(r);
        sim.AddActor("Faraway", 'f', outside, Species.Human, new Once(new OperateAction(panel.Id, before ? PowerOp.SiteOff : PowerOp.SiteOn)));
        sim.RunUntil(sim.CurrentTick + 500);
        Assert.Equal(before, site.BoardOn);
        Assert.False(Assert.Single(result).Done);
    }

    [Fact]
    public void ARunningGenerator_IsHeard()
    {
        var (sim, _, grid, site) = AtGenetics();
        Generator gen = grid.GeneratorOf(site)!;
        PowerGrid.SetGenerator(gen, GeneratorState.Running, gen.TankCentilitres);
        sim.RunUntil(sim.CurrentTick + Simulation.GeneratorNoiseTicks + 1);
        Assert.Contains(sim.RecentNoises, n => n.Kind == NoiseKinds.Generator);
    }

    [Fact]
    public void TheLamps_StayOnTheirRooms_WhenTheMapScrolls()
    {
        var (sim, window, grid, site) = AtGenetics();
        PowerLamp lamp = grid.Lamps.First(l => l.SiteId == site.Id);
        (int dx, int dy) = window.Recenter(new TilePos(window.Size / 2 + 128, window.Size / 2));
        sim.ShiftWindow(dx, dy);
        TilePos tile = sim.FromIsland(lamp.X, lamp.Y);
        if (sim.Map.InBounds(tile))
            Assert.Contains(sim.Lights, l => l.Position == tile && l.RadiusTiles == lamp.RadiusTiles);
        else
            Assert.DoesNotContain(sim.Lights, l => l.On && !sim.Map.InBounds(l.Position));
    }

    [Fact]
    public void TwoRuns_WithPower_EndTheSame()
    {
        ulong Run()
        {
            var (sim, _, _, _) = AtGenetics();
            sim.RunUntil(3 * WorldClock.TicksPerGameHour);
            return sim.ComputeStateHash();
        }
        Assert.Equal(Run(), Run());
    }
}
