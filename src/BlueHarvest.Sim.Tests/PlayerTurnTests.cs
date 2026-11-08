using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Demo;
using BlueHarvest.Sim.World;
using Xunit;

namespace BlueHarvest.Sim.Tests;

public class PlayerTurnTests
{
    [Fact]
    public void Step_WaitsForInput_WithoutChangingAnything()
    {
        var player = new PlayerController();
        var sim = new Simulation(HeightMap.FromRows("000"), seed: 1);
        var you = sim.AddActor("You", '@', new TilePos(0, 0), Species.Human, player);

        Assert.Equal(StepResult.AwaitingInput, sim.Step());
        Assert.Equal(StepResult.AwaitingInput, sim.Step());
        Assert.Equal(0, sim.ActionsResolved);
        Assert.Equal(new TilePos(0, 0), you.Position);
    }

    [Fact]
    public void PendingAction_IsTaken_ThenOthersAct_UntilPlayerIsDueAgain()
    {
        var player = new PlayerController();
        var sim = new Simulation(HeightMap.FromRows("00000", "00000"), seed: 1);
        var you = sim.AddActor("You", '@', new TilePos(0, 0), Species.Human, player);
        var other = sim.AddActor("Other", 'o', new TilePos(4, 1), Species.Human,
            new ScriptedController(Array.Empty<ActorAction>())); // always waits 100 ticks

        Assert.Equal(StepResult.AwaitingInput, sim.RunUntilInputNeeded());

        player.Pending = new MoveAction(Direction.East);
        Assert.Equal(StepResult.AwaitingInput, sim.RunUntilInputNeeded());

        Assert.Equal(new TilePos(1, 0), you.Position);
        Assert.Equal(70, sim.CurrentTick);     // paused on the player's next turn
        Assert.Equal(2, sim.ActionsResolved);  // the player moved, the other actor waited
        Assert.Equal(100, other.NextActTick);
        Assert.Null(player.Pending);
    }

    [Fact]
    public void BlockedClimb_RaisesEventWithReason()
    {
        var player = new PlayerController();
        var sim = new Simulation(HeightMap.FromRows("05"), seed: 1); // 2.5 m: past the 2 m people can climb
        var you = sim.AddActor("You", '@', new TilePos(0, 0), Species.Human, player);
        string? reason = null;
        sim.MoveBlocked += (_, r) => reason = r;

        player.Pending = new MoveAction(Direction.East);
        sim.RunUntilInputNeeded();

        Assert.Equal("too steep to climb (2.5 m)", reason);
        Assert.Equal(new TilePos(0, 0), you.Position);
    }

    [Fact]
    public void DropPastSafeLimit_IsBlocked()
    {
        var sim = new Simulation(HeightMap.FromRows("50"), seed: 1);
        var you = sim.AddActor("You", '@', new TilePos(0, 0), Species.Human, new PlayerController());
        Assert.Equal("too far to drop (2.5 m)", sim.MoveBlockReason(you, Direction.East));
    }

    [Fact]
    public void PlayerHistory_ReplaysIdentically()
    {
        var player = new PlayerController();
        var live = DemoWorld.Create(5, player);
        live.RunUntilInputNeeded();

        var inputs = new ActorAction[]
        {
            new MoveAction(Direction.North), new MoveAction(Direction.North), new WaitAction(70),
            new MoveAction(Direction.East), new MoveAction(Direction.NorthEast), new MoveAction(Direction.West),
            new MoveAction(Direction.South), new WaitAction(35), new MoveAction(Direction.SouthWest),
        };
        foreach (var input in inputs)
        {
            player.Pending = input;
            live.RunUntilInputNeeded();
        }

        var replay = DemoWorld.Create(5, new ScriptedController(player.History));
        for (long i = 0; i < live.ActionsResolved; i++)
            replay.Step();

        Assert.Equal(Snapshot(live), Snapshot(replay));
    }

    private static List<(TilePos Position, long NextActTick)> Snapshot(Simulation sim) =>
        sim.Actors.Select(a => (a.Position, a.NextActTick)).ToList();
}
