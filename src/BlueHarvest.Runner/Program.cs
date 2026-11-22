using BlueHarvest.Sim;
using BlueHarvest.Sim.Ai;
using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.Demo;
using BlueHarvest.Sim.Scenarios;
using BlueHarvest.Sim.Testing;
using BlueHarvest.Sim.World;
using BhContent = BlueHarvest.Content;
using Gen = BlueHarvest.Sim.World.Generation;

// Usage: BlueHarvest.Runner [seed] [seconds] [map file] [weather]
//    or: BlueHarvest.Runner batch [runs per option] [minutes] [weather] [options] [map file]
//    or: BlueHarvest.Runner island validate [first seed] [count] [scale]
//    or: BlueHarvest.Runner content validate [--path <packs folder or pack folder>] [--runtime]
// Island mode generates islands from count seeds starting at first seed (defaults 1, 200, 100% scale),
// measures each one, walks the routes between its key sites, runs the seed checks (IslandChecks), and
// writes reports/island-<time>/report.md and seeds.csv under the repo.
// Batch mode plays the testbed with a scripted player per escape option (with and without NPCs) and
// writes reports/batch-<time>/report.md and runs.csv under the repo. Use "-" for any default:
// runs 30, minutes 5, the map's own weather, all options (or a comma list: vehicle,jungle,building,
// rappel,flare,tower), the testbed map.
// With no map file, it looks for godot/Maps/testbed.bhmap above the program folder,
// and falls back to the built-in demo map if it can't find one. Pass "-" as the map file to use
// that default while still choosing the weather.
// Weather (optional): clear, rain, storm or fog, overriding the map's own weather line.
// Content mode loads YAML content packs (default: content/packs above the program folder) with the content
// library, prints every diagnostic, and exits 1 if there are errors. --runtime also requires every definition
// to be runtime-ready (all its handlers implemented), as the game will at startup.
if (args.Length > 0 && args[0] == "batch")
{
    RunBatch(args.Skip(1).ToArray());
    return;
}
if (args.Length > 0 && args[0] == "island")
{
    RunIsland(args.Skip(1).ToArray());
    return;
}
if (args.Length > 0 && args[0] == "content")
{
    Environment.ExitCode = RunContent(args.Skip(1).ToArray());
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

static void RunIsland(string[] args)
{
    string Arg(int i) => i < args.Length ? args[i] : "-";
    if (Arg(0) != "validate")
        throw new ArgumentException($"Unknown island command '{Arg(0)}'; use: island validate [first seed] [count] [scale].");
    var invariant = System.Globalization.CultureInfo.InvariantCulture;
    uint first = uint.TryParse(Arg(1), out uint f) ? f : 1;
    int count = int.TryParse(Arg(2), out int c) && c > 0 ? c : 200;
    double scale = double.TryParse(Arg(3), System.Globalization.NumberStyles.Float, invariant, out double s) && s > 0 ? s : 100;

    Console.WriteLine($"Island validation: {count} seeds from {first} at {scale.ToString("0.#", invariant)}% scale, "
        + $"{Environment.ProcessorCount} at a time");
    var clock = System.Diagnostics.Stopwatch.StartNew();
    var results = new Gen.IslandSeedResult[count];
    int done = 0;
    var gate = new object();
    Parallel.For(0, count, i =>
    {
        uint islandSeed = unchecked(first + (uint)i);
        var one = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var world = Gen.IslandWorld.Generate(new Gen.IslandParams { Seed = islandSeed, Scale = scale });
            var measures = world.Measures();
            var validation = world.Validate();
            results[i] = new Gen.IslandSeedResult(islandSeed, measures.Stats, one.Elapsed.TotalSeconds,
                Reach: measures.Reach, Validation: validation);
        }
        catch (Exception e)
        {
            results[i] = new Gen.IslandSeedResult(islandSeed, null, one.Elapsed.TotalSeconds, $"{e.GetType().Name}: {e.Message}");
        }
        int now = Interlocked.Increment(ref done);
        lock (gate)
        {
            if (now % 10 == 0 || now == count)
                Console.WriteLine($"  {now,4}/{count}  ({clock.Elapsed.TotalSeconds:0}s)");
        }
    });
    clock.Stop();

    var settings = new Gen.IslandReportSettings(first, count, scale, clock.Elapsed.TotalSeconds);
    string root = Path.GetDirectoryName(FindUp("BlueHarvest.sln") ?? Path.Combine(Directory.GetCurrentDirectory(), "x")) ?? ".";
    string folder = Path.Combine(root, "reports", $"island-{DateTime.Now:yyyyMMdd-HHmmss}");
    Directory.CreateDirectory(folder);
    string reportPath = Path.Combine(folder, "report.md");
    string csvPath = Path.Combine(folder, "seeds.csv");
    File.WriteAllText(reportPath, Gen.IslandReport.Markdown(results, settings));
    File.WriteAllText(csvPath, Gen.IslandReport.Csv(results));

    Console.WriteLine();
    Console.Write(Gen.IslandReport.ConsoleSummary(results));
    Console.WriteLine();
    Console.WriteLine($"Done in {clock.Elapsed.TotalSeconds:0}s.");
    Console.WriteLine($"Report: {reportPath}");
    Console.WriteLine($"Seeds:  {csvPath}");
}

static int RunContent(string[] args)
{
    if (args.Length == 0 || args[0] != "validate")
    {
        Console.Error.WriteLine("Usage: BlueHarvest.Runner content validate [--path <packs folder or pack folder>] [--runtime]");
        return 2;
    }
    string? path = null;
    bool runtime = false;
    for (int i = 1; i < args.Length; i++)
    {
        if (args[i] == "--path" && i + 1 < args.Length)
            path = args[++i];
        else if (args[i] == "--runtime")
            runtime = true;
        else
        {
            Console.Error.WriteLine($"Unknown option {args[i]}. Usage: content validate [--path <folder>] [--runtime]");
            return 2;
        }
    }
    path ??= FindUpDirectory(Path.Combine("content", "packs"));
    if (path is null || !Directory.Exists(path))
    {
        Console.Error.WriteLine(path is null
            ? "Could not find content/packs above the program folder; pass --path."
            : $"No folder at {path}.");
        return 2;
    }

    // The sim's handlers (the item mechanics it has, and the P5 contracts) and the game version, as the game loads it.
    var options = BlueHarvest.Sim.ContentIntegration.SimContent.Options(runtime ? BhContent.ContentMode.Runtime : BhContent.ContentMode.Authoring);
    var source = new BhContent.FileSystemContentSource(path);
    Console.WriteLine($"Blue Harvest content validate | {source.Description} | {(runtime ? "runtime" : "authoring")} mode | game {SimInfo.Version}");
    Console.WriteLine();

    BhContent.ContentLoadResult result = BhContent.ContentLoader.LoadFrom(source, options);
    foreach (BhContent.ContentDiagnostic d in result.Diagnostics)
        Console.WriteLine(d);
    if (result.Diagnostics.Count > 0)
        Console.WriteLine();

    int errors = result.Diagnostics.Count(d => d.Severity == BhContent.ContentSeverity.Error);
    int warnings = result.Diagnostics.Count(d => d.Severity == BhContent.ContentSeverity.Warning);
    int notes = result.Diagnostics.Count(d => d.Severity == BhContent.ContentSeverity.Note);
    if (result.Registry is { } registry)
    {
        Console.WriteLine($"Packs, in load order: {string.Join(", ", registry.Packs.Select(p => $"{p.Id} {p.Version}"))}");
        var kinds = BhContent.ContentKinds.All
            .Select(k => (kind: k, count: registry.Definitions.Count(x => x.Kind == k)))
            .Where(t => t.count > 0)
            .Select(t => $"{t.count} {BhContent.ContentKinds.Label(t.kind)}");
        Console.WriteLine($"Definitions: {registry.Count} ({string.Join(", ", kinds)})");
        Console.WriteLine($"Not runtime-ready: {registry.NotRuntimeReady.Count}");
        Console.WriteLine($"Fingerprint: {registry.Fingerprint}");
    }
    Console.WriteLine($"Result: {(result.Succeeded ? "OK" : "FAILED")}, {errors} error(s), {warnings} warning(s), {notes} note(s)");
    return result.Succeeded ? 0 : 1;
}

static string? FindUpDirectory(string relativePath)
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        string candidate = Path.Combine(dir.FullName, relativePath);
        if (Directory.Exists(candidate))
            return candidate;
    }
    return null;
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
