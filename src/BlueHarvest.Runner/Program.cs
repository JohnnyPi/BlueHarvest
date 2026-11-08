using BlueHarvest.Sim;
using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.Demo;
using BlueHarvest.Sim.Scenarios;
using BlueHarvest.Sim.World;

// Usage: BlueHarvest.Runner [seed] [seconds] [map file]
// With no map file, it looks for godot/Maps/testbed.bhmap above the program folder,
// and falls back to the built-in demo map if it can't find one.
ulong seed = args.Length > 0 && ulong.TryParse(args[0], out var parsedSeed) ? parsedSeed : 12345UL;
int seconds = args.Length > 1 && int.TryParse(args[1], out var parsedSeconds) ? parsedSeconds : 60;
string? mapPath = args.Length > 2 ? args[2] : FindUp(Path.Combine("godot", "Maps", "testbed.bhmap"));

Simulation sim;
if (mapPath is not null)
{
    sim = ScenarioBuilder.Build(MapFile.Parse(File.ReadAllText(mapPath)), seed);
    Console.WriteLine($"Blue Harvest sim {SimInfo.Version} | {Path.GetFileName(mapPath)} | seed {seed} | {seconds}s of game time");
}
else
{
    sim = DemoWorld.Create(seed);
    Console.WriteLine($"Blue Harvest sim {SimInfo.Version} | demo map (no testbed found) | seed {seed} | {seconds}s of game time");
}

Console.WriteLine();
Console.WriteLine("Start (map-file cover characters; @ you, h NPC, T rex, R raptor):");
PrintMap(sim);

sim.RunUntil(Ticks.FromSeconds(seconds));

Console.WriteLine();
Console.WriteLine($"After {Ticks.Format(sim.CurrentTick)} and {sim.ActionsResolved} actions:");
PrintMap(sim);

Console.WriteLine();
foreach (var actor in sim.Actors)
    Console.WriteLine($"  {actor.Glyph} {actor.Name,-11} at {actor.Position}, next turn at {Ticks.Format(actor.NextActTick)}");

Console.WriteLine();
Console.WriteLine($"State hash: {sim.ComputeStateHash():X16}");

static void PrintMap(Simulation sim)
{
    for (int y = 0; y < sim.Map.Height; y++)
    {
        var line = new char[sim.Map.Width];
        for (int x = 0; x < sim.Map.Width; x++)
        {
            var p = new TilePos(x, y);
            line[x] = sim.ActorAt(p)?.Glyph ?? Covers.Get(sim.Cover[p]).FileChar;
        }
        Console.WriteLine("  " + new string(line));
    }
}

static string? FindUp(string relativePath)
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        string candidate = Path.Combine(dir.FullName, relativePath);
        if (File.Exists(candidate))
            return candidate;
    }
    return null;
}
