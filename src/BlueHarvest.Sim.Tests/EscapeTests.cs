using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Ai;
using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.Scenarios;
using BlueHarvest.Sim.Testing;
using BlueHarvest.Sim.World;
using Xunit;

namespace BlueHarvest.Sim.Tests;

/// <summary>Exits: stepping onto one ends the run as an escape, for people only.</summary>
public class ExitTests
{
    [Fact]
    public void ReachingAnExit_EndsTheRunAsAnEscape()
    {
        var sim = new Simulation(new WorldMap(10, 3), seed: 1);
        var exit = sim.AddExit(new TilePos(5, 1), "floor hatch");
        var player = new PlayerController();
        var you = sim.AddActor("You", '@', new TilePos(3, 1), Species.Human, player);
        var escaped = new List<(Actor, ExitPoint)>();
        sim.ActorEscaped += (a, e) => escaped.Add((a, e));
        Assert.Equal(StepResult.AwaitingInput, sim.RunUntilInputNeeded());

        player.Pending = new MoveAction(Direction.East);
        Assert.Equal(StepResult.AwaitingInput, sim.RunUntilInputNeeded());
        player.Pending = new MoveAction(Direction.East);

        Assert.Equal(StepResult.PlayerEscaped, sim.RunUntilInputNeeded());
        Assert.True(you.HasEscaped);
        Assert.True(you.IsGone);
        Assert.False(you.IsDead);
        Assert.Equal(exit, you.EscapedVia);
        Assert.Null(sim.ActorAt(new TilePos(5, 1)));
        Assert.Equal((you, exit), Assert.Single(escaped));
    }

    [Fact]
    public void Escapees_AreGone_FromTurnsAndFromSight()
    {
        var sim = new Simulation(new WorldMap(10, 3), seed: 1);
        sim.AddExit(new TilePos(5, 1), "culvert");
        var pete = sim.AddActor("Pete", 'h', new TilePos(4, 1), Species.Human,
            new ScriptedController(new ActorAction[] { new MoveAction(Direction.East) }));
        var watcher = sim.AddActor("Watcher", 'h', new TilePos(8, 1), Species.Human, new HoldStillController());

        sim.RunUntil(1_000);

        Assert.True(pete.HasEscaped);
        Assert.Equal(1_000, sim.CurrentTick); // the schedule carries on without him
        Assert.DoesNotContain(PerceptionSystem.Perceive(sim, watcher, 0).Seen, s => s.Target == pete);
    }

    [Fact]
    public void TheRex_CantUseAnExit()
    {
        var sim = new Simulation(new WorldMap(20, 24), seed: 1);
        sim.AddExit(new TilePos(10, 11), "floor hatch");
        var rex = sim.AddActor("Rex", 'T', new TilePos(10, 12), Species.Rex,
            new ScriptedController(new ActorAction[] { new MoveAction(Direction.North) }), Direction.North);

        sim.RunUntil(200);

        Assert.Equal(new TilePos(10, 11), rex.Position);
        Assert.False(rex.HasEscaped);
    }

    [Fact]
    public void MapFiles_KeepExitsAndSparks()
    {
        const string text =
            "bhmap 2\nsize 3 1\nheights\n0 0 0\ncover\n.w.\nmarkers\n" +
            "player 0 0\nexit 2 0 floor hatch\nsparks 1 0\nend\n";

        var data = MapFile.Parse(text);
        string written = MapFile.Serialize(data);
        var sim = ScenarioBuilder.Build(data, seed: 1);

        Assert.Contains("exit 2 0 floor hatch\n", written);
        Assert.Contains("sparks 1 0\n", written);
        Assert.Equal(CoverType.WeakFence, data.Map.Cover[new TilePos(1, 0)]);
        Assert.Equal("floor hatch", Assert.Single(sim.Exits).Name);
        Assert.Equal(new TilePos(1, 0), Assert.Single(sim.Sparks).Position);
    }
}

/// <summary>The sagging fence: sparks draw the Rex, and its bite snaps the section.</summary>
public class SaggingFenceTests
{
    [Fact]
    public void ABite_SnapsTheSaggingFence_AndKillsItsSparks_ButNotTheElectricFence()
    {
        // The Rex at (10,12) facing south has its head tip at (10,16); the fence row is y = 17.
        var map = new WorldMap(20, 24);
        map.Cover[new TilePos(9, 17)] = CoverType.WeakFence;
        map.Cover[new TilePos(10, 17)] = CoverType.WeakFence;
        map.Cover[new TilePos(11, 17)] = CoverType.Fence;
        var sim = new Simulation(map, seed: 1) { AmbientLightPercent = 6 };
        var sparks = sim.AddSparks(new TilePos(10, 17));
        var died = new List<SparkingFence>();
        sim.SparksDied += died.Add;
        var rex = sim.AddActor("Rex", 'T', new TilePos(10, 12), Species.Rex,
            new ScriptedController(new ActorAction[] { new BiteAction(Investigative: true) }), Direction.South);
        var zone = sim.BiteZone(rex);
        Assert.Contains(new TilePos(9, 17), zone);
        Assert.Contains(new TilePos(10, 17), zone);
        Assert.Contains(new TilePos(11, 17), zone);
        Assert.True(sim.Lights[sparks.LightIndex].On);

        sim.RunUntil(1);

        Assert.Contains(sim.RecentNoises, n => n.Kind == NoiseKinds.Sparks && n.ActorId is null);
        Assert.Contains(sim.RecentNoises, n => n.Kind == NoiseKinds.SnappingWire);
        Assert.Equal(CoverType.BrokenFence, sim.Cover[new TilePos(9, 17)]);
        Assert.Equal(CoverType.BrokenFence, sim.Cover[new TilePos(10, 17)]);
        Assert.Equal(CoverType.Fence, sim.Cover[new TilePos(11, 17)]);
        Assert.False(sparks.Live);
        Assert.False(sim.Lights[sparks.LightIndex].On);
        Assert.Equal(sparks, Assert.Single(died));
    }

    [Fact]
    public void Sparks_KeepCracklingWhileTheFenceStands()
    {
        var map = new WorldMap(10, 10);
        map.Cover[new TilePos(5, 5)] = CoverType.WeakFence;
        var sim = new Simulation(map, seed: 1);
        sim.AddSparks(new TilePos(5, 5));
        sim.AddActor("Pete", 'h', new TilePos(1, 1), Species.Human, new HoldStillController());

        sim.RunUntil(280); // turns at 0, 70, ... cross into the 0-149 and 150-299 windows

        Assert.Equal(2, sim.RecentNoises.Count(n => n.Kind == NoiseKinds.Sparks));
    }

    [Fact]
    public void Sparks_NeedASaggingFence()
    {
        var sim = new Simulation(new WorldMap(4, 4), seed: 1);
        Assert.Throws<ArgumentException>(() => sim.AddSparks(new TilePos(1, 1)));
    }
}

/// <summary>The real testbed, played headless: the opening, the exits, and each scripted escape route.</summary>
public class TestbedEscapeTests
{
    private static readonly string TestbedPath = FindUp(Path.Combine("godot", "Maps", "testbed.bhmap"))
        ?? throw new FileNotFoundException("Could not find godot/Maps/testbed.bhmap above the test folder.");

    private static string Text => File.ReadAllText(TestbedPath);

    /// <summary>The testbed without its Rex: a pure navigation check for the scripted players.</summary>
    private static string TextWithoutRex =>
        string.Join('\n', Text.Split('\n').Where(line => !line.StartsWith("rex ", StringComparison.Ordinal)));

    [Fact]
    public void TheTestbed_HasItsThreeExitsAndSparks()
    {
        var sim = ScenarioBuilder.Build(MapFile.Parse(Text), seed: 1);
        Assert.Equal(new[] { EscapePlans.JungleExit, EscapePlans.BuildingExit, EscapePlans.CulvertExit }.OrderBy(n => n),
            sim.Exits.Select(e => e.Name).OrderBy(n => n));
        Assert.NotEmpty(sim.Sparks);
        Assert.True(sim.Weather.Settings.Storm);
    }

    [Theory]
    [InlineData(EscapePlans.JungleExit)]
    [InlineData(EscapePlans.BuildingExit)]
    [InlineData(EscapePlans.CulvertExit)]
    public void EveryExit_IsOutOfTheRexsReach(string exitName)
    {
        var sim = ScenarioBuilder.Build(MapFile.Parse(Text), seed: 1);
        var exit = sim.Exits.Single(e => e.Name == exitName);
        var rex = sim.Actors.Single(a => a.Species == Species.Rex);
        var someone = sim.AddActor("Someone", 'h', exit.Position, Species.Human, new HoldStillController());

        var status = EscapeCheck.Evaluate(sim, rex, (CreatureBrain)rex.Controller, someone);

        Assert.True(status.OutOfReach, $"The Rex can reach the {exitName} at {exit.Position}.");
    }

    [Fact]
    public void TheRex_BreaksThroughTheSaggingFence_RightAway()
    {
        var run = BatchRunner.RunOnce(Text, EscapePlan.StayInVehicle, withNpcs: true, seed: 1, maxTicks: Ticks.FromSeconds(30));

        Assert.NotNull(run.BreachTick);
        Assert.InRange(run.BreachTick!.Value, 0, Ticks.FromSeconds(20));
    }

    [Theory]
    [InlineData(EscapePlan.RunToJungle, EscapePlans.JungleExit)]
    [InlineData(EscapePlan.RunToBuilding, EscapePlans.BuildingExit)]
    [InlineData(EscapePlan.RappelToCulvert, EscapePlans.CulvertExit)]
    [InlineData(EscapePlan.FlareThenJungle, EscapePlans.JungleExit)]
    public void WithNoRex_EachRunningPlan_ReachesItsExit(EscapePlan plan, string exitName)
    {
        var run = BatchRunner.RunOnce(TextWithoutRex, plan, withNpcs: false, seed: 1, maxTicks: Ticks.FromSeconds(180));

        Assert.Equal(RunOutcome.Escaped, run.Outcome);
        Assert.Equal(exitName, run.Exit);
    }

    [Fact]
    public void WithNoRex_TheTowerPlan_EndsUpOnTheDeck()
    {
        var bot = new EscapeBot(EscapePlan.ClimbWatchtower);
        var sim = ScenarioBuilder.Build(MapFile.Parse(TextWithoutRex), seed: 1, bot);
        var player = sim.Actors.Single(a => a.Controller == bot);

        sim.RunUntil(Ticks.FromSeconds(120));

        Assert.True(player.OnDeck, $"The tower bot is at {player.Position}, {bot.Status}.");
    }

    [Fact]
    public void TheRappelPoint_IsTheLedgeAboveTheCulvert()
    {
        var sim = ScenarioBuilder.Build(MapFile.Parse(Text), seed: 1);
        var culvert = sim.Exits.Single(e => e.Name == EscapePlans.CulvertExit);

        var point = EscapeBot.RappelPoint(sim, culvert.Position);

        Assert.Equal((new TilePos(69, 19), Direction.North), point);
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

/// <summary>The batch report's arithmetic and wording, on made-up results.</summary>
public class BatchReportTests
{
    private static RunResult Run(EscapePlan plan, bool npcs, RunOutcome outcome, int seconds, string? cause = null) =>
        new(plan, npcs, 1, outcome, Ticks.FromSeconds(seconds),
            outcome == RunOutcome.Escaped ? EscapePlans.JungleExit : null, cause,
            BreachTick: Ticks.FromSeconds(4), HuntedTick: null, Npcs: 0, NpcsDied: 0, NpcsEscaped: 0);

    [Fact]
    public void Bars_AndClocks_ReadAtAGlance()
    {
        Assert.Equal("███████░░░", BatchReport.Bar(7, 10));
        Assert.Equal("░░░░░░░░░░", BatchReport.Bar(0, 10));
        Assert.Equal("██████████", BatchReport.Bar(3, 3));
        Assert.Equal("1:05", BatchReport.Clock(Ticks.FromSeconds(65)));
    }

    [Fact]
    public void TheVerdict_FlagsOptionsThatNeverWork_NeverFail_OrDominate()
    {
        var results = new List<RunResult>
        {
            Run(EscapePlan.RunToJungle, true, RunOutcome.Escaped, 60),
            Run(EscapePlan.RunToJungle, true, RunOutcome.Escaped, 70),
            Run(EscapePlan.StayInVehicle, true, RunOutcome.Died, 30, "bitten by the Rex"),
            Run(EscapePlan.StayInVehicle, true, RunOutcome.HuntedAtEnd, 300),
        };

        var verdict = BatchReport.Verdict(results).ToDictionary(c => c.Name);

        Assert.False(verdict["Every option can work"].Passed);
        Assert.Contains("Stay in the vehicle (with NPCs)", verdict["Every option can work"].Detail);
        Assert.False(verdict["Every option can fail"].Passed);
        Assert.Contains("Run to the jungle (with NPCs)", verdict["Every option can fail"].Detail);
        Assert.False(verdict["No option dominates"].Passed);
        Assert.True(verdict["The Rex breaks out at the start"].Passed);
    }

    [Fact]
    public void TheReport_LeadsWithTheVerdict_AndTheCsvHasARowPerRun()
    {
        var results = new List<RunResult>
        {
            Run(EscapePlan.RunToJungle, true, RunOutcome.Escaped, 60),
            Run(EscapePlan.RunToJungle, true, RunOutcome.Died, 40, "bitten by the Rex"),
        };
        var settings = new BatchSettings("testbed.bhmap", "dry, calm", 2, Ticks.FromSeconds(300), 1);

        string report = BatchReport.Markdown(results, settings);
        string csv = BatchReport.Csv(results);

        Assert.StartsWith("# Testbed batch report", report);
        Assert.True(report.IndexOf("## Verdict", StringComparison.Ordinal) < report.IndexOf("## Got away", StringComparison.Ordinal));
        Assert.Contains("| Run to the jungle | with | `█████░░░░░` 50% |", report);
        Assert.Contains("1 of 2 got away (all through the jungle trail, median 1:00).", report);
        Assert.Equal(3, csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("jungle,with,1,escaped,yes,60.0,jungle trail,,4.0,,0,0,0", csv);
    }
}
