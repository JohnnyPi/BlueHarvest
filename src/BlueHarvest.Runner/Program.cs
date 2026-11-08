using BlueHarvest.Sim;
using BlueHarvest.Sim.Ai;
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

// Run a second at a time, logging each creature's activity whenever it changes.
var brains = sim.Actors.Where(a => a.Controller is CreatureBrain).ToList();
var player = sim.Actors.FirstOrDefault(a => a.Glyph == '@');
var director = new Director();
var lastDescription = new Dictionary<int, string>();
int logged = 0;
const int MaxLogLines = 60;
if (brains.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("Creature activity:");
}
for (int second = 1; second <= seconds; second++)
{
    sim.RunUntil(Ticks.FromSeconds(second));
    if (player is not null)
        director.Update(sim, player);
    foreach (var creature in brains)
    {
        string description = ((CreatureBrain)creature.Controller).Describe(sim);
        if (lastDescription.TryGetValue(creature.Id, out var previous) && previous == description)
            continue;
        lastDescription[creature.Id] = description;
        if (logged++ < MaxLogLines)
            Console.WriteLine($"  {Ticks.Format(sim.CurrentTick),8}  {creature.Name}: {description}");
    }
}
if (logged > MaxLogLines)
    Console.WriteLine($"  ... and {logged - MaxLogLines} more changes");

Console.WriteLine();
Console.WriteLine($"After {Ticks.Format(sim.CurrentTick)} and {sim.ActionsResolved} actions:");
PrintMap(sim);

Console.WriteLine();
foreach (var actor in sim.Actors)
    Console.WriteLine($"  {actor.Glyph} {actor.Name,-11} at {actor.Position}, next turn at {Ticks.Format(actor.NextActTick)}");

Console.WriteLine();
if (player is not null)
    Console.WriteLine($"Director: tension now {director.Tension}, peak {director.PeakSoFar}");
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
