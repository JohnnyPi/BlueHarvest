using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.Injuries;
using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.World;
using Xunit;

namespace BlueHarvest.Sim.Tests;

/// <summary>Wounds, bleeding, first aid and death.</summary>
public class HealthTests
{
    /// <summary>A human the test drives, alone on open ground at (5,5).</summary>
    private static (Simulation Sim, PlayerController Drive, Actor Human) Alone(Loadout? loadout = null)
    {
        var sim = new Simulation(new WorldMap(20, 20), seed: 3);
        var drive = new PlayerController();
        var human = sim.AddActor("You", '@', new TilePos(5, 5), Species.Human, drive, loadout: loadout);
        sim.RunUntilInputNeeded();
        return (sim, drive, human);
    }

    private static StepResult Do(Simulation sim, PlayerController drive, ActorAction action)
    {
        drive.Pending = action;
        return sim.RunUntilInputNeeded();
    }

    [Fact]
    public void ASecondMajorWound_ToTheSameSection_Kills()
    {
        var (sim, _, human) = Alone();

        sim.Injure(human, BodySection.Torso, Severity.Red, bleeding: false, "test");
        Assert.False(human.IsDead);
        sim.Injure(human, BodySection.LeftArm, Severity.Red, bleeding: false, "test");
        Assert.False(human.IsDead);
        sim.Injure(human, BodySection.Torso, Severity.Red, bleeding: false, "a falling crate");

        Assert.True(human.IsDead);
        Assert.Equal("a falling crate: a second major wound to the torso", human.Health.DeathCause);
        Assert.Null(sim.ActorAt(new TilePos(5, 5))); // the body doesn't block the way
        Assert.Equal(StepResult.PlayerDied, sim.RunUntilInputNeeded());
    }

    [Fact]
    public void TwoMinorWounds_MakeAMajorOne()
    {
        var (sim, _, human) = Alone();

        sim.Injure(human, BodySection.LeftLeg, Severity.Yellow, bleeding: false, "test");
        Assert.Equal(Severity.Yellow, human.Health[BodySection.LeftLeg].Severity);
        sim.Injure(human, BodySection.LeftLeg, Severity.Yellow, bleeding: false, "test");
        Assert.Equal(Severity.Red, human.Health[BodySection.LeftLeg].Severity);
        Assert.False(human.IsDead);
    }

    [Fact]
    public void Bleeding_RunsOut_AfterNinetySeconds()
    {
        var sim = new Simulation(new WorldMap(20, 20), seed: 3);
        var human = sim.AddActor("Pete", 'h', new TilePos(5, 5), Species.Human, new HoldStillController());
        sim.Injure(human, BodySection.Torso, Severity.Red, bleeding: true, "test");
        var died = new List<string>();
        sim.ActorDied += (_, cause) => died.Add(cause);

        sim.RunUntil(Ticks.FromSeconds(85));
        Assert.False(human.IsDead);
        Assert.InRange(human.Health.BleedTicksLeft(sim.CurrentTick)!.Value, 0, 600);

        sim.RunUntil(Ticks.FromSeconds(95));
        Assert.True(human.IsDead);
        Assert.Equal(new[] { "bled out" }, died);
    }

    [Fact]
    public void FirstAid_StopsTheBleeding_AndUsesTheKit()
    {
        var expert = new Loadout("medic", new Skills(FirstAid: 3), new[] { (ItemKind.FirstAidKit, 1) });
        var (sim, drive, human) = Alone(expert);
        sim.Injure(human, BodySection.RightLeg, Severity.Red, bleeding: true, "test");

        Do(sim, drive, new TreatAction());

        Assert.Equal(new Wound(Severity.Yellow, false, 0), human.Health[BodySection.RightLeg]);
        Assert.Null(human.Health.BleedTicksLeft(sim.CurrentTick));
        Assert.Equal(0, human.Inventory.Count(ItemKind.FirstAidKit));
        Assert.Equal(InjuryRules.TreatTicks(3), human.NextActTick);
    }

    [Fact]
    public void FirstAid_NeedsAKit()
    {
        var (sim, drive, human) = Alone();
        sim.Injure(human, BodySection.RightLeg, Severity.Red, bleeding: true, "test");
        var blocked = new List<string>();
        sim.MoveBlocked += (_, reason) => blocked.Add(reason);

        Do(sim, drive, new TreatAction());

        Assert.Equal(new[] { "no first-aid kit" }, blocked);
        Assert.True(human.Health[BodySection.RightLeg].Bleeding);
    }

    [Fact]
    public void YellowWounds_HealWithTime()
    {
        var sim = new Simulation(new WorldMap(20, 20), seed: 3);
        var human = sim.AddActor("Pete", 'h', new TilePos(5, 5), Species.Human, new HoldStillController());
        sim.Injure(human, BodySection.LeftArm, Severity.Yellow, bleeding: false, "test");

        sim.RunUntil(Ticks.FromSeconds(181));

        Assert.Equal(Severity.Green, human.Health[BodySection.LeftArm].Severity);
    }

    [Theory]
    [InlineData(Severity.Green, 70)]
    [InlineData(Severity.Yellow, 91)]  // +30%
    [InlineData(Severity.Red, 140)]    // a limp: twice as slow
    public void HurtLegs_SlowYouDown(Severity leg, int expectedTicks)
    {
        var (sim, drive, human) = Alone();
        sim.Injure(human, BodySection.LeftLeg, leg, bleeding: false, "test");

        Do(sim, drive, new MoveAction(Direction.East));

        Assert.Equal(expectedTicks, human.NextActTick);
    }

    [Fact]
    public void BleedingLeavesATrail()
    {
        var (sim, drive, human) = Alone();
        sim.Injure(human, BodySection.Torso, Severity.Red, bleeding: true, "test");

        Do(sim, drive, new MoveAction(Direction.East));
        Do(sim, drive, new MoveAction(Direction.East));

        Assert.Equal(new[] { new TilePos(5, 5), new TilePos(6, 5) }, sim.BloodSpots.Select(b => b.Position));
    }

    [Fact]
    public void ABrokenArm_StopsYouClimbing()
    {
        var map = new WorldMap(10, 10);
        map.Structures.SetDeck(new TilePos(5, 5), 60);
        map.Structures.SetLadder(new TilePos(5, 5), true);
        var sim = new Simulation(map, seed: 1);
        var drive = new PlayerController();
        var human = sim.AddActor("You", '@', new TilePos(5, 5), Species.Human, drive);
        sim.RunUntilInputNeeded();
        sim.Injure(human, BodySection.RightArm, Severity.Red, bleeding: false, "test");
        var blocked = new List<string>();
        sim.MoveBlocked += (_, reason) => blocked.Add(reason);

        Do(sim, drive, new ClimbAction());

        Assert.False(human.OnDeck);
        Assert.Equal(new[] { "too badly hurt to climb" }, blocked);
    }

    [Fact]
    public void AConcussion_HalvesSightRange()
    {
        var (sim, _, human) = Alone();
        sim.Injure(human, BodySection.Head, Severity.Red, bleeding: false, "test");

        var senses = sim.SensesOf(human);

        Assert.Equal(Species.Human.Senses.SightRangeTiles / 2, senses.SightRangeTiles);
        Assert.Equal(Species.Human.Senses.HearingThreshold + InjuryRules.ConcussionHearingPenalty, senses.HearingThreshold);
    }

    [Fact]
    public void ARexBite_IsTwoBleedingMajorWounds()
    {
        var sim = new Simulation(new WorldMap(30, 30), seed: 1);
        var drive = new PlayerController();
        sim.AddActor("Rex", 'T', new TilePos(10, 10), Species.Rex, drive, Direction.South);
        var pete = sim.AddActor("Pete", 'h', new TilePos(10, 14), Species.Human, new HoldStillController());
        sim.RunUntilInputNeeded();

        Do(sim, drive, new BiteAction());

        var red = Enum.GetValues<BodySection>().Where(s => pete.Health[s].Severity == Severity.Red).ToList();
        Assert.Equal(2, red.Count);
        Assert.All(red, s => Assert.True(pete.Health[s].Bleeding));
        Assert.False(pete.IsDead);
    }

    [Fact]
    public void TheDead_TakeNoTurns_AndArentSeen()
    {
        var sim = new Simulation(new WorldMap(40, 40), seed: 1);
        var watcher = sim.AddActor("Rex", 'T', new TilePos(20, 30), Species.Rex, new HoldStillController(), Direction.North);
        var pete = sim.AddActor("Pete", 'h', new TilePos(20, 20), Species.Human, new HoldStillController());
        sim.Injure(pete, BodySection.Head, Severity.Red, bleeding: false, "test");
        sim.Injure(pete, BodySection.Head, Severity.Red, bleeding: false, "test");
        long next = pete.NextActTick;

        sim.RunUntil(Ticks.FromSeconds(5));

        Assert.True(pete.IsDead);
        Assert.Equal(next, pete.NextActTick);
        Assert.Null(PerceptionSystem.Perceive(sim, watcher, 0).ContactWith(pete));
    }
}
