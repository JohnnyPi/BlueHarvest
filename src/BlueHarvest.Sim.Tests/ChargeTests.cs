using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Ai;
using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.Lighting;
using BlueHarvest.Sim.Testing;
using BlueHarvest.Sim.World;
using Xunit;

namespace BlueHarvest.Sim.Tests;

/// <summary>The Rex's charge: faster than a sprinting person, but only straight ahead over open ground.</summary>
public class ChargeTests
{
    private static Actor RexDoing(ActorAction action, out Simulation sim)
    {
        sim = new Simulation(new WorldMap(30, 40), seed: 1);
        return sim.AddActor("Rex", 'T', new TilePos(15, 15), Species.Rex,
            new ScriptedController(new[] { action }), Direction.South);
    }

    [Fact]
    public void AStraightCharge_IsFasterThanAWalk_AndASprintingPerson()
    {
        var walker = RexDoing(new MoveAction(Direction.South), out var walk);
        walk.RunUntil(1);
        var charger = RexDoing(new MoveAction(Direction.South, Sprint: true), out var charge);
        charge.RunUntil(1);

        Assert.Equal(50, walker.NextActTick);
        Assert.False(walker.Charging);
        Assert.Equal(30, charger.NextActTick);
        Assert.True(charger.Charging);
        Assert.True(MovementProfile.Rex.ChargeTicks < MovementProfile.Human.StepTicks * GearRules.SprintCostPercent / 100);
    }

    [Fact]
    public void Turning_CantBeCharged()
    {
        var rex = RexDoing(new MoveAction(Direction.SouthEast, Sprint: true), out var sim);

        sim.RunUntil(1);

        Assert.Equal(Direction.SouthEast, rex.Facing);
        Assert.Equal(MovementProfile.Rex.TurnTicks, rex.NextActTick);
        Assert.False(rex.Charging);
    }

    [Fact]
    public void SmashingThroughUndergrowth_BreaksTheCharge()
    {
        // Sparse jungle everywhere: wherever the core lands, it smashes something.
        var map = new WorldMap(30, 40);
        for (int y = 0; y < 40; y++)
        {
            for (int x = 0; x < 30; x++)
                map.Cover[new TilePos(x, y)] = CoverType.SparseJungle;
        }
        var sim = new Simulation(map, seed: 1);
        var rex = sim.AddActor("Rex", 'T', new TilePos(15, 15), Species.Rex,
            new ScriptedController(new[] { new MoveAction(Direction.South, Sprint: true) }), Direction.South);

        sim.RunUntil(1);

        Assert.False(rex.Charging);
        Assert.True(rex.NextActTick > 50);
    }

    [Fact]
    public void TheBrain_ChargesPreyItCanSee()
    {
        // Daylight; a person pacing 22 tiles ahead of the Rex is plainly seen.
        var sim = new Simulation(new WorldMap(40, 40), seed: 1);
        var pacing = Enumerable.Range(0, 20)
            .Select(i => (ActorAction)new MoveAction(i % 2 == 0 ? Direction.East : Direction.West)).ToList();
        sim.AddActor("Pete", 'h', new TilePos(20, 8), Species.Human, new ScriptedController(pacing));
        var brain = new CreatureBrain(AiProfile.Rex, sim.Rng.Fork("ai:rex"));
        var rex = sim.AddActor("Rex", 'T', new TilePos(20, 30), Species.Rex, brain, Direction.North);

        sim.RunUntil(1);

        Assert.Equal(ContactClass.Prey, brain.Focus?.Class);
        Assert.True(rex.Charging);
        Assert.Equal(30, rex.NextActTick);
        Assert.StartsWith("CHARGING Pete", brain.Describe(sim));
    }

    [Fact]
    public void TheBrain_DoesntChargeALight()
    {
        var sim = new Simulation(new WorldMap(40, 40), seed: 1) { AmbientLightPercent = 6 };
        sim.AddLight(new LightSource(new TilePos(20, 14), HeightDm: 10, RadiusTiles: 4, IntensityPercent: 90));
        var brain = new CreatureBrain(AiProfile.Rex, sim.Rng.Fork("ai:rex"));
        var rex = sim.AddActor("Rex", 'T', new TilePos(20, 30), Species.Rex, brain, Direction.North);

        sim.RunUntil(1);

        Assert.Equal(ContactSource.Light, brain.Focus?.Key.Source);
        Assert.False(rex.Charging);
        Assert.Equal(50, rex.NextActTick);
    }
}

/// <summary>Scripted survivors wait for the breach, then react after a delay that varies by seed.</summary>
public class BotStartTests
{
    [Fact]
    public void WithNoBreach_TheBotWaits_ThenSetsOffAnyway()
    {
        var sim = new Simulation(new WorldMap(40, 30), seed: 1);
        sim.AddExit(new TilePos(35, 5), EscapePlans.JungleExit);
        sim.AddActor("Rex", 'T', new TilePos(30, 20), Species.Rex, new HoldStillController(), Direction.South);
        var bot = new EscapeBot(EscapePlan.RunToJungle);
        var you = sim.AddActor("You", '@', new TilePos(5, 5), Species.Human, bot);

        sim.RunUntil(EscapeBot.MaxWaitTicks - 100);
        Assert.Equal(new TilePos(5, 5), you.Position);
        Assert.Null(bot.StartedTick);
        Assert.Equal("waiting in the vehicle", bot.Status);

        sim.RunUntil(EscapeBot.MaxWaitTicks + 200);
        Assert.Equal(EscapeBot.MaxWaitTicks, bot.StartedTick);
        Assert.NotEqual(new TilePos(5, 5), you.Position);
    }

    [Fact]
    public void AfterTheBreach_TheBotReacts_AfterItsDelay()
    {
        // The Rex bites through a sagging fence on its first turn (tick 0).
        var map = new WorldMap(20, 24);
        map.Cover[new TilePos(10, 17)] = CoverType.WeakFence;
        var sim = new Simulation(map, seed: 1);
        sim.AddExit(new TilePos(2, 23), EscapePlans.JungleExit);
        sim.AddActor("Rex", 'T', new TilePos(10, 12), Species.Rex,
            new ScriptedController(new[] { new BiteAction(Investigative: true) }), Direction.South);
        var bot = new EscapeBot(EscapePlan.RunToJungle, reactionTicks: 200);
        sim.AddActor("You", '@', new TilePos(2, 20), Species.Human, bot);

        sim.RunUntil(199);
        Assert.Equal(0, sim.FenceBreachTick);
        Assert.Null(bot.StartedTick);
        Assert.Equal("reacting to the breach", bot.Status);

        sim.RunUntil(201);
        Assert.Equal(200, bot.StartedTick);
    }

    [Fact]
    public void EachSeed_VariesTheStart_TheSameWayForEveryPlan()
    {
        var variations = Enumerable.Range(1, 30).Select(i => BatchRunner.VariationFor((ulong)i)).ToList();

        Assert.Equal(BatchRunner.VariationFor(7), BatchRunner.VariationFor(7));
        Assert.All(variations, v =>
        {
            Assert.InRange(v.RexDx, -BatchRunner.RexJitterX, BatchRunner.RexJitterX);
            Assert.InRange(v.RexDy, -BatchRunner.RexJitterY, BatchRunner.RexJitterY);
            Assert.InRange(v.ReactionTicks, 0, BatchRunner.MaxReactionTicks);
            Assert.Contains(v.RexFacing, new[] { Direction.South, Direction.SouthEast, Direction.SouthWest });
        });
        Assert.True(variations.Select(v => v.ReactionTicks).Distinct().Count() > 10);
        Assert.True(variations.Select(v => v.RexFacing).Distinct().Count() == 3);
    }
}

/// <summary>Batch runs on the real testbed: still repeatable with the per-seed variation.</summary>
public class BatchRepeatTests
{
    private static string Text => File.ReadAllText(FindUp(Path.Combine("godot", "Maps", "testbed.bhmap"))
        ?? throw new FileNotFoundException("Could not find godot/Maps/testbed.bhmap above the test folder."));

    [Fact]
    public void TheSameSeed_PlaysTheSameRun()
    {
        string text = Text;
        var first = BatchRunner.RunOnce(text, EscapePlan.RunToJungle, withNpcs: true, seed: 3, maxTicks: Ticks.FromSeconds(40));
        var second = BatchRunner.RunOnce(text, EscapePlan.RunToJungle, withNpcs: true, seed: 3, maxTicks: Ticks.FromSeconds(40));

        Assert.Equal(first, second);
        Assert.NotNull(first.StartedTick);
        Assert.True(first.StartedTick >= first.BreachTick, "the survivor set off before the fence broke");
    }

    private static string? FindUp(string relativePath)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }
}

/// <summary>The verdict's new judgements: ties at 100% and runs that never vary.</summary>
public class VerdictTests
{
    private static RunResult Run(EscapePlan plan, RunOutcome outcome, double seconds, string? cause = null, bool runDown = false) =>
        new(plan, true, 1, outcome, (long)(seconds * Ticks.PerSecond),
            outcome == RunOutcome.Escaped ? EscapePlans.JungleExit : null, cause,
            BreachTick: Ticks.FromSeconds(4), HuntedTick: null, Npcs: 0, NpcsDied: 0, NpcsEscaped: 0,
            CaughtByCharge: runDown);

    [Fact]
    public void TiesAt100Percent_CantBeJudgedForDominance()
    {
        var results = new List<RunResult>
        {
            Run(EscapePlan.RunToJungle, RunOutcome.Escaped, 19),
            Run(EscapePlan.RunToBuilding, RunOutcome.Escaped, 23),
            Run(EscapePlan.StayInVehicle, RunOutcome.Died, 25, "bitten by the Rex: a second major wound to the left leg"),
        };

        var dominance = BatchReport.Verdict(results).Single(c => c.Name == "No option dominates");

        Assert.Null(dominance.Passed);
        Assert.Contains("can't tell", dominance.Detail);
    }

    [Fact]
    public void RunsThatNeverVary_AreFlagged()
    {
        var results = Enumerable.Range(0, 3).Select(_ => Run(EscapePlan.RunToJungle, RunOutcome.Escaped, 19.2))
            .Concat(new[]
            {
                Run(EscapePlan.StayInVehicle, RunOutcome.Died, 25),
                Run(EscapePlan.StayInVehicle, RunOutcome.SafeAtEnd, 300),
                Run(EscapePlan.StayInVehicle, RunOutcome.Died, 40),
            })
            .ToList();

        var check = BatchReport.Verdict(results).Single(c => c.Name == "The seeds make a difference");
        var jungle = BatchReport.Rows(results).Single(r => r.Plan == EscapePlan.RunToJungle);

        Assert.False(check.Passed);
        Assert.Contains("Run to the jungle (with NPCs)", check.Detail);
        Assert.DoesNotContain("Stay in the vehicle", check.Detail);
        Assert.Contains("the seeds changed nothing", BatchReport.Story(jungle));
    }

    [Fact]
    public void DeathCauses_AreGroupedByWhatDidIt()
    {
        var results = new List<RunResult>
        {
            Run(EscapePlan.StayInVehicle, RunOutcome.Died, 25, "bitten by the Rex: a second major wound to the left leg", runDown: true),
            Run(EscapePlan.StayInVehicle, RunOutcome.Died, 26, "bitten by the Rex: a second major wound to the torso"),
            Run(EscapePlan.StayInVehicle, RunOutcome.Escaped, 30),
        };

        var row = BatchReport.Rows(results).Single();
        string story = BatchReport.Story(row);

        Assert.Equal(("bitten by the Rex", 2), Assert.Single(row.DeathCauses));
        Assert.Equal(1, row.RunDown);
        Assert.Contains("bitten by the Rex ×2 (the fatal wound: left leg 1, torso 1)", story);
        Assert.Contains("1 of them were run down by a charge.", story);
        Assert.Equal("bleeding out", BatchReport.Killer("bleeding out"));
    }
}
