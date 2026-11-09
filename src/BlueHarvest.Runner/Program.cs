using BlueHarvest.Sim;
using BlueHarvest.Sim.Ai;
using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.Demo;
using BlueHarvest.Sim.Scenarios;
using BlueHarvest.Sim.Testing;
using BlueHarvest.Sim.World;

// Usage: BlueHarvest.Runner [seed] [seconds] [map file] [weather]
//    or: BlueHarvest.Runner batch [runs per option] [minutes] [weather] [options] [map file]
// Batch mode plays the testbed with a scripted player per escape option (with and without NPCs) and
// writes reports/batch-<time>/report.md and runs.csv under the repo. Use "-" for any default:
// runs 30, minutes 5, the map's own weather, all options (or a comma list: vehicle,jungle,building,
// rappel,flare,tower), the testbed map.
// With no map file, it looks for godot/Maps/testbed.bhmap above the program folder,
// and falls back to the built-in demo map if it can't find one. Pass "-" as the map file to use
// that default while still choosing the weather.
// Weather (optional): clear, rain, storm or fog, overriding the map's own weather line.
if (args.Length > 0 && args[0] == "batch")
{
    RunBatch(args.Skip(1).ToArray());
    return;
}

ulong seed = args.Length > 0 && ulong.TryParse(args[0], out var parsedSeed) ? parsedSeed : 12345UL;
int seconds = args.Length > 1 && int.TryParse(args[1], out var parsedSeconds) ? parsedSeconds : 60;
string? mapPath = args.Length > 2 && args[2] != "-" ? args[2] : FindUp(Path.Combine("godot", "Maps", "testbed.bhmap"));
WeatherSettings? weatherOverride = args.Length > 3 ? ParseWeather(args[3]) : null;

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

if (weatherOverride is not null)
    sim.SetWeather(weatherOverride);
int strikes = 0;
sim.LightningStruck += _ => strikes++;
Console.WriteLine($"Weather: {sim.Weather.Settings.Describe()}");

Console.WriteLine();
Console.WriteLine("Start (map-file cover characters; @ you, h NPC, T rex, R raptor):");
PrintMap(sim);

// Run a second at a time, logging what each creature and person is doing whenever it changes.
var brains = sim.Actors.Where(a => a.Controller is CreatureBrain or HumanBrain).ToList();
var player = sim.Actors.FirstOrDefault(a => a.Glyph == '@');
var director = new Director();
var lastDescription = new Dictionary<int, string>();
int logged = 0;
const int MaxLogLines = 60;
if (brains.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("Activity:");
}
for (int second = 1; second <= seconds; second++)
{
    sim.RunUntil(Ticks.FromSeconds(second));
    if (player is not null)
        director.Update(sim, player);
    foreach (var creature in brains)
    {
        string description = creature.IsDead ? "dead" : creature.Controller switch
        {
            CreatureBrain rex => rex.Describe(sim),
            HumanBrain person => person.Mood.ToString().ToLowerInvariant() + (person.RefusingOrder ? ", ignoring orders" : ""),
            _ => "",
        };
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
{
    Console.WriteLine($"Director: tension now {director.Tension}, peak {director.PeakSoFar}");
    Console.WriteLine(player.IsDead
        ? $"{player.Name} died at {Ticks.Format(player.Health.DiedTick)}: {player.Health.DeathCause}."
        : $"{player.Name} is alive; wounds: {Wounds(player)}.");
}
Console.WriteLine($"Weather: {sim.Weather.Settings.Describe()}; {strikes} lightning strike(s); "
    + $"{sim.ScentMarks.Count} scent marks on the ground.");
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

static string Wounds(BlueHarvest.Sim.Actors.Actor actor)
{
    var hurt = Enum.GetValues<BlueHarvest.Sim.Injuries.BodySection>()
        .Where(s => actor.Health[s].Severity != BlueHarvest.Sim.Injuries.Severity.Green)
        .Select(s => $"{BlueHarvest.Sim.Injuries.InjuryRules.Name(s)} {actor.Health[s].Severity.ToString().ToLowerInvariant()}"
            + (actor.Health[s].Bleeding ? " (bleeding)" : ""))
        .ToList();
    return hurt.Count == 0 ? "none" : string.Join(", ", hurt);
}

static WeatherSettings? ParseWeather(string name) => name.ToLowerInvariant() switch
{
    "-" or "map" => null,
    "clear" => WeatherSettings.Clear,
    "rain" => new WeatherSettings(RainPercent: 70, WindFrom: Direction.East, WindSpeed: 5),
    "storm" => WeatherSettings.TestbedStorm,
    "fog" => new WeatherSettings(FogPercent: 70),
    _ => throw new ArgumentException($"Unknown weather '{name}'; use clear, rain, storm, fog or map."),
};

static void RunBatch(string[] args)
{
    string Arg(int i) => i < args.Length ? args[i] : "-";
    int runs = int.TryParse(Arg(0), out int r) && r > 0 ? r : 30;
    int minutes = int.TryParse(Arg(1), out int m) && m > 0 ? m : 5;
    var weather = ParseWeather(Arg(2));
    var plans = new List<EscapePlan>();
    if (Arg(3) == "-")
    {
        plans.AddRange(EscapePlans.All);
    }
    else
    {
        foreach (string key in Arg(3).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = EscapePlans.All.Where(p => p.Key() == key).ToList();
            if (match.Count == 0)
                throw new ArgumentException($"Unknown option '{key}'; use vehicle, jungle, building, rappel, flare or tower.");
            plans.Add(match[0]);
        }
    }
    string mapPath = Arg(4) != "-" ? Arg(4)
        : FindUp(Path.Combine("godot", "Maps", "testbed.bhmap")) ?? throw new FileNotFoundException("Could not find godot/Maps/testbed.bhmap.");
    string mapText = File.ReadAllText(mapPath);
    var mapWeather = weather ?? MapFile.Parse(mapText).StartingWeather;
    long limit = Ticks.FromSeconds(minutes * 60);

    int total = plans.Count * 2 * runs;
    Console.WriteLine($"Batch: {plans.Count} option(s) x with/without NPCs x {runs} seeds = {total} runs of up to {minutes} min");
    Console.WriteLine($"Map {Path.GetFileName(mapPath)}, weather: {mapWeather.Describe()}");
    var clock = System.Diagnostics.Stopwatch.StartNew();
    int lastShown = -1;
    var gate = new object();
    var results = BatchRunner.RunAll(mapText, plans, runs, limit, weather, firstSeed: 1, progress: (done, all) =>
    {
        int percent = done * 100 / all;
        lock (gate)
        {
            if (percent / 10 == lastShown / 10 && done != all)
                return;
            lastShown = percent;
            Console.WriteLine($"  {percent,3}%  ({done}/{all} runs, {clock.Elapsed.TotalSeconds:0}s)");
        }
    });

    var settings = new BatchSettings(Path.GetFileName(mapPath), mapWeather.Describe(), runs, limit, FirstSeed: 1);
    string root = Path.GetDirectoryName(FindUp("BlueHarvest.sln") ?? mapPath) ?? ".";
    string folder = Path.Combine(root, "reports", $"batch-{DateTime.Now:yyyyMMdd-HHmmss}");
    Directory.CreateDirectory(folder);
    string reportPath = Path.Combine(folder, "report.md");
    string csvPath = Path.Combine(folder, "runs.csv");
    File.WriteAllText(reportPath, BatchReport.Markdown(results, settings));
    File.WriteAllText(csvPath, BatchReport.Csv(results));

    Console.WriteLine();
    Console.Write(BatchReport.ConsoleSummary(results));
    Console.WriteLine();
    Console.WriteLine($"Done in {clock.Elapsed.TotalSeconds:0}s.");
    Console.WriteLine($"Report: {reportPath}");
    Console.WriteLine($"Runs:   {csvPath}");
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
