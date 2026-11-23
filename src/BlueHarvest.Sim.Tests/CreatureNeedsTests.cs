using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Ai;
using BlueHarvest.Sim.Injuries;
using BlueHarvest.Sim.World;
using Xunit;
using BlueHarvest.Sim.ContentIntegration;

namespace BlueHarvest.Sim.Tests;

/// <summary>Step 23b: hunger and sleep shaping dinosaurs and survivors, opted in per actor.</summary>
public class CreatureNeedsTests
{
    private const int Minute = WorldClock.TicksPerGameMinute;
    private const int Hour = 60 * Minute;

    private sealed class Waits(int ticks) : IActorController
    {
        public ActorAction? Decide(Actor self, Simulation sim) => new WaitAction(ticks);
    }

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

    private static Simulation Open(int width = 60, int height = 30, int? hour = null, WorldMap? map = null)
    {
        var sim = new Simulation(TestContent.NewSession(), map ?? new WorldMap(width, height), seed: 5);
        sim.SetWeather(WeatherSettings.Clear);
        if (hour is { } h)
            sim.SetTimeOfDay(h);
        return sim;
    }

    private static readonly NeedsStart Fresh = new(HungerKcal: 0, ThirstMl: 0, AwakeMinutes: 0);

    // ---------- profiles ----------

    [Fact]
    public void ARex_HungersAndThirstsSlowly_AndFeelsNoCold()
    {
        var sim = Open();
        sim.SetBaseTemperature(0);
        var rex = sim.AddActor("Rex", 'T', new TilePos(20, 15), Species.Rex, new Waits(24 * Hour), Direction.South);
        sim.UseNeeds(rex, Fresh);
        Assert.Equal(NeedProfile.Rex, rex.Needs!.Profile);
        sim.RunUntil(24 * Hour);
        Assert.Equal(288, rex.Needs.HungerKcal); // 14% of a person's 2016
        Assert.Equal(864, rex.Needs.ThirstMl);
        Assert.Equal(0, rex.Needs.Chill);
    }

    [Fact]
    public void VariedStarts_Differ_AndCreaturesStartAsleepInTheirHours()
    {
        var sim = Open(hour: 23);
        var rex = sim.AddActor("Rex", 'T', new TilePos(20, 15), Species.Rex, new Waits(Minute), Direction.South);
        var raptor = sim.AddActor("Raptor", 'R', new TilePos(40, 15), Species.Raptor, new Waits(Minute), Direction.West);
        sim.UseVariedNeeds(rex);
        sim.UseVariedNeeds(raptor);
        Assert.True(rex.Needs!.Asleep); // the Rex sleeps 21:00–05:00
        Assert.False(raptor.Needs!.Asleep); // raptors hunt by night
        Assert.InRange(rex.Needs.HungerKcal, 0, 3999);
        Assert.NotEqual((rex.Needs.HungerKcal, rex.Needs.ThirstMl), (raptor.Needs.HungerKcal, raptor.Needs.ThirstMl));
        Assert.Equal(rex.Needs.HungerKcal, sim.VariedStart(rex, NeedProfile.Rex).HungerKcal); // the same every time
    }

    // ---------- eating ----------

    [Fact]
    public void AKill_FillsTheBiter()
    {
        var sim = Open();
        var rex = sim.AddActor("Rex", 'T', new TilePos(20, 10), Species.Rex, new Once(new BiteAction()), Direction.South);
        sim.UseNeeds(rex, new NeedsStart(HungerKcal: 5000, ThirstMl: 0, AwakeMinutes: 0));
        var at = sim.BiteZone(rex).First(t => sim.Map.InBounds(t) && sim.ActorAt(t) is null);
        var victim = sim.AddActor("Victim", 'h', at, Species.Human, new Waits(Minute), Direction.North);
        foreach (var section in Enum.GetValues<BodySection>())
            sim.Injure(victim, section, Severity.Red, bleeding: false, "a test");
        var fed = new List<Actor>();
        sim.AteKill += (eater, _) => fed.Add(eater);

        sim.RunUntil(sim.CurrentTick + 10);
        Assert.True(victim.IsDead);
        Assert.Equal(rex, Assert.Single(fed));
        Assert.Equal(0, rex.Needs!.HungerKcal);
        Assert.Equal(30, CreatureBrain.HungerPercent(rex, sim.CurrentTick)); // lying up, digesting
    }

    [Fact]
    public void Hunger_ScalesHowMuchPreyMatters()
    {
        var sim = Open();
        var rex = sim.AddActor("Rex", 'T', new TilePos(20, 15), Species.Rex, new Waits(Minute), Direction.South);
        Assert.Equal(100, CreatureBrain.HungerPercent(rex, sim.CurrentTick)); // no needs: as tuned
        sim.UseNeeds(rex, Fresh);
        Assert.Equal(70, CreatureBrain.HungerPercent(rex, sim.CurrentTick));
        sim.SetNeeds(rex, hungerKcal: 1500);
        Assert.Equal(100, CreatureBrain.HungerPercent(rex, sim.CurrentTick));
        sim.SetNeeds(rex, hungerKcal: 4000);
        Assert.Equal(140, CreatureBrain.HungerPercent(rex, sim.CurrentTick));
        sim.SetNeeds(rex, hungerKcal: 10_000);
        Assert.Equal(180, CreatureBrain.HungerPercent(rex, sim.CurrentTick));
    }

    [Fact]
    public void AFedCreature_LiesUp()
    {
        var sim = Open(hour: 12);
        var brain = new CreatureBrain(AiProfile.Rex, sim.Rng.Fork("ai:rex"));
        var rex = sim.AddActor("Rex", 'T', new TilePos(20, 15), Species.Rex, brain, Direction.South);
        sim.UseNeeds(rex, Fresh);
        sim.SetNeeds(rex, fed: true);
        var start = rex.Position;
        sim.RunUntil(sim.CurrentTick + 10 * Minute);
        Assert.Equal(Activity.Resting, brain.Activity);
        Assert.Equal(start, rex.Position);
    }

    // ---------- sleep ----------

    [Fact]
    public void ARex_SleepsInItsHours_ThroughQuiet_ButALoudNoiseWakesIt()
    {
        var sim = Open(hour: 23);
        var brain = new CreatureBrain(AiProfile.Rex, sim.Rng.Fork("ai:rex"));
        var rex = sim.AddActor("Rex", 'T', new TilePos(20, 15), Species.Rex, brain, Direction.South);
        sim.UseNeeds(rex, new NeedsStart(HungerKcal: 0, ThirstMl: 0, AwakeMinutes: 900));
        sim.RunUntil(sim.CurrentTick + Minute);
        Assert.True(rex.Needs!.Asleep);
        Assert.Equal(Activity.Sleeping, brain.Activity);
        Assert.Equal("asleep", brain.Describe(sim));

        sim.RunUntil(sim.CurrentTick + 30 * Minute);
        Assert.True(rex.Needs.Asleep);

        sim.EmitNoise(new TilePos(24, 18), 60, "a test");
        sim.RunUntil(sim.CurrentTick + CreatureBrain.SleepTurnTicks); // its next turn: awake, and after the noise
        Assert.False(rex.Needs.Asleep);
        Assert.NotEqual(Activity.Sleeping, brain.Activity);
    }

    [Fact]
    public void Raptors_SleepByDay()
    {
        var noon = Open(hour: 12);
        var dozing = new CreatureBrain(AiProfile.Rex, noon.Rng.Fork("ai:r"));
        var raptor = noon.AddActor("Raptor", 'R', new TilePos(20, 15), Species.Raptor, dozing, Direction.South);
        noon.UseNeeds(raptor, new NeedsStart(HungerKcal: 0, ThirstMl: 0, AwakeMinutes: 600));
        noon.RunUntil(noon.CurrentTick + Minute);
        Assert.True(raptor.Needs!.Asleep);

        var dusk = Open(hour: 20);
        var awake = new CreatureBrain(AiProfile.Rex, dusk.Rng.Fork("ai:r"));
        var hunter = dusk.AddActor("Raptor", 'R', new TilePos(20, 15), Species.Raptor, awake, Direction.South);
        dusk.UseNeeds(hunter, new NeedsStart(HungerKcal: 0, ThirstMl: 0, AwakeMinutes: 600));
        dusk.RunUntil(dusk.CurrentTick + Minute);
        Assert.False(hunter.Needs!.Asleep);
    }

    [Fact]
    public void WithoutNeeds_ARexNeverSleeps()
    {
        var sim = Open(hour: 23);
        var brain = new CreatureBrain(AiProfile.Rex, sim.Rng.Fork("ai:rex"));
        var rex = sim.AddActor("Rex", 'T', new TilePos(20, 15), Species.Rex, brain, Direction.South);
        sim.RunUntil(sim.CurrentTick + 10 * Minute);
        Assert.Null(rex.Needs);
        Assert.NotEqual(Activity.Sleeping, brain.Activity);
    }

    // ---------- water ----------

    [Fact]
    public void AThirstyCreature_GoesToWater_AndDrinks()
    {
        var map = new WorldMap(60, 30);
        var water = new TilePos(30, 15);
        map.Cover[water] = CoverType.ShallowWater;
        var sim = Open(hour: 20, map: map);
        var brain = new CreatureBrain(AiProfile.Rex, sim.Rng.Fork("ai:r"));
        var raptor = sim.AddActor("Raptor", 'R', new TilePos(20, 15), Species.Raptor, brain, Direction.East);
        sim.UseNeeds(raptor, new NeedsStart(HungerKcal: 0, ThirstMl: 1500, AwakeMinutes: 60));
        // where it stood and where it drank, each time (once it's had enough it wanders off again)
        var drinks = new List<(TilePos From, TilePos At)>();
        sim.DrankWater += (who, at) => { if (who == raptor) drinks.Add((who.Position, at)); };
        long until = sim.CurrentTick + 2 * Hour;
        while (sim.CurrentTick < until && raptor.Needs!.ThirstMl >= 800)
            sim.RunUntil(sim.CurrentTick + Minute);
        Assert.True(raptor.Needs!.ThirstMl < 800);
        Assert.InRange(drinks.Count, 2, 3); // 1500 ml short, half a litre a drink: two (three if the walk was long)
        Assert.All(drinks, d =>
        {
            Assert.Equal(water, d.At);
            Assert.True(Math.Max(Math.Abs(d.From.X - water.X), Math.Abs(d.From.Y - water.Y)) <= 1);
        });
    }

    // ---------- survivors ----------

    private static (Simulation Sim, Actor Person) Survivor(NeedsStart start, int hour = 12)
    {
        var sim = Open(hour: hour);
        var person = sim.AddActor("Marisol", 'h', new TilePos(20, 15), Species.Human,
            new HumanBrain(Traits.Steady, AiProfile.Human, sim.Rng.Fork("ai:m")), Direction.South);
        sim.UseNeeds(person, start);
        return (sim, person);
    }

    [Fact]
    public void AThirstySurvivor_DrinksFromTheirOwnBottle_AndEats()
    {
        var (sim, person) = Survivor(new NeedsStart(HungerKcal: 2000, ThirstMl: 1000, AwakeMinutes: 60));
        var bottle = sim.CreateItem(CoreItems.WaterBottle);
        var bars = sim.CreateItem(CoreItems.EnergyBar, amount: 3);
        Assert.True(sim.GiveItem(person, bottle));
        Assert.True(sim.GiveItem(person, bars));
        sim.RunUntil(sim.CurrentTick + 5 * Minute);
        Assert.True(person.Needs!.ThirstMl < 800);
        Assert.True(bottle.Amount < 1000);
        Assert.True(bars.Amount < 3);
        Assert.True(person.Needs.HungerKcal < 1500);
    }

    [Fact]
    public void AHurtSurvivor_BindsTheBleed_AndTakesPainkillers()
    {
        var (sim, person) = Survivor(new NeedsStart(HungerKcal: 0, ThirstMl: 0, AwakeMinutes: 60));
        sim.Injure(person, BodySection.LeftLeg, Severity.Red, bleeding: true, "a test");
        Assert.True(sim.GiveItem(person, sim.CreateItem(CoreItems.Bandage)));
        Assert.True(sim.GiveItem(person, sim.CreateItem(CoreItems.Painkillers)));
        sim.RunUntil(sim.CurrentTick + 3 * Minute);
        Assert.Equal(0, person.Health.BleedingWounds);
        Assert.True(person.Needs!.OnPainkillers(sim.CurrentTick));
    }

    [Fact]
    public void AnExhaustedSurvivor_LiesDownToSleep()
    {
        var (sim, person) = Survivor(new NeedsStart(HungerKcal: 0, ThirstMl: 0, AwakeMinutes: 1500));
        var brain = (HumanBrain)person.Controller;
        sim.RunUntil(sim.CurrentTick + Minute);
        Assert.True(person.Needs!.Asleep);
        Assert.Equal(Mood.Sleeping, brain.Mood);
    }
}
