using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.Testing;
using BlueHarvest.Sim.World;
using Xunit;

namespace BlueHarvest.Sim.Tests;

/// <summary>The flare bot throws only when the Rex isn't looking, then keeps still before it runs.</summary>
public class FlareBotTests
{
    /// <summary>
    /// The Rex bites through a sagging fence on its first turn (tick 0) and then stands facing south;
    /// the bot, with the testbed survivor's three flares, reacts at once.
    /// </summary>
    private static (Simulation Sim, Actor Rex, Actor You, EscapeBot Bot) Scene(TilePos youAt)
    {
        var map = new WorldMap(20, 24);
        map.Cover[new TilePos(10, 17)] = CoverType.WeakFence;
        var sim = new Simulation(map, seed: 1);
        sim.AddExit(new TilePos(2, 2), EscapePlans.JungleExit);
        var rex = sim.AddActor("Rex", 'T', new TilePos(10, 12), Species.Rex,
            new ScriptedController(new[] { new BiteAction(Investigative: true) }), Direction.South);
        var bot = new EscapeBot(EscapePlan.FlareThenJungle);
        var you = sim.AddActor("You", '@', youAt, Species.Human, bot, loadout: Loadout.TestbedSurvivor);
        return (sim, rex, you, bot);
    }

    [Fact]
    public void InTheRexsView_ItKeepsStill_ThenThrowsAnywayAfterAWhile()
    {
        var (sim, rex, you, bot) = Scene(new TilePos(10, 21));
        Assert.True(EscapeBot.SeenBy(sim, rex, you), "the test needs the Rex to see the bot");

        sim.RunUntil(EscapeBot.FlareHoldTicks - 100);
        Assert.Equal(0, sim.FenceBreachTick);
        Assert.Equal(3, you.Inventory.Count(ItemKind.Flare));
        Assert.Equal(new TilePos(10, 21), you.Position);
        Assert.Equal("keeping still until the Rex looks away", bot.Status);

        sim.RunUntil(EscapeBot.FlareHoldTicks + 200);
        Assert.Equal(2, you.Inventory.Count(ItemKind.Flare));
    }

    [Fact]
    public void BehindTheRex_ItThrowsAtOnce_KeepsStill_ThenRuns()
    {
        var (sim, rex, you, bot) = Scene(new TilePos(10, 3));
        Assert.False(EscapeBot.SeenBy(sim, rex, you), "the test needs the bot out of the Rex's view");

        sim.RunUntil(GearRules.ThrowTicks + 50);
        Assert.Equal(2, you.Inventory.Count(ItemKind.Flare));
        Assert.Equal(new TilePos(10, 3), you.Position);
        Assert.Equal("keeping still while the Rex turns to the flare", bot.Status);

        sim.RunUntil(GearRules.ThrowTicks + EscapeBot.FlareSettleTicks + 300);
        Assert.NotEqual(new TilePos(10, 3), you.Position);
        Assert.Equal($"heading for the {EscapePlans.JungleExit}", bot.Status);
        Assert.Equal(2, you.Inventory.Count(ItemKind.Flare));
    }
}

/// <summary>The report's "Reacting late" table: deaths split by how long the survivor waited.</summary>
public class ReactionReportTests
{
    private static RunResult Run(EscapePlan plan, RunOutcome outcome, int reactionSeconds) =>
        new(plan, true, 1, outcome, Ticks.FromSeconds(30),
            outcome == RunOutcome.Escaped ? EscapePlans.JungleExit : null,
            outcome == RunOutcome.Died ? "bitten by the Rex: a second major wound to the left leg" : null,
            BreachTick: Ticks.FromSeconds(4), HuntedTick: null, Npcs: 0, NpcsDied: 0, NpcsEscaped: 0,
            StartedTick: Ticks.FromSeconds(4 + reactionSeconds), ReactionTicks: (int)Ticks.FromSeconds(reactionSeconds));

    private static readonly List<RunResult> Results = new()
    {
        Run(EscapePlan.RunToJungle, RunOutcome.Escaped, 1),
        Run(EscapePlan.RunToJungle, RunOutcome.Escaped, 2),
        Run(EscapePlan.RunToJungle, RunOutcome.Died, 4),
        Run(EscapePlan.RunToJungle, RunOutcome.Died, 8),
        Run(EscapePlan.RunToJungle, RunOutcome.Escaped, 9),
        Run(EscapePlan.StayInVehicle, RunOutcome.Died, 1),
    };

    [Fact]
    public void RunsAreSplitIntoBands_LeavingOutTheOptionThatStays()
    {
        var row = Assert.Single(BatchReport.ReactionRows(Results));

        Assert.Equal(EscapePlan.RunToJungle, row.Plan);
        Assert.Equal(new[] { (0, 2), (1, 1), (1, 2) }, row.Bands);
        Assert.Equal("under 3 s", BatchReport.ReactionBandLabel(0));
        Assert.Equal("3–6 s", BatchReport.ReactionBandLabel(1));
        Assert.Equal("6 s or more", BatchReport.ReactionBandLabel(2));
    }

    [Fact]
    public void TheReport_ShowsTheTable_AndTheCsvTheDelay()
    {
        var settings = new BatchSettings("testbed.bhmap", "dry, calm", 5, Ticks.FromSeconds(300), 1);

        string report = BatchReport.Markdown(Results, settings);
        string csv = BatchReport.Csv(Results);

        Assert.Contains("## Reacting late", report);
        Assert.Contains("| Option | set off under 3 s after | set off 3–6 s after | set off 6 s or more after |", report);
        Assert.Contains("| Run to the jungle | 0% died (0 of 2) | 100% died (1 of 1) | 50% died (1 of 2) |", report);
        Assert.DoesNotContain("| Stay in the vehicle | 100% died", report);
        Assert.EndsWith(",reaction_seconds", csv.Split('\n')[0]);
        Assert.Contains(",no,8.0\n", csv);
    }
}
