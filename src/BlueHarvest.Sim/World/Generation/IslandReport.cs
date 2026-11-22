using System.Globalization;
using System.Text;

namespace BlueHarvest.Sim.World.Generation;

/// <summary>One seed of an island validation run: its measures, key-site routes and checks, or why the island could not be generated.</summary>
public sealed record IslandSeedResult(uint Seed, IslandStats? Stats, double Seconds, string? Error = null, IslandReach? Reach = null,
    IslandValidation? Validation = null);

/// <summary>How a validation run was set up, for the report's header.</summary>
public sealed record IslandReportSettings(uint FirstSeed, int Count, double Scale, double WallSeconds);

/// <summary>
/// Turns island measures over many seeds into a Markdown report (each measure's spread across the seeds,
/// then shortfalls worth a look), a CSV with one row per seed, and a short console summary.
/// Step 24a: measures; step 24b: walking routes between key sites; step 24c: the checks (<see cref="IslandChecks"/>).
/// </summary>
public static class IslandReport
{
    /// <summary>The decided minimum beach share of the coast (design: "at least about 15%").</summary>
    public const double DecidedBeachShare = IslandChecks.BeachMinShare;

    /// <summary>How many seeds a list in the report names before it says "and N more".</summary>
    public const int ListedSeeds = 20;

    /// <summary>One measure: a CSV key, a readable name, a display format and how to read it from the stats.</summary>
    public sealed record Column(string Key, string Name, string Format, Func<IslandStats, double> Get);

    public static IReadOnlyList<Column> Columns { get; } =
    [
        new Column("land_pct", "Land, % of the frame", "0.0", s => 100 * s.LandFraction),
        new Column("land_km2", "Land, km2", "0", s => s.LandKm2),
        new Column("main_km2", "Main island, km2", "0", s => s.MainKm2),
        new Column("islets", "Islets (0.1 km2 and up)", "0", s => s.Islets),
        new Column("largest_islet_km2", "Largest islet, km2", "0.00", s => s.LargestIsletKm2),
        new Column("rivers", "Rivers", "0", s => s.Rivers),
        new Column("rivers_to_sea_pct", "Rivers reaching the sea, %", "0.0", s => 100 * s.RiversToSeaShare),
        new Column("largest_river_q", "Largest river, m3/s", "0.0", s => s.LargestRiverQ),
        new Column("deep_river_cells", "River cells too deep to wade", "0", s => s.DeepRiverCells),
        new Column("lakes", "Lakes", "0", s => s.Lakes),
        new Column("lake_km2", "Lakes, km2", "0.00", s => s.LakeKm2),
        new Column("pit_cells", "Pit cells (all)", "0", s => s.PitCells),
        new Column("highest_m", "Highest point, m", "0", s => s.HighestM),
        new Column("lowland_pct", "Lowland (below 150 m), % of dry land", "0.0", s => 100 * s.LowlandShare),
        new Column("steep_pct", "Steeper than 30 deg, % of dry land", "0.0", s => 100 * s.SteepShare),
        new Column("walkable_pct", "Walkable, % of dry land", "0.0", s => 100 * s.WalkableShare),
        new Column("walkable_connected_pct", "Walkable and connected, % of dry land", "0.0", s => 100 * s.WalkableConnectedShare),
        new Column("beach_pct", "Beach, % of the main island's coast", "0.0", s => 100 * s.BeachShare),
        new Column("beach_all_pct", "Beach, % of all coasts", "0.0", s => 100 * s.BeachShareAll),
        new Column("facilities_short", "Facilities not placed", "0", s => s.FacilitiesShort),
        new Column("paddocks_placed", "Paddocks placed", "0", s => s.PaddocksPlaced),
        new Column("road_km", "Roads, km", "0", s => s.RoadKm),
        new Column("bridges", "Bridges", "0", s => s.Bridges),
        new Column("tunnels", "Road tunnels", "0", s => s.Tunnels),
        new Column("coastal_pit_cells", "Coast cells below sea level (count as reaching the sea)", "0", s => s.CoastalPitCells),
        new Column("inland_pit_cells", "Enclosed basins that aren't lakes (inland pits)", "0", s => s.InlandPitCells),
    ];

    /// <summary>The spread of one measure over the seeds that generated.</summary>
    public sealed record Spread(Column Column, double Min, double Median, double Max, uint MinSeed, uint MaxSeed);

    public static IReadOnlyList<Spread> Spreads(IReadOnlyList<IslandSeedResult> results)
    {
        var ok = Generated(results);
        var spreads = new List<Spread>();
        if (ok.Count == 0) return spreads;
        foreach (Column col in Columns)
        {
            var values = ok.Select(r => (Seed: r.Seed, Value: col.Get(r.Stats!))).ToList();
            var min = values[0];
            var max = values[0];
            foreach (var v in values)
            {
                if (v.Value < min.Value) min = v;
                if (v.Value > max.Value) max = v;
            }
            spreads.Add(new Spread(col, min.Value, Median(values.Select(v => v.Value)), max.Value, min.Seed, max.Seed));
        }
        return spreads;
    }

    /// <summary>The middle value; the mean of the two middle values when there is an even number. 0 for none.</summary>
    public static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count == 0) return 0;
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    // ---------- Markdown ----------

    public static string Markdown(IReadOnlyList<IslandSeedResult> results, IslandReportSettings settings)
    {
        var ok = Generated(results);
        var failed = results.Where(r => r.Stats is null).ToList();
        var sb = new StringBuilder();
        uint last = unchecked(settings.FirstSeed + (uint)Math.Max(0, settings.Count - 1));
        sb.AppendLine("# Island validation report");
        sb.AppendLine();
        sb.AppendLine($"Seeds {settings.FirstSeed}–{last} at {F(settings.Scale, "0")}% scale · sim {SimInfo.Version} · "
            + $"{ok.Count} of {results.Count} islands generated in {F(settings.WallSeconds, "0")} s"
            + (ok.Count > 0 ? $" ({F(ok.Average(r => r.Seconds), "0.0")} s per island on average, {F(ok.Max(r => r.Seconds), "0.0")} s the slowest)." : "."));
        sb.AppendLine();
        sb.AppendLine("Every seed's numbers are in `seeds.csv` beside this report.");
        sb.AppendLine();

        if (failed.Count > 0)
        {
            sb.AppendLine("## Seeds that could not be generated");
            sb.AppendLine();
            sb.AppendLine("| Seed | Error |");
            sb.AppendLine("|---:|---|");
            foreach (IslandSeedResult r in failed) sb.AppendLine($"| {r.Seed} | {Cell(r.Error ?? "unknown error")} |");
            sb.AppendLine();
        }
        if (ok.Count == 0) return sb.ToString();

        ChecksSection(sb, ok);

        sb.AppendLine("## Measures across the seeds");
        sb.AppendLine();
        sb.AppendLine("| Measure | Min | Median | Max | Seed at min | Seed at max |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|");
        foreach (Spread sp in Spreads(results))
        {
            string fmt = sp.Column.Format;
            sb.AppendLine($"| {sp.Column.Name} | {F(sp.Min, fmt)} | {F(sp.Median, fmt)} | {F(sp.Max, fmt)} | {sp.MinSeed} | {sp.MaxSeed} |");
        }
        sb.AppendLine();

        RoutesSection(sb, ok);

        sb.AppendLine("## Facility placement");
        sb.AppendLine();
        sb.AppendLine("| Facility | Wanted | Seeds short | Fewest placed | Short in seeds |");
        sb.AppendLine("|---|---:|---:|---:|---|");
        for (int k = 0; k < IslandWorks.Facilities.Length; k++)
        {
            FacilityType f = IslandWorks.Facilities[k];
            var shortSeeds = ok.Where(r => r.Stats!.Facilities[k].Short > 0).Select(r => r.Seed).ToList();
            int fewest = ok.Min(r => r.Stats!.Facilities[k].Placed);
            string wanted = f.Key == "outstation" ? "2–8" : f.Count.ToString(CultureInfo.InvariantCulture); // outstations: by seed (P4.5)
            sb.AppendLine($"| {f.Name}{(f.Major ? " (major)" : "")} | {wanted} | {shortSeeds.Count} | {fewest} | {SeedList(shortSeeds)} |");
        }
        sb.AppendLine();

        sb.AppendLine("## Worth a look");
        sb.AppendLine();
        Flag(sb, ok, "A paddock missing", s => s.PaddocksShort > 0);
        Flag(sb, ok, $"Beach under the decided {F(100 * DecidedBeachShare, "0")}% of the main island's coast", s => s.BeachShare < DecidedBeachShare);
        Flag(sb, ok, "A river that doesn't reach the sea (coastal pits count as reaching it)", s => s.RiversToSea < s.Rivers);
        Flag(sb, ok, "Enclosed basins that aren't lakes (inland pits)", s => s.InlandPitCells > 0);
        Flag(sb, ok, "Coast cells below sea level (rivers into them count as reaching the sea)", s => s.CoastalPitCells > 0);
        Flag(sb, ok, "No islets (the design asks for 3–8)", s => s.Islets == 0);
        Flag(sb, ok, "More than 8 islets", s => s.Islets > 8);
        Flag(sb, ok, "Road tunnels (impassable on foot until step 55)", s => s.Tunnels > 0);
        Flag(sb, ok, "Rivers too deep to wade at base flow (32 m³/s and up)", s => s.DeepRiverCells > 0);
        sb.AppendLine();

        sb.AppendLine("## Slopes over all seeds");
        sb.AppendLine();
        sb.AppendLine("| Slope | Share of dry land | |");
        sb.AppendLine("|---|---:|---|");
        long dry = ok.Sum(r => (long)r.Stats!.DryCells);
        for (int b = 0; b <= IslandStats.SlopeBinEdgesDeg.Count; b++)
        {
            long cells = ok.Sum(r => (long)r.Stats!.SlopeCells[b]);
            double share = dry == 0 ? 0 : cells / (double)dry;
            sb.AppendLine($"| {SlopeBinName(b)} | {F(100 * share, "0.0")}% | {Bar(share)} |");
        }
        return sb.ToString();
    }

    private static void ChecksSection(StringBuilder sb, List<IslandSeedResult> ok)
    {
        var checkedSeeds = ok.Where(r => r.Validation is not null).ToList();
        if (checkedSeeds.Count == 0) return;
        int passed = checkedSeeds.Count(r => r.Validation!.Passed);
        int warned = checkedSeeds.Count(r => r.Validation!.Warnings.Count > 0);
        sb.AppendLine("## Checks");
        sb.AppendLine();
        sb.AppendLine($"**{passed} of {checkedSeeds.Count} seeds pass.** {warned} have warnings (reported only; warnings never reject a seed).");
        sb.AppendLine();
        sb.AppendLine("| Check | Severity | Seeds not passing | Seeds |");
        sb.AppendLine("|---|---|---:|---|");
        foreach (IslandCheck check in IslandChecks.All)
        {
            var seeds = checkedSeeds.Where(r => Results(r.Validation!).Any(x => x.Key == check.Key)).Select(r => r.Seed).ToList();
            sb.AppendLine($"| {check.Name} (`{check.Key}`) | {check.Severity} | {seeds.Count} | {SeedList(seeds)} |");
        }
        sb.AppendLine();

        var failing = checkedSeeds.Where(r => !r.Validation!.Passed).ToList();
        if (failing.Count > 0)
        {
            sb.AppendLine("### Failing seeds");
            sb.AppendLine();
            sb.AppendLine("| Seed | Why |");
            sb.AppendLine("|---:|---|");
            foreach (IslandSeedResult r in failing.Take(ListedSeeds)) sb.AppendLine($"| {r.Seed} | {Cell(r.Validation!.FailureSummary)} |");
            if (failing.Count > ListedSeeds) sb.AppendLine($"| | and {failing.Count - ListedSeeds} more seeds (see `seeds.csv`) |");
            sb.AppendLine();
        }
        var warnings = checkedSeeds.Where(r => r.Validation!.Warnings.Count > 0).ToList();
        if (warnings.Count > 0)
        {
            sb.AppendLine("### Warnings");
            sb.AppendLine();
            sb.AppendLine("| Seed | Warning |");
            sb.AppendLine("|---:|---|");
            foreach (IslandSeedResult r in warnings.Take(ListedSeeds)) sb.AppendLine($"| {r.Seed} | {Cell(r.Validation!.WarningSummary)} |");
            if (warnings.Count > ListedSeeds) sb.AppendLine($"| | and {warnings.Count - ListedSeeds} more seeds (see `seeds.csv`) |");
            sb.AppendLine();
        }
    }

    private static IEnumerable<CheckResult> Results(IslandValidation v) => v.Failures.Concat(v.Warnings);

    private static void RoutesSection(StringBuilder sb, List<IslandSeedResult> ok)
    {
        var withReach = ok.Where(r => r.Reach is not null).ToList();
        if (withReach.Count == 0) return;
        sb.AppendLine("## Walking routes between key sites");
        sb.AppendLine();
        sb.AppendLine("Key sites: docks, airstrip, comms relay, control centre and watch posts. Every pair is walked on foot "
            + "with road tunnels shut (impassable until step 55) and rivers too deep to wade at base flow (32 m³/s and up) shut.");
        sb.AppendLine();
        int connected = withReach.Count(r => r.Reach!.FullyConnected);
        sb.AppendLine($"- **Every pair walkable:** {connected} of {withReach.Count} seeds.");
        ReachFlag(sb, withReach, "A pair that needs a road tunnel", x => x.TunnelGaps > 0);
        ReachFlag(sb, withReach, "A pair that crosses deep water", x => x.DeepGaps > 0);
        ReachFlag(sb, withReach, "A site with no walkable ground, or no route at all", x => x.GroundGaps > 0);
        var ford = withReach.Select(r => (double)r.Reach!.MustFord).ToList();
        int pairs = withReach.Max(r => r.Reach!.Routes.Count);
        sb.AppendLine($"- **Pairs that must ford a river** (every river floods past wading in heavy rain): median {F(Median(ford), "0.#")} "
            + $"of {pairs} pairs, {F(ford.Min(), "0")}–{F(ford.Max(), "0")} across the seeds.");
        var detour = withReach.Select(r => (r.Seed, Detour: r.Reach!.WorstDryDetour)).OrderBy(x => x.Detour).ToList();
        sb.AppendLine($"- **Worst detour when rivers flood** (the dry route against today's walk, per seed): median ×{F(Median(detour.Select(x => x.Detour)), "0.00")}, "
            + $"worst ×{F(detour[^1].Detour, "0.00")} (seed {detour[^1].Seed}).");
        var slow = withReach.Select(r => r.Reach!.SlowestMinutes).ToList();
        sb.AppendLine($"- **Slowest walk between two key sites:** {F(slow.Min(), "0")}–{F(slow.Max(), "0")} minutes, median {F(Median(slow), "0")}.");
        sb.AppendLine();

        var gapped = withReach.Where(r => !r.Reach!.FullyConnected).ToList();
        if (gapped.Count > 0)
        {
            sb.AppendLine("| Seed | Pairs that can't be walked today |");
            sb.AppendLine("|---:|---|");
            foreach (IslandSeedResult r in gapped.Take(ListedSeeds))
            {
                var gaps = r.Reach!.Routes.Where(x => x.Status != RouteStatus.Ok)
                    .Select(x => $"{x.From} – {x.To} ({IslandReach.Describe(x.Status)})");
                sb.AppendLine($"| {r.Seed} | {Cell(string.Join("; ", gaps))} |");
            }
            if (gapped.Count > ListedSeeds) sb.AppendLine($"| | and {gapped.Count - ListedSeeds} more seeds (see `seeds.csv`) |");
            sb.AppendLine();
        }
    }

    private static void ReachFlag(StringBuilder sb, List<IslandSeedResult> withReach, string what, Func<IslandReach, bool> test)
    {
        var seeds = withReach.Where(r => test(r.Reach!)).Select(r => r.Seed).ToList();
        sb.AppendLine($"- **{what}:** {seeds.Count} of {withReach.Count} seeds{(seeds.Count > 0 ? " (" + SeedList(seeds) + ")" : "")}.");
    }

    private static void Flag(StringBuilder sb, List<IslandSeedResult> ok, string what, Func<IslandStats, bool> test)
    {
        var seeds = ok.Where(r => test(r.Stats!)).Select(r => r.Seed).ToList();
        sb.AppendLine($"- **{what}:** {seeds.Count} of {ok.Count} seeds{(seeds.Count > 0 ? " (" + SeedList(seeds) + ")" : "")}.");
    }

    public static string SlopeBinName(int bin)
    {
        IReadOnlyList<double> e = IslandStats.SlopeBinEdgesDeg;
        if (bin == 0) return $"under {F(e[0], "0")}°";
        if (bin >= e.Count) return $"{F(e[e.Count - 1], "0")}° and steeper";
        return $"{F(e[bin - 1], "0")}–{F(e[bin], "0")}°";
    }

    private static string SeedList(List<uint> seeds)
    {
        if (seeds.Count == 0) return "";
        string shown = string.Join(", ", seeds.Take(ListedSeeds));
        return seeds.Count > ListedSeeds ? $"{shown} and {seeds.Count - ListedSeeds} more" : shown;
    }

    private static string Bar(double share) => new string('█', (int)Math.Round(Math.Clamp(share, 0, 1) * 40));

    private static string Cell(string text) => text.Replace("|", "/").Replace("\r", " ").Replace("\n", " ");

    // ---------- CSV ----------

    /// <summary>The CSV keys of the stats part of a row, in order: the columns, the slope bins, then placed facilities per kind.</summary>
    public static string StatsHeader()
    {
        var keys = new List<string>();
        keys.AddRange(Columns.Select(c => c.Key));
        for (int b = 0; b <= IslandStats.SlopeBinEdgesDeg.Count; b++) keys.Add($"slope_bin{b}_cells");
        keys.AddRange(IslandWorks.Facilities.Select(f => $"placed_{f.Key}"));
        return string.Join(",", keys);
    }

    /// <summary>
    /// The stats as one CSV row matching <see cref="StatsHeader"/>, at most six decimals, invariant culture.
    /// The golden-island tests pin these rows.
    /// </summary>
    public static string StatsRow(IslandStats s)
    {
        var values = new List<string>();
        values.AddRange(Columns.Select(c => F(c.Get(s), "0.######")));
        values.AddRange(s.SlopeCells.Select(v => v.ToString(CultureInfo.InvariantCulture)));
        values.AddRange(s.Facilities.Select(f => f.Placed.ToString(CultureInfo.InvariantCulture)));
        return string.Join(",", values);
    }

    /// <summary>The CSV keys of the route part of a row (step 24b).</summary>
    public static string ReachHeader() =>
        "anchors,anchor_pairs,anchor_gaps,tunnel_gaps,deep_gaps,no_ground_gaps,must_ford_pairs,slowest_anchor_minutes,worst_dry_detour";

    /// <summary>The routes as one CSV row matching <see cref="ReachHeader"/>. The golden-island tests pin these rows.</summary>
    public static string ReachRow(IslandReach r) => string.Join(",",
        r.Anchors.Count.ToString(CultureInfo.InvariantCulture), r.Routes.Count.ToString(CultureInfo.InvariantCulture),
        r.Gaps.ToString(CultureInfo.InvariantCulture), r.TunnelGaps.ToString(CultureInfo.InvariantCulture),
        r.DeepGaps.ToString(CultureInfo.InvariantCulture), r.GroundGaps.ToString(CultureInfo.InvariantCulture),
        r.MustFord.ToString(CultureInfo.InvariantCulture), F(r.SlowestMinutes, "0.######"), F(r.WorstDryDetour, "0.######"));

    /// <summary>The CSV keys of the checks part of a row (step 24c).</summary>
    public static string ChecksHeader() => "passed,failed_checks,warned_checks";

    /// <summary>The checks as one CSV row matching <see cref="ChecksHeader"/>; check keys joined with ';'.</summary>
    public static string ChecksRow(IslandValidation v) =>
        $"{(v.Passed ? 1 : 0)},{string.Join(";", v.Failures.Select(f => f.Key))},{string.Join(";", v.Warnings.Select(f => f.Key))}";

    /// <summary>One row per seed: seed, seconds, the stats, routes and checks (empty when missing), then the error, if any.</summary>
    public static string Csv(IReadOnlyList<IslandSeedResult> results)
    {
        int statsFields = StatsHeader().Split(',').Length;
        int reachFields = ReachHeader().Split(',').Length;
        int checkFields = ChecksHeader().Split(',').Length;
        var sb = new StringBuilder();
        sb.Append("seed,seconds,").Append(StatsHeader()).Append(',').Append(ReachHeader()).Append(',').Append(ChecksHeader()).AppendLine(",error");
        foreach (IslandSeedResult r in results)
        {
            string stats = r.Stats is null ? new string(',', statsFields - 1) : StatsRow(r.Stats);
            string reach = r.Reach is null ? new string(',', reachFields - 1) : ReachRow(r.Reach);
            string checks = r.Validation is null ? new string(',', checkFields - 1) : ChecksRow(r.Validation);
            string error = (r.Error ?? "").Replace(",", ";").Replace("\"", "'").Replace("\r", " ").Replace("\n", " ");
            sb.Append(r.Seed).Append(',').Append(F(r.Seconds, "0.00")).Append(',').Append(stats).Append(',')
                .Append(reach).Append(',').Append(checks).Append(',').AppendLine(error);
        }
        return sb.ToString();
    }

    // ---------- console ----------

    public static string ConsoleSummary(IReadOnlyList<IslandSeedResult> results)
    {
        var ok = Generated(results);
        var sb = new StringBuilder();
        sb.AppendLine($"{ok.Count} of {results.Count} islands generated.");
        if (ok.Count == 0) return sb.ToString();
        int width = Columns.Max(c => c.Name.Length);
        sb.AppendLine($"  {"Measure".PadRight(width)}  {"min",9}  {"median",9}  {"max",9}");
        foreach (Spread sp in Spreads(results))
        {
            string fmt = sp.Column.Format;
            sb.AppendLine($"  {sp.Column.Name.PadRight(width)}  {F(sp.Min, fmt),9}  {F(sp.Median, fmt),9}  {F(sp.Max, fmt),9}");
        }
        int facShort = ok.Count(r => r.Stats!.FacilitiesShort > 0);
        int padShort = ok.Count(r => r.Stats!.PaddocksShort > 0);
        int lowBeach = ok.Count(r => r.Stats!.BeachShare < DecidedBeachShare);
        sb.AppendLine($"Seeds short of a facility: {facShort}; of a paddock: {padShort}; under {F(100 * DecidedBeachShare, "0")}% beach: {lowBeach}; "
            + $"failed to generate: {results.Count - ok.Count}.");
        var withReach = ok.Where(r => r.Reach is not null).ToList();
        if (withReach.Count > 0)
            sb.AppendLine($"Key-site routes: every pair walkable in {withReach.Count(r => r.Reach!.FullyConnected)} of {withReach.Count} seeds; "
                + $"seeds with a pair needing a tunnel: {withReach.Count(r => r.Reach!.TunnelGaps > 0)}; crossing deep water: "
                + $"{withReach.Count(r => r.Reach!.DeepGaps > 0)}; with no ground or no route: {withReach.Count(r => r.Reach!.GroundGaps > 0)}.");
        var checkedSeeds = ok.Where(r => r.Validation is not null).ToList();
        if (checkedSeeds.Count > 0)
        {
            string Counts(Func<IslandValidation, IReadOnlyList<CheckResult>> pick)
            {
                var keys = checkedSeeds.SelectMany(r => pick(r.Validation!).Select(x => x.Key)).GroupBy(k => k)
                    .Select(g => $"{g.Key} {g.Count()}").ToList();
                return keys.Count == 0 ? "none" : string.Join(", ", keys);
            }
            sb.AppendLine($"Checks: {checkedSeeds.Count(r => r.Validation!.Passed)} of {checkedSeeds.Count} seeds pass; "
                + $"failing: {Counts(v => v.Failures)}; warnings: {Counts(v => v.Warnings)}.");
        }
        return sb.ToString();
    }

    private static List<IslandSeedResult> Generated(IReadOnlyList<IslandSeedResult> results) =>
        results.Where(r => r.Stats is not null).OrderBy(r => r.Seed).ToList();

    private static string F(double v, string format) => v.ToString(format, CultureInfo.InvariantCulture);
}
