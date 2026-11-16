using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Injuries;
using BlueHarvest.Sim.World;
using Xunit;

namespace BlueHarvest.Sim.Tests;

/// <summary>Step 22a: hunger, thirst, sleep, pain and stamina.</summary>
public class NeedsTests
{
    private const int Minute = WorldClock.TicksPerGameMinute;

    private static (Simulation Sim, PlayerController Drive, Actor You) Person(NeedsStart? start = null, int width = 40, bool needs = true)
    {
        var sim = new Simulation(new WorldMap(width, 10), seed: 1);
        sim.SetWeather(WeatherSettings.Clear); // no thunder to wake anyone
        var drive = new PlayerController();
        var you = sim.AddActor("You", '@', new TilePos(2, 5), Species.Human, drive, Direction.East);
        if (needs)
            sim.UseNeeds(you, start ?? new NeedsStart(HungerKcal: 0, ThirstMl: 0, AwakeMinutes: 0));
        sim.RunUntilInputNeeded();
        return (sim, drive, you);
    }

    private static void Do(Simulation sim, PlayerController drive, ActorAction action)
    {
        drive.Pending = action;
        sim.RunUntilInputNeeded();
    }

    [Fact]
    public void WithoutNeeds_NothingIsTracked()
    {
        var (sim, drive, you) = Person(needs: false);
        Do(sim, drive, new WaitAction(1440 * Minute));
        Assert.Null(you.Needs);
        Assert.True(sim.HasBreathToSprint(you));
        Assert.Equal(NeedEffects.None, sim.NeedEffectsOf(you));
    }

    [Fact]
    public void ADayAwake_CostsAbout2000Kcal_And2400Ml()
    {
        var (sim, drive, you) = Person();
        Do(sim, drive, new WaitAction(1440 * Minute));
        var needs = you.Needs!;
        Assert.Equal(2016, needs.HungerKcal);
        Assert.Equal(2448, needs.ThirstMl);
        Assert.Equal(1440, needs.Fatigue);
        Assert.Equal(1, sim.NeedStage(you, NeedKind.Hunger)); // hungry
        Assert.Equal(2, sim.NeedStage(you, NeedKind.Thirst)); // parched
        Assert.Equal(2, sim.NeedStage(you, NeedKind.Sleep)); // exhausted
    }

    [Fact]
    public void ThirstComesOnInTheAfternoon_AndIsReported()
    {
        var (sim, drive, you) = Person();
        var changes = new List<(NeedKind, int, int)>();
        sim.NeedChanged += (_, kind, stage, was) => changes.Add((kind, stage, was));
        Do(sim, drive, new WaitAction(470 * Minute));
        Assert.Equal(0, sim.NeedStage(you, NeedKind.Thirst));
        Do(sim, drive, new WaitAction(Minute));
        Assert.Equal(1, sim.NeedStage(you, NeedKind.Thirst));
        Assert.Contains((NeedKind.Thirst, 1, 0), changes);
        Assert.Equal("Thirsty", NeedRules.Word(NeedKind.Thirst, 1));
    }

    [Fact]
    public void EatingAndDrinking_MakeUpWhatsShort_AndCoffeeKeepsYouUp()
    {
        var (sim, drive, you) = Person(new NeedsStart(HungerKcal: 1000, ThirstMl: 1000, AwakeMinutes: 600));
        var water = sim.CreateItem(ItemKind.WaterBottle);
        var bar = sim.CreateItem(ItemKind.EnergyBar);
        var coffee = sim.CreateItem(ItemKind.Thermos);
        Assert.True(sim.GiveItem(you, water));
        Assert.True(sim.GiveItem(you, bar));
        Assert.True(sim.GiveItem(you, coffee));

        Do(sim, drive, new UseItemAction(water.Id));
        Assert.Equal(750, you.Needs!.ThirstMl);
        Do(sim, drive, new UseItemAction(bar.Id));
        Assert.InRange(you.Needs.HungerKcal, 750, 752); // a few seconds' hunger on top
        Do(sim, drive, new UseItemAction(coffee.Id));
        Assert.InRange(you.Needs.ThirstMl, 500, 503);
        Assert.InRange(you.Needs.Fatigue, 480, 482);
    }

    [Fact]
    public void Thirst_Kills()
    {
        var (sim, drive, you) = Person(new NeedsStart(HungerKcal: 0, ThirstMl: 5900, AwakeMinutes: 0));
        var died = new List<string>();
        sim.ActorDied += (_, cause) => died.Add(cause);
        for (int i = 0; i < 10 && !you.IsDead; i++) // a blackout may cut a wait short
            Do(sim, drive, new WaitAction(60 * Minute));
        Assert.True(you.IsDead);
        Assert.Equal("died of thirst", Assert.Single(died));
    }

    [Fact]
    public void GoingWithoutSleep_Kills()
    {
        var (sim, drive, you) = Person(new NeedsStart(HungerKcal: 0, ThirstMl: 0, AwakeMinutes: NeedRules.DiedOfExhaustionAt - 1));
        for (int i = 0; i < 10 && !you.IsDead; i++)
            Do(sim, drive, new WaitAction(2 * Minute));
        Assert.Equal("died of exhaustion", you.Health.DeathCause);
    }

    [Fact]
    public void Sprinting_SpendsBreath_UntilYouCantSprint()
    {
        var (sim, drive, you) = Person(width: 100);
        for (int i = 0; i < 46; i++)
            Do(sim, drive, new MoveAction(Direction.East, Sprint: true));
        Assert.Equal(NeedRules.MaxStamina - 46 * NeedRules.SprintStepCost, you.Needs!.Stamina);
        Assert.False(sim.HasBreathToSprint(you));
        Assert.Equal(2, sim.NeedStage(you, NeedKind.Stamina));

        int before = you.Needs.Stamina;
        Do(sim, drive, new MoveAction(Direction.East, Sprint: true)); // a walk: nothing spent, a little back
        Assert.True(you.Needs.Stamina > before);
    }

    [Fact]
    public void Breath_ComesBack_InAMinuteStandingStill()
    {
        var (sim, drive, you) = Person();
        sim.SetNeeds(you, stamina: 0);
        Do(sim, drive, new WaitAction(NeedRules.RegenFullTicks));
        Assert.Equal(NeedRules.MaxStamina, you.Needs!.Stamina);
    }

    [Fact]
    public void Hunger_SlowsYou_AndCapsYourBreath()
    {
        var (sim, _, you) = Person(new NeedsStart(HungerKcal: 4000, ThirstMl: 3500, AwakeMinutes: 0));
        var effects = sim.NeedEffectsOf(you);
        Assert.Equal(125, effects.StepCostPercent); // weak (+10) and dehydrated (+15)
        Assert.Equal(500, effects.StaminaCap);
        Assert.Equal(2, effects.BlackoutPercent);
    }

    [Fact]
    public void Wounds_Hurt_AndPainkillersDullThem_ForFourHours()
    {
        var (sim, drive, you) = Person();
        sim.Injure(you, BodySection.LeftLeg, Severity.Red, bleeding: false, "a test");
        Do(sim, drive, new WaitAction(1));
        Assert.Equal(2, sim.NeedStage(you, NeedKind.Pain)); // hurting

        var pills = sim.CreateItem(ItemKind.Painkillers);
        Assert.True(sim.GiveItem(you, pills));
        Do(sim, drive, new UseItemAction(pills.Id));
        Assert.Equal(0, sim.NeedStage(you, NeedKind.Pain));
        Assert.Equal(18, pills.Amount);

        Do(sim, drive, new WaitAction(NeedRules.PainkillerTicks));
        Assert.Equal(2, sim.NeedStage(you, NeedKind.Pain));
    }

    [Fact]
    public void Adrenaline_RefillsBreath_AndShutsOutPain()
    {
        var (sim, drive, you) = Person(new NeedsStart(HungerKcal: 0, ThirstMl: 0, AwakeMinutes: 600));
        sim.Injure(you, BodySection.LeftLeg, Severity.Red, bleeding: false, "a test");
        sim.SetNeeds(you, stamina: 0);
        var pen = sim.CreateItem(ItemKind.EpiPen);
        Assert.True(sim.GiveItem(you, pen));
        Do(sim, drive, new UseItemAction(pen.Id));
        Assert.Equal(NeedRules.MaxStamina, you.Needs!.Stamina);
        Assert.Equal(0, sim.NeedStage(you, NeedKind.Pain));
        Assert.Equal("your heart is still pounding", sim.SleepBlock(you));
    }

    [Fact]
    public void YouCantSleep_UntilYoureTired()
    {
        var (sim, drive, you) = Person(new NeedsStart(HungerKcal: 0, ThirstMl: 0, AwakeMinutes: 100));
        Assert.Equal("you aren't tired enough to sleep", sim.SleepBlock(you));
        Do(sim, drive, new SleepAction(Minute));
        Assert.False(you.Needs!.Asleep);
    }

    [Fact]
    public void Sleep_PaysBackTwoForOne_AndYouWakeRested()
    {
        var (sim, drive, you) = Person(new NeedsStart(HungerKcal: 0, ThirstMl: 0, AwakeMinutes: 480));
        var woke = new List<string>();
        sim.WokeUp += (_, why) => woke.Add(why);
        Do(sim, drive, new SleepAction(300));
        Assert.True(you.Needs!.Asleep);
        Assert.Equal(0, sim.SensesOf(you).SightRangeTiles);

        long start = sim.CurrentTick;
        for (int i = 0; i < 1000 && you.Needs.Asleep; i++)
            Do(sim, drive, new SleepAction(300));
        Assert.False(you.Needs.Asleep);
        Assert.Equal("rested", Assert.Single(woke));
        Assert.Equal(0, you.Needs.Fatigue);
        Assert.InRange((sim.CurrentTick - start) / Minute, 239, 241); // four hours for eight
    }

    [Fact]
    public void ALoudNoise_WakesASleeper()
    {
        var (sim, drive, you) = Person(new NeedsStart(HungerKcal: 0, ThirstMl: 0, AwakeMinutes: 600));
        var woke = new List<string>();
        sim.WokeUp += (_, why) => woke.Add(why);
        Do(sim, drive, new SleepAction(300));
        Do(sim, drive, new SleepAction(300));
        Assert.True(you.Needs!.Asleep); // nothing to hear

        sim.EmitNoise(new TilePos(5, 5), 30, "a test");
        Do(sim, drive, new SleepAction(300));
        Assert.False(you.Needs.Asleep);
        Assert.Equal("a noise: a test", Assert.Single(woke));
    }

    [Fact]
    public void BeingHurt_WakesASleeper()
    {
        var (sim, drive, you) = Person(new NeedsStart(HungerKcal: 0, ThirstMl: 0, AwakeMinutes: 600));
        Do(sim, drive, new SleepAction(300));
        sim.Injure(you, BodySection.LeftArm, Severity.Yellow, bleeding: false, "a test");
        Assert.False(you.Needs!.Asleep);
    }

    [Fact]
    public void BadPain_KeepsYouAwake_UnlessYoureExhausted()
    {
        var (sim, _, you) = Person(new NeedsStart(HungerKcal: 0, ThirstMl: 0, AwakeMinutes: 600));
        sim.Injure(you, BodySection.LeftLeg, Severity.Red, bleeding: false, "a test");
        Assert.Equal("it hurts too much to sleep", sim.SleepBlock(you));
        sim.SetNeeds(you, awakeMinutes: 1500);
        Assert.Null(sim.SleepBlock(you));
    }

    [Fact]
    public void TheDayStarts_AwakeSinceSeven()
    {
        var sim = new Simulation(new WorldMap(10, 10), seed: 1);
        sim.SetTimeOfDay(13);
        var you = sim.AddActor("You", '@', new TilePos(2, 5), Species.Human, new PlayerController(), Direction.East);
        sim.UseNeeds(you);
        Assert.Equal(360, you.Needs!.Fatigue);
        Assert.Equal(300, you.Needs.HungerKcal);
    }
}
