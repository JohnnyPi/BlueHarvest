using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Injuries;
using BlueHarvest.Sim.World;
using Xunit;
using BlueHarvest.Sim.ContentIntegration;

namespace BlueHarvest.Sim.Tests;

/// <summary>Step 22b: warmth (temperature, wet, wind, shelter, fire) and sickness (bad water, dirty wounds, antibiotics).</summary>
public class AilmentTests
{
    private const int Minute = WorldClock.TicksPerGameMinute;
    private const int Hour = 60 * Minute;

    private static readonly TilePos Start = new(2, 5);

    private static (Simulation Sim, PlayerController Drive, Actor You) Person(WorldMap? map = null, WeatherSettings? weather = null)
    {
        var sim = new Simulation(TestContent.NewSession(), map ?? new WorldMap(20, 10), seed: 1);
        sim.SetWeather(weather ?? WeatherSettings.Clear);
        var drive = new PlayerController();
        var you = sim.AddActor("You", '@', Start, Species.Human, drive, Direction.East);
        sim.UseNeeds(you, new NeedsStart(HungerKcal: 0, ThirstMl: 0, AwakeMinutes: 0));
        sim.RunUntilInputNeeded();
        return (sim, drive, you);
    }

    private static void Do(Simulation sim, PlayerController drive, ActorAction action)
    {
        drive.Pending = action;
        sim.RunUntilInputNeeded();
    }

    /// <summary>Waits a long while in six-hour stretches, fed, watered and rested between them, so only the ailment under test builds up.</summary>
    private static void Endure(Simulation sim, PlayerController drive, Actor you, int hours)
    {
        for (int done = 0; done < hours && !you.IsDead; done += 6)
        {
            sim.SetNeeds(you, hungerKcal: 0, thirstMl: 0, awakeMinutes: 0);
            Do(sim, drive, new WaitAction(Math.Min(6, hours - done) * Hour));
        }
    }

    private static WorldMap WithStream(int x = 3, int y = 5)
    {
        var map = new WorldMap(20, 10);
        map.Cover[new TilePos(x, y)] = CoverType.ShallowWater;
        return map;
    }

    // ---------- warmth ----------

    [Fact]
    public void TheAir_IsCoolestBeforeDawn_AndWarmestInTheAfternoon()
    {
        var (sim, _, you) = Person();
        Assert.Equal(NeedRules.DefaultBaseTemperature, sim.AirTemperature(you.Position)); // no clock: no swing
        sim.SetBaseTemperature(20);
        Assert.Equal(200, sim.AirTemperature(you.Position));

        sim.SetTimeOfDay(5);
        Assert.Equal(160, sim.AirTemperature(you.Position));
        sim.SetTimeOfDay(15);
        Assert.InRange(sim.AirTemperature(you.Position), 240, 260); // the swing, and the sun out in the open
        Assert.Equal(240, sim.AirTemperature(you.Position, outdoors: false));
    }

    [Fact]
    public void Rain_SoaksYou_AndAPonchoKeepsMostOfItOff()
    {
        var rain = new WeatherSettings(RainPercent: 70);
        var (sim, drive, you) = Person(weather: rain);
        Do(sim, drive, new WaitAction(20 * Minute));
        Assert.Equal(100, you.Needs!.Wet);

        var (sim2, drive2, you2) = Person(weather: rain);
        Assert.True(sim2.GiveItem(you2, sim2.CreateItem(CoreItems.Poncho)));
        Assert.Equal(CoreItems.Poncho, you2.Inventory.WornOn(WearSlot.Body)?.Kind);
        Do(sim2, drive2, new WaitAction(20 * Minute));
        Assert.Equal(20, you2.Needs!.Wet);
    }

    [Fact]
    public void WetAndWind_ChillYou_ButARoofKeepsThemOff()
    {
        var storm = new WeatherSettings(RainPercent: 70, WindSpeed: 8);
        var (sim, drive, you) = Person(weather: storm);
        sim.SetBaseTemperature(16);
        Do(sim, drive, new WaitAction(2 * Hour));
        Assert.True(sim.NeedStage(you, NeedKind.Warmth) >= 2); // cold

        var roofed = new WorldMap(20, 10);
        roofed.Structures.SetRoof(Start, StructureMap.DefaultRoofDm);
        var (sim2, drive2, you2) = Person(roofed, storm);
        sim2.SetBaseTemperature(16);
        Do(sim2, drive2, new WaitAction(2 * Hour));
        Assert.Equal(0, you2.Needs!.Wet);
        Assert.True(sim2.NeedStage(you2, NeedKind.Warmth) <= 1);
    }

    [Fact]
    public void AFire_WarmsYou()
    {
        var (sim, _, you) = Person();
        sim.SetBaseTemperature(10);
        Assert.Equal(100, sim.FeelsLike(you));
        var by = new TilePos(3, 5);
        sim.SpillFuel(by);
        Assert.True(sim.TryIgnite(by, FireRules.LighterHeat, "a test"));
        Assert.Equal(100 + NeedRules.FireCloseWarmth, sim.FeelsLike(you));
    }

    [Fact]
    public void ColdKills()
    {
        var (sim, drive, you) = Person();
        sim.SetBaseTemperature(0);
        sim.SetNeeds(you, chill: NeedRules.DiedOfColdAt - 10);
        for (int i = 0; i < 10 && !you.IsDead; i++)
            Do(sim, drive, new WaitAction(5 * Minute));
        Assert.Equal("died of cold", you.Health.DeathCause);
    }

    [Fact]
    public void Shivering_MakesYourHandsClumsy()
    {
        var (sim, _, you) = Person();
        sim.SetNeeds(you, chill: NeedRules.WarmthStages[2]);
        Assert.Equal(3, sim.NeedStage(you, NeedKind.Warmth));
        Assert.Equal(130, sim.NeedEffectsOf(you).HandsPercent);
    }

    // ---------- water ----------

    [Fact]
    public void ABottleFilledFromAStream_IsUntreated_UntilATabletGoesIn()
    {
        var (sim, drive, you) = Person(WithStream());
        Assert.Equal(WaterSource.Fresh, sim.WaterSourceAt(new TilePos(3, 5)));
        var bottle = sim.CreateItem(CoreItems.WaterBottle, amount: 0);
        Assert.True(sim.GiveItem(you, bottle));

        Do(sim, drive, new FillAction(bottle.Id, Direction.East));
        Assert.Equal(1000, bottle.Amount);
        Assert.True(bottle.Unsafe);
        Assert.Contains("(untreated)", bottle.Describe());

        var tablets = sim.CreateItem(CoreItems.WaterTablets);
        Assert.True(sim.GiveItem(you, tablets));
        Do(sim, drive, new UseItemAction(tablets.Id));
        Assert.False(bottle.Unsafe);
    }

    [Fact]
    public void Seawater_WontDo()
    {
        var map = WithStream();
        map.Water[new TilePos(3, 5)] = new WaterTile(WaterKind.Sea, 0, CoverType.Sand, 0, null);
        var (sim, drive, you) = Person(map);
        Assert.Equal(WaterSource.Salt, sim.WaterSourceAt(new TilePos(3, 5)));
        sim.SetNeeds(you, thirstMl: 1000);
        Do(sim, drive, new DrinkFromAction(Direction.East));
        Assert.Equal(1000, you.Needs!.ThirstMl);
    }

    [Fact]
    public void BadWater_CanMakeYouIll_HoursLater_ForADayAndAHalf()
    {
        var (sim, drive, you) = Person(WithStream());
        for (int i = 0; i < 60 && !you.Needs!.GutInfected(sim.CurrentTick); i++)
        {
            sim.SetNeeds(you, thirstMl: 1000);
            Do(sim, drive, new DrinkFromAction(Direction.East));
        }
        Assert.True(you.Needs!.GutInfected(sim.CurrentTick));
        Assert.Equal(0, sim.NeedStage(you, NeedKind.Sickness)); // not yet

        Endure(sim, drive, you, 7);
        Assert.Equal(1, sim.NeedStage(you, NeedKind.Sickness)); // queasy
        Endure(sim, drive, you, 4);
        Assert.Equal(2, sim.NeedStage(you, NeedKind.Sickness)); // sick
        Endure(sim, drive, you, 36);
        Assert.Equal(0, sim.NeedStage(you, NeedKind.Sickness));
        Assert.False(you.IsDead);
    }

    // ---------- wounds ----------

    [Fact]
    public void ADirtyWound_GoesBad_AndAntibioticsCureIt()
    {
        var (sim, drive, you) = Person();
        sim.Injure(you, BodySection.LeftLeg, Severity.Red, bleeding: false, "a test");
        Assert.Equal(1, sim.UncleanedWounds(you));
        Endure(sim, drive, you, 30);
        Assert.Equal(1, sim.DirtyWounds(you));
        Assert.True(sim.NeedStage(you, NeedKind.Sickness) >= 2);

        var pills = sim.CreateItem(CoreItems.Antibiotics);
        Assert.True(sim.GiveItem(you, pills));
        Do(sim, drive, new UseItemAction(pills.Id));
        Endure(sim, drive, you, 18);
        Assert.Equal(0, sim.NeedStage(you, NeedKind.Sickness));
        Assert.Equal(0, sim.DirtyWounds(you));
    }

    [Fact]
    public void CleaningAWound_KeepsItFromGoingBad()
    {
        var (sim, drive, you) = Person();
        sim.Injure(you, BodySection.LeftLeg, Severity.Red, bleeding: false, "a test");
        var antiseptic = sim.CreateItem(CoreItems.Antiseptic);
        Assert.True(sim.GiveItem(you, antiseptic));
        Do(sim, drive, new UseItemAction(antiseptic.Id));
        Assert.Equal(0, sim.UncleanedWounds(you));
        Endure(sim, drive, you, 30);
        Assert.Equal(0, sim.NeedStage(you, NeedKind.Sickness));
    }
}
