using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Ai;
using BlueHarvest.Sim.Navigation;
using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.World;
using Xunit;

namespace BlueHarvest.Sim.Tests;

/// <summary>Platforms: decks above the ground, stairs, ladders, support posts and collapse.</summary>
public class PlatformTests
{
    private static void Do(Simulation sim, PlayerController drive, ActorAction action)
    {
        drive.Pending = action;
        sim.RunUntilInputNeeded();
    }

    /// <summary>
    /// A staircase along row 1: decks at x 3-8 rising 0.5 m a tile to 3 m. A human starts on the
    /// ground at (2,1). With <paramref name="walled"/>, rows 0 and 2 are walls, so the stairs are the only way.
    /// </summary>
    private static (Simulation Sim, PlayerController Drive, Actor Human) Stairs(bool walled = false)
    {
        var map = new WorldMap(12, 3);
        for (int x = 3; x <= 8; x++)
            map.Structures.SetDeck(new TilePos(x, 1), (x - 2) * 5);
        if (walled)
        {
            for (int x = 0; x < 12; x++)
            {
                map.Cover[new TilePos(x, 0)] = CoverType.Wall;
                map.Cover[new TilePos(x, 2)] = CoverType.Wall;
            }
        }
        var sim = new Simulation(map, seed: 1);
        var drive = new PlayerController();
        var human = sim.AddActor("You", '@', new TilePos(2, 1), Species.Human, drive);
        sim.RunUntilInputNeeded();
        return (sim, drive, human);
    }

    [Fact]
    public void Stairs_LeadUpOntoAPlatform_WhoseEdgeIsADrop()
    {
        var (sim, drive, human) = Stairs();

        for (int i = 0; i < 6; i++)
            Do(sim, drive, new MoveAction(Direction.East));

        Assert.Equal(new TilePos(8, 1), human.Position);
        Assert.True(human.OnDeck);
        Assert.Equal(30, sim.StandHeight(human));
        Assert.Same(human, sim.ActorOnDeck(new TilePos(8, 1)));
        Assert.Null(sim.ActorAt(new TilePos(8, 1)));
        Assert.Equal("too far to drop (3.0 m)", sim.MoveBlockReason(human, Direction.North));
        Assert.Contains(sim.RecentNoises, n => n.Kind == NoiseKinds.Footsteps
            && n.Loudness == Species.Human.FootstepLoudness + Covers.Deck.NoiseModifier);
    }

    [Fact]
    public void Pathfinder_TakesTheStairs()
    {
        var (sim, drive, human) = Stairs(walled: true);

        var path = Pathfinder.FindPath(sim, human, new TilePos(8, 1));

        Assert.NotNull(path);
        foreach (var move in path)
            Do(sim, drive, new MoveAction(move));
        Assert.Equal(new TilePos(8, 1), human.Position);
        Assert.True(human.OnDeck);
    }

    [Fact]
    public void Ladder_ClimbsUpAndDown()
    {
        var map = new WorldMap(10, 10);
        map.Structures.SetDeck(new TilePos(5, 5), 60);
        map.Structures.SetLadder(new TilePos(5, 5), true);
        var sim = new Simulation(map, seed: 1);
        var drive = new PlayerController();
        var human = sim.AddActor("You", '@', new TilePos(5, 5), Species.Human, drive);
        sim.RunUntilInputNeeded();

        Do(sim, drive, new ClimbAction());
        Assert.True(human.OnDeck);
        Assert.Equal(60, sim.StandHeight(human));
        Assert.Equal(60 * Species.Human.Movement.ClimbTicksPerDm, human.NextActTick);

        Do(sim, drive, new ClimbAction());
        Assert.False(human.OnDeck);
        Assert.Equal(0, sim.StandHeight(human));
    }

    [Theory]
    [InlineData(30, false)] // a 3 m catwalk: too low for a 4.5 m Rex
    [InlineData(50, true)]  // a 5 m catwalk: it walks underneath
    public void Rex_FitsUnderAPlatform_OnlyIfItsHighEnough(int deckDm, bool fits)
    {
        var map = new WorldMap(20, 20);
        for (int x = 0; x < 20; x++)
            map.Structures.SetDeck(new TilePos(x, 10), deckDm);
        var sim = new Simulation(map, seed: 1);
        var rex = sim.AddActor("Rex", 'T', new TilePos(10, 12), Species.Rex, new PlayerController(), Direction.North);

        Assert.Equal(fits ? null : "a platform is in the way", sim.MoveBlockReason(rex, Direction.North));
    }

    [Theory]
    [InlineData(40, true)]  // 4 m up: in reach
    [InlineData(60, false)] // 6 m up: safe
    public void Rex_BitesPeopleOnLowPlatforms_NotHighOnes(int deckDm, bool bitten)
    {
        var map = new WorldMap(30, 30);
        for (int y = 13; y <= 14; y++)
            for (int x = 9; x <= 11; x++)
                map.Structures.SetDeck(new TilePos(x, y), deckDm);
        var sim = new Simulation(map, seed: 1);
        var drive = new PlayerController();
        var rex = sim.AddActor("Rex", 'T', new TilePos(10, 10), Species.Rex, drive, Direction.South);
        var human = sim.AddActor("Pete", 'h', new TilePos(10, 13), Species.Human, new HoldStillController(), onDeck: true);
        int bites = 0;
        sim.ActorBitten += (_, victim) => bites += victim == human ? 1 : 0;
        sim.RunUntilInputNeeded();

        Do(sim, drive, new BiteAction());

        Assert.Equal(bitten ? 1 : 0, bites);
    }

    /// <summary>
    /// A 6 m catwalk along row 10, x 8-12, on two posts at (8,10) and (9,10), with Pete up on it at
    /// (11,10). A Rex driven by the test stands at (8,12) facing north.
    /// </summary>
    private static (Simulation Sim, PlayerController Drive, Actor Rex, Actor Pete, Platform Platform) Catwalk()
    {
        var map = new WorldMap(20, 20);
        for (int x = 8; x <= 12; x++)
            map.Structures.SetDeck(new TilePos(x, 10), 60);
        map.Cover[new TilePos(8, 10)] = CoverType.Support;
        map.Cover[new TilePos(9, 10)] = CoverType.Support;
        var sim = new Simulation(map, seed: 1);
        var drive = new PlayerController();
        var rex = sim.AddActor("Rex", 'T', new TilePos(8, 12), Species.Rex, drive, Direction.North);
        var pete = sim.AddActor("Pete", 'h', new TilePos(11, 10), Species.Human, new HoldStillController(), onDeck: true);
        sim.RunUntilInputNeeded();
        return (sim, drive, rex, pete, Assert.Single(sim.Platforms));
    }

    [Fact]
    public void SmashingItsSupports_BringsAPlatformDown_AndRidersFall()
    {
        var (sim, drive, rex, pete, platform) = Catwalk();
        Assert.Equal(2, platform.InitialSupports);
        var fallen = new List<(Actor Actor, int Drop)>();
        sim.ActorFell += (actor, drop) => fallen.Add((actor, drop));

        Do(sim, drive, new MoveAction(Direction.North)); // the core tramples both posts

        Assert.True(platform.Collapsed);
        Assert.Empty(sim.SupportsOf(platform));
        Assert.False(sim.Map.Structures.HasDeck(new TilePos(11, 10)));
        Assert.Equal(CoverType.Rubble, sim.Cover[new TilePos(11, 10)]);
        Assert.False(pete.OnDeck);
        Assert.Same(pete, sim.ActorAt(pete.Position));
        Assert.Equal(new[] { (pete, 60) }, fallen);
        Assert.Contains(sim.RecentNoises, n => n.Kind == NoiseKinds.Collapse);
        Assert.Contains(sim.RecentNoises, n => n.Kind == NoiseKinds.Scream && n.ActorId == pete.Id);
        Assert.Equal(new TilePos(8, 11), rex.Position);
    }

    [Fact]
    public void Rex_KnocksDownAPlatform_ToGetAtPreyOutOfReach()
    {
        var map = new WorldMap(40, 40);
        for (int x = 15; x <= 25; x++)
            map.Structures.SetDeck(new TilePos(x, 10), 60);
        map.Cover[new TilePos(16, 10)] = CoverType.Support;
        map.Cover[new TilePos(17, 10)] = CoverType.Support;
        var sim = new Simulation(map, seed: 7);
        var brain = new CreatureBrain(AiProfile.Rex, sim.Rng.Fork("ai:rex"));
        var rex = sim.AddActor("Rex", 'T', new TilePos(20, 24), Species.Rex, brain, Direction.North);
        var pacing = Enumerable.Range(0, 80)
            .Select(i => (ActorAction)new MoveAction(i % 2 == 0 ? Direction.East : Direction.West))
            .ToList();
        var pete = sim.AddActor("Pete", 'h', new TilePos(20, 10), Species.Human, new ScriptedController(pacing), onDeck: true);
        var platform = Assert.Single(sim.Platforms);
        var activities = new HashSet<Activity>();

        for (int second = 1; second <= 60 && !platform.Collapsed; second++)
        {
            sim.RunUntil(Core.Ticks.FromSeconds(second));
            activities.Add(brain.Activity);
        }

        Assert.Contains(Activity.Smashing, activities);
        Assert.True(platform.Collapsed, $"Still standing; the Rex is at {rex.Position} and is {brain.Describe(sim)}.");
        Assert.False(pete.OnDeck);
    }

    [Fact]
    public void Escape_OnAHighPlatform_IsOutOfReach()
    {
        var (sim, _, rex, pete, _) = Catwalk();
        var brain = new CreatureBrain(AiProfile.Rex, sim.Rng.Fork("ai:rex"));

        Assert.True(EscapeCheck.Evaluate(sim, rex, brain, pete).OutOfReach);
    }
}
