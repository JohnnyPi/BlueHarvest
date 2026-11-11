#nullable enable
using BlueHarvest.Sim.World.Generation;

namespace BlueHarvest.Game.Island;

/// <summary>
/// What the play scene should start on. The island explorer fills this in before switching scenes
/// ("walk here"); when it is empty the play scene loads the testbed map as before. It also lets the
/// explorer come back to the island it had generated.
/// </summary>
public static class IslandLaunch
{
    /// <summary>The generated island, kept between scenes.</summary>
    public static IslandWorld? World { get; private set; }
    public static IslandChunker? Chunker { get; private set; }

    /// <summary>Whether the play scene should start on the island, at <see cref="X"/>, <see cref="Y"/> (metres).</summary>
    public static bool Walk { get; private set; }
    public static double X { get; private set; }
    public static double Y { get; private set; }

    public static void Remember(IslandWorld world)
    {
        if (ReferenceEquals(World, world)) return;
        World = world;
        Chunker = null;
    }

    public static void WalkAt(IslandWorld world, double x, double y)
    {
        Remember(world);
        Chunker ??= new IslandChunker(world);
        X = x; Y = y;
        Walk = true;
    }

    /// <summary>Back to the testbed: the island is kept for the explorer, but the play scene won't start on it.</summary>
    public static void PlayTestbed() => Walk = false;
}
