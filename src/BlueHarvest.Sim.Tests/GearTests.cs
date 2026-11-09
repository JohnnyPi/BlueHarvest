using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Ai;
using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.Injuries;
using BlueHarvest.Sim.Lighting;
using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.World;
using Xunit;

namespace BlueHarvest.Sim.Tests;

/// <summary>Flares, the flashlight, rope, climbing steps and sprinting.</summary>
public class GearTests
{
    private static readonly Loadout Expert = new("expert",
        new Skills(Climbing: 3, Rappelling: 3, Throwing: 3),
        new[] { (ItemKind.Flare, 2), (ItemKind.Flashlight, 1), (ItemKind.Rope, 1) });

    /// <summary>A human the test drives at <paramref name="at"/>, on a 30×30 map (night unless given).</summary>
    private static (Simulation Sim, PlayerController Drive, Actor Human) Person(WorldMap? map = null, TilePos? at = null,
        Loadout? loadout = null, int ambient = 6)
    {
        var sim = new Simulation(map ?? new WorldMap(30, 30), seed: 5) { AmbientLightPercent = ambient };
        var drive = new PlayerController();
        var human = sim.AddActor("You", '@', at ?? new TilePos(5, 5), Species.Human, drive, Direction.East,
            loadout: loadout ?? Expert);
        sim.RunUntilInputNeeded();
        return (sim, drive, human);
    }

    private static void Do(Simulation sim, PlayerController drive, ActorAction action)
    {
        drive.Pending = action;
        sim.RunUntilInputNeeded();
    }

    [Fact]
    public void AFlare_LandsWhereAimed_LightsUp_AndBurnsOut()
    {
        var (sim, drive, human) = Person();
        var landed = new List<TilePos>();
        sim.ItemThrown += (_, _, at) => landed.Add(at);
        int before = sim.Light.LevelAt(new TilePos(10, 5));

        Do(sim, drive, new ThrowAction(ItemKind.Flare, new TilePos(10, 5)));

        Assert.Equal(new[] { new TilePos(10, 5) }, landed); // expert at 5 tiles: no scatter
        Assert.Equal(1, human.Inventory.Count(ItemKind.Flare));
        Assert.True(sim.Light.LevelAt(new TilePos(10, 5)) > before + 50);
        Assert.Contains(sim.RecentNoises, n => n.Kind == NoiseKinds.Flare && n.ActorId is null);

        sim.SetController(human, new HoldStillController()); // so time can pass without input
        sim.RunUntil(GearRules.FlareBurnTicks + 200);
        Assert.False(sim.Lights[^1].On);
        Assert.Equal(before, sim.Light.LevelAt(new TilePos(10, 5)));
    }

    [Fact]
    public void Throws_FallShortPastTheirRange()
    {
        var (sim, drive, human) = Person();
        var landed = new List<TilePos>();
        sim.ItemThrown += (_, _, at) => landed.Add(at);
        int range = GearRules.ThrowRange(human.Skills.Throwing, human.Health); // 15 for an expert

        Do(sim, drive, new ThrowAction(ItemKind.Flare, new TilePos(29, 5)));

        var at = Assert.Single(landed);
        Assert.InRange(at.X - 5, range - GearRules.Scatter(range, 3), range + GearRules.Scatter(range, 3));
    }

    /// <summary>
    /// The Rex at (20,30) facing north has its eyes at its head tip, (20,26), and a 120° view. A flare
    /// at (20,15) is 11 tiles ahead of it (score 64).
    /// </summary>
    private static (Simulation Sim, CreatureBrain Brain, PlayerController Drive, Actor Human) RexAndThrower(TilePos thrower)
    {
        var map = new WorldMap(40, 40);
        var sim = new Simulation(map, seed: 5) { AmbientLightPercent = 6 };
        var brain = new CreatureBrain(AiProfile.Rex, sim.Rng.Fork("ai:rex"));
        sim.AddActor("Rex", 'T', new TilePos(20, 30), Species.Rex, brain, Direction.North);
        var drive = new PlayerController();
        var human = sim.AddActor("You", '@', thrower, Species.Human, drive, loadout: Expert);
        sim.RunUntilInputNeeded();
        return (sim, brain, drive, human);
    }

    [Fact]
    public void AFlare_DrawsTheRex()
    {
        // Off to its side (dx 13, dy -2 from its eyes), outside its 120° view: it never sees the throw.
        var (sim, brain, drive, _) = RexAndThrower(new TilePos(33, 24));

        Do(sim, drive, new ThrowAction(ItemKind.Flare, new TilePos(20, 15)));
        sim.RunUntil(sim.CurrentTick + 100);

        Assert.Equal(new ContactKey(ContactSource.Light, 0), brain.Focus?.Key);
        Assert.Equal(Activity.Investigating, brain.Activity);
    }

    [Fact]
    public void ThrowingInItsView_GivesYouAway()
    {
        // In its view 17 tiles off. A throw is motion, so it sees the thrower (score 17, needs 10),
        // and prey outranks a light.
        var (sim, brain, drive, human) = RexAndThrower(new TilePos(30, 12));

        Do(sim, drive, new ThrowAction(ItemKind.Flare, new TilePos(20, 15)));
        sim.RunUntil(sim.CurrentTick + 100);

        Assert.Equal(ContactKey.ForActor(human.Id), brain.Focus?.Key);
        Assert.Equal(ContactClass.Prey, brain.Focus?.Class);
    }

    [Fact]
    public void TheFlashlight_FollowsYou_AndSwitchesOff()
    {
        var (sim, drive, human) = Person();

        Do(sim, drive, new ToggleLightAction());
        var index = Assert.IsType<int>(human.CarriedLight);
        Assert.True(sim.Lights[index].On);
        Assert.True(sim.Light.LevelAt(new TilePos(9, 5)) > 30); // the beam, 4 tiles ahead

        Do(sim, drive, new MoveAction(Direction.South));
        Assert.Equal(new TilePos(5, 6), sim.Lights[index].Position);
        Assert.Equal(Direction.South, sim.Lights[index].Facing);

        Do(sim, drive, new ToggleLightAction());
        Assert.False(sim.Lights[index].On);
        Assert.Equal(6, sim.Light.LevelAt(new TilePos(5, 10)));
    }

    /// <summary>A map whose west half (x 0-4) is an 8 m ledge above level ground.</summary>
    private static WorldMap Ledge(int heightDm)
    {
        var map = new WorldMap(12, 12);
        for (int y = 0; y < 12; y++)
            for (int x = 0; x < 5; x++)
                map.Heights[new TilePos(x, y)] = (short)heightDm;
        return map;
    }

    [Fact]
    public void Rope_TakesYouDownADropTooFarToJump()
    {
        var (sim, drive, human) = Person(Ledge(80), new TilePos(4, 5));
        Assert.Equal("too far to drop (8.0 m)", sim.MoveBlockReason(human, Direction.East));

        Do(sim, drive, new RappelAction(Direction.East));

        Assert.Equal(new TilePos(5, 5), human.Position);
        Assert.Equal(0, human.Inventory.Count(ItemKind.Rope));
        Assert.Equal(new RopeSpot(new TilePos(4, 5), new TilePos(5, 5)), Assert.Single(sim.Ropes));
        Assert.All(Enum.GetValues<BodySection>(), s => Assert.Equal(Severity.Green, human.Health[s].Severity));
        Assert.Equal(GearRules.RappelTicks(80, 3), human.NextActTick);
    }

    [Fact]
    public void Rope_IsntForShortDrops_OrBrokenArms()
    {
        var (sim, drive, human) = Person(Ledge(15), new TilePos(4, 5));
        var blocked = new List<string>();
        sim.MoveBlocked += (_, reason) => blocked.Add(reason);

        Do(sim, drive, new RappelAction(Direction.East));
        sim.Injure(human, BodySection.LeftArm, Severity.Red, bleeding: false, "test");
        Do(sim, drive, new RappelAction(Direction.West));

        Assert.Equal(new[] { "no need for a rope here", "too badly hurt to rappel" }, blocked);
        Assert.Equal(new TilePos(4, 5), human.Position);
    }

    [Theory]
    [InlineData(3, 0, 0)]
    [InlineData(0, 0, 15)]
    [InlineData(1, 1, 25)]  // a sprained wrist makes it a gamble
    [InlineData(1, 2, 50)]
    public void RappelOdds(int skill, int yellowArms, int failPercent)
    {
        Assert.Equal(failPercent, GearRules.RappelFailPercent(skill, yellowArms));
    }

    [Theory]
    [InlineData(20, true)]  // 2 m: a climb
    [InlineData(25, false)] // 2.5 m: too high
    public void People_ClimbStepsUpToTwoMetres(int riseDm, bool climbs)
    {
        var map = new WorldMap(12, 12);
        for (int y = 0; y < 12; y++)
            for (int x = 6; x < 12; x++)
                map.Heights[new TilePos(x, y)] = (short)riseDm;
        var (sim, drive, human) = Person(map, new TilePos(5, 5));

        Do(sim, drive, new MoveAction(Direction.East));

        Assert.Equal(climbs ? new TilePos(6, 5) : new TilePos(5, 5), human.Position);
        if (climbs)
        {
            Assert.Equal(GearRules.ClimbStepTicks(riseDm, 3), human.NextActTick);
            Assert.Contains(sim.RecentNoises, n => n.Kind == NoiseKinds.Scrambling);
        }
    }

    [Theory]
    [InlineData(Severity.Green, 42)]   // 60% of 70
    [InlineData(Severity.Yellow, 56)]  // winded: 80%
    public void Sprinting_IsFaster_AndLouder(Severity torso, int ticks)
    {
        var (sim, drive, human) = Person();
        sim.Injure(human, BodySection.Torso, torso, bleeding: false, "test");

        Do(sim, drive, new MoveAction(Direction.East, Sprint: true));

        Assert.Equal(ticks, human.NextActTick);
        Assert.Contains(sim.RecentNoises, n => n.Kind == NoiseKinds.Footsteps
            && n.Loudness == Species.Human.FootstepLoudness + GearRules.SprintLoudnessBonus);
    }

    [Fact]
    public void ABrokenLeg_MeansNoSprinting()
    {
        var (sim, drive, human) = Person();
        sim.Injure(human, BodySection.LeftLeg, Severity.Red, bleeding: false, "test");

        Do(sim, drive, new MoveAction(Direction.East, Sprint: true));

        Assert.Equal(140, human.NextActTick); // a limp, not a run
    }
}
