using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.World;
using BlueHarvest.Sim.World.Generation;

namespace BlueHarvest.Sim;

/// <summary>
/// Starts a game on the generated island in a session (P3b): the streamed window, the simulation on it, and everything
/// the run lays over the island (weather, power, tides and rivers, the loot, the rooms) before the survivor lands. The
/// game, the runner and the tests all start an island the same way, and a snapshot is put back over the same steps
/// (<see cref="Snapshots.ExpeditionSnapshot.Restore"/>).
/// </summary>
public static class IslandExpedition
{
    /// <summary>The survivor's name and glyph.</summary>
    public const string SurvivorName = "Survivor";

    public const char SurvivorGlyph = '@';

    /// <summary>
    /// Starts an expedition: the window centred on the landing, the morning's hour, the run laid over the island, and
    /// the survivor at the nearest standable tile with the testbed kit and their own island map.
    /// </summary>
    public static Simulation Start(WorldSession session, IslandWorld world, IslandChunker chunker, IslandStart start,
        IActorController player, FeatureIndex? features = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(start);
        var window = new IslandWindow(world, chunker, start.X, start.Y, start.ChunksAcross);
        Simulation sim = Begin(session, window, start);
        sim.SetTimeOfDay(start.StartHour);
        LayOver(sim, world, window, start.RunSeed, ItemPlan.Build(world, start.RunSeed, sim.Catalog));
        TilePos landing = window.NearestStandable(window.ToLocal(start.X, start.Y));
        Actor survivor = sim.AddActor(SurvivorName, SurvivorGlyph, landing, Species.Human, player, loadout: Loadout.TestbedSurvivor);
        // the survivor's own island map, filled in as they explore
        sim.UseMapKnowledge(survivor, world, features);
        return sim;
    }

    /// <summary>The simulation on a window, in the session, which now plays on that island.</summary>
    internal static Simulation Begin(WorldSession session, IslandWindow window, IslandStart start)
    {
        var sim = new Simulation(session, window.Map, start.RunSeed);
        session.UseIsland(window, start);
        return sim;
    }

    /// <summary>What every run lays over the island, in the order the game always has: the items given.</summary>
    internal static void LayOver(Simulation sim, IslandWorld world, IslandWindow window, ulong runSeed, ItemLedger items)
    {
        // showers drift in on the trade wind and differ from place to place; each run has its own
        sim.FollowWeather(IslandWeather.For(world, runSeed), window.OriginX, window.OriginY);
        // the facilities' power as this run finds it: some still running, most dark, each with ways back
        sim.UsePower(PowerPlan.Build(world, runSeed), window.OriginX, window.OriginY);
        // the tide comes and goes twice a day, and the rivers rise after rain over their catchments
        sim.UseWater(IslandWaters.For(world, runSeed));
        // what was left in the rooms, ten years on (or, from a snapshot, where it all is now)
        sim.UseItems(items);
        // every named room, so the sim can say which room a tile is in and who walked into which
        sim.UseRooms(RoomIndex.Build(world));
        // the island's mean air temperature at sea level: the sea's (it cools 6.5 °C a kilometre up)
        sim.SetBaseTemperature(world.Params.SeaTemp);
    }
}
