using System.Globalization;
using System.Text;
using BlueHarvest.Sim.Core;

namespace BlueHarvest.Sim.Testing;

/// <summary>
/// Turns batch results into things a person can judge at a glance: a Markdown report (verdict
/// first, then a table with bars, then what happened per option), a CSV with one row per run, and a
/// short plain-text summary for the console. The verdict applies the design doc's bar: every option
/// can work, every option can fail, and none dominates; it also checks the runs themselves (the
/// breach happened, and the seeds made a difference).
/// </summary>
public static class BatchReport
{
    /// <summary>An option "dominates" when it gets away this many points more often than the next best.</summary>
    public const int DominanceMarginPoints = 25;

    /// <summary>Runs of one option that all end the same way within this long count as "the seeds changed nothing".</summary>
    public const int SameRunTicks = 100;

    /// <summary>The numbers for one table row: one plan, with or without NPCs.</summary>
    public sealed record Row(
        EscapePlan Plan,
        bool WithNpcs,
        int Runs,
        int GotAway,
        int Escaped,
        int SafeAtEnd,
        int HuntedAtEnd,
        int Died,
        long? MedianEscapeTicks,
        long? MedianDeathTicks,
        IReadOnlyList<(string Cause, int Count)> DeathCauses,
        IReadOnlyList<(string Exit, int Count)> Exits,
        int Hunted,
        double NpcsDiedPerRun,
        double NpcsEscapedPerRun,
        int Npcs,
        int RunDown,
        bool AllAlike,
        IReadOnlyList<(string Wound, int Count)> Wounds)
    {
        public int GotAwayPercent => Percent(GotAway, Runs);
    }

    /// <summary>Where the reaction-delay bands split: under 3 s, 3–6 s, 6 s or more.</summary>
    public static IReadOnlyList<int> ReactionBandTicks { get; } = new[] { 300, 600 };

    /// <summary>One option's deaths split by how long the survivor took to react: (died, runs) per band.</summary>
    public sealed record ReactionRow(EscapePlan Plan, IReadOnlyList<(int Died, int Runs)> Bands);

    /// <summary>One verdict line: a check, whether it passed (null: it can't be judged yet), and why.</summary>
    public sealed record Check(string Name, bool? Passed, string Detail);

    public static IReadOnlyList<Row> Rows(IReadOnlyList<RunResult> results) =>
        results
            .GroupBy(r => (r.Plan, r.WithNpcs))
            .OrderBy(g => g.Key.Plan).ThenByDescending(g => g.Key.WithNpcs)
            .Select(g =>
            {
                var runs = g.ToList();
                return new Row(
                    g.Key.Plan, g.Key.WithNpcs, runs.Count,
                    runs.Count(r => r.GotAway),
                    runs.Count(r => r.Outcome == RunOutcome.Escaped),
                    runs.Count(r => r.Outcome == RunOutcome.SafeAtEnd),
                    runs.Count(r => r.Outcome == RunOutcome.HuntedAtEnd),
                    runs.Count(r => r.Outcome == RunOutcome.Died),
                    Median(runs.Where(r => r.Outcome == RunOutcome.Escaped).Select(r => r.EndTick)),
                    Median(runs.Where(r => r.Outcome == RunOutcome.Died).Select(r => r.EndTick)),
                    Tally(runs.Where(r => r.Outcome == RunOutcome.Died).Select(r => Killer(r.DeathCause))),
                    Tally(runs.Where(r => r.Exit is not null).Select(r => r.Exit!)),
                    runs.Count(r => r.HuntedTick is not null),
                    runs.Count == 0 ? 0 : runs.Average(r => r.NpcsDied),
                    runs.Count == 0 ? 0 : runs.Average(r => r.NpcsEscaped),
                    runs.Max(r => r.Npcs),
                    runs.Count(r => r.CaughtByCharge),
                    runs.Count >= 3 && runs.All(r => r.Outcome == runs[0].Outcome)
                        && runs.Max(r => r.EndTick) - runs.Min(r => r.EndTick) < SameRunTicks,
                    Tally(runs.Where(r => r.Outcome == RunOutcome.Died).Select(r => Wound(r.DeathCause)).OfType<string>()));
            })
            .ToList();

    /// <summary>
    /// Every option that leaves the vehicle, its runs (with and without NPCs together) split by how
    /// long the survivor sat after the breach, and how many of each died.
    /// </summary>
    public static IReadOnlyList<ReactionRow> ReactionRows(IReadOnlyList<RunResult> results) =>
        results
            .Where(r => r.Plan != EscapePlan.StayInVehicle)
            .GroupBy(r => r.Plan)
            .OrderBy(g => g.Key)
            .Select(g => new ReactionRow(g.Key, Enumerable.Range(0, ReactionBandTicks.Count + 1)
                .Select(band =>
                {
                    var inBand = g.Where(r => ReactionBand(r.ReactionTicks) == band).ToList();
                    return (inBand.Count(r => r.Outcome == RunOutcome.Died), inBand.Count);
                })
                .ToList()))
            .ToList();

    /// <summary>Which band a reaction delay falls in: 0 for the quickest.</summary>
    public static int ReactionBand(int ticks)
    {
        int band = 0;
        while (band < ReactionBandTicks.Count && ticks >= ReactionBandTicks[band])
            band++;
        return band;
    }

    /// <summary>A band's heading: "under 3 s", "3–6 s", "6 s or more".</summary>
    public static string ReactionBandLabel(int band)
    {
        int last = ReactionBandTicks.Count;
        if (band <= 0)
            return $"under {ReactionBandTicks[0] / Ticks.PerSecond} s";
        if (band >= last)
            return $"{ReactionBandTicks[last - 1] / Ticks.PerSecond} s or more";
        return $"{ReactionBandTicks[band - 1] / Ticks.PerSecond}–{ReactionBandTicks[band] / Ticks.PerSecond} s";
    }

    /// <summary>What killed them: "bitten by the Rex" from "bitten by the Rex: a second major wound to the left leg".</summary>
    public static string Killer(string? cause)
    {
        if (string.IsNullOrWhiteSpace(cause))
            return "unknown";
        int colon = cause.IndexOf(':');
        return colon > 0 ? cause[..colon].Trim() : cause.Trim();
    }

    /// <summary>Which wound did it: "left leg" from "...: a second major wound to the left leg", or null.</summary>
    public static string? Wound(string? cause)
    {
        const string marker = "wound to the ";
        if (cause is null)
            return null;
        int at = cause.IndexOf(marker, StringComparison.Ordinal);
        return at >= 0 ? cause[(at + marker.Length)..].Trim() : null;
    }

    /// <summary>The design doc's bar, checked separately with and without NPCs, plus the opening.</summary>
    public static IReadOnlyList<Check> Verdict(IReadOnlyList<RunResult> results)
    {
        var rows = Rows(results);
        var checks = new List<Check>();

        var neverWorked = rows.Where(r => r.GotAway == 0).ToList();
        checks.Add(new Check("Every option can work", neverWorked.Count == 0,
            neverWorked.Count == 0
                ? "every option got away at least once"
                : "never got away: " + string.Join("; ", neverWorked.Select(Label))));

        var neverFailed = rows.Where(r => r.GotAway == r.Runs).ToList();
        checks.Add(new Check("Every option can fail", neverFailed.Count == 0,
            neverFailed.Count == 0
                ? "every option failed at least once"
                : "always got away: " + string.Join("; ", neverFailed.Select(Label))));

        // Dominance can't be judged while several options are pinned at 100%: they all "win".
        var dominance = new List<string>();
        var spreads = new List<string>();
        var pinned = new List<string>();
        foreach (bool npcs in new[] { true, false })
        {
            var ranked = rows.Where(r => r.WithNpcs == npcs).OrderByDescending(r => r.GotAwayPercent).ToList();
            if (ranked.Count < 2)
                continue;
            string setting = npcs ? "with" : "without";
            int atCeiling = ranked.Count(r => r.GotAway == r.Runs);
            if (atCeiling >= 2)
            {
                pinned.Add($"{setting} NPCs, {atCeiling} options always got away");
                continue;
            }
            int gap = ranked[0].GotAwayPercent - ranked[1].GotAwayPercent;
            string line = $"{setting} NPCs, best is {ranked[0].Plan.Describe()} at {ranked[0].GotAwayPercent}%, "
                + $"next {ranked[1].Plan.Describe()} at {ranked[1].GotAwayPercent}%";
            spreads.Add(line);
            if (gap >= DominanceMarginPoints)
                dominance.Add(line);
        }
        if (dominance.Count > 0)
        {
            checks.Add(new Check("No option dominates", false,
                string.Join("; ", dominance) + $" ({DominanceMarginPoints}+ points ahead)"));
        }
        else if (pinned.Count > 0)
        {
            checks.Add(new Check("No option dominates", null,
                "can't tell while options tie at 100%: " + string.Join("; ", pinned.Concat(spreads))));
        }
        else
        {
            checks.Add(new Check("No option dominates", true,
                string.Join("; ", spreads) + $" (dominant = {DominanceMarginPoints}+ points ahead)"));
        }

        int breached = results.Count(r => r.BreachTick is not null);
        var breachTime = Median(results.Where(r => r.BreachTick is not null).Select(r => r.BreachTick!.Value));
        checks.Add(new Check("The Rex breaks out at the start", results.Count > 0 && breached == results.Count,
            $"fence broken in {Percent(breached, results.Count)}% of runs"
            + (breachTime is { } t ? $", median {Clock(t)} in" : "")));

        var alike = rows.Where(r => r.AllAlike).ToList();
        checks.Add(new Check("The seeds make a difference", alike.Count == 0,
            alike.Count == 0
                ? "every option played out differently across seeds"
                : "every run ended the same way at the same moment: " + string.Join("; ", alike.Select(Label))
                  + " (so those rows are one run, repeated)"));

        return checks;
    }

    /// <summary>The full Markdown report.</summary>
    public static string Markdown(IReadOnlyList<RunResult> results, BatchSettings settings)
    {
        var rows = Rows(results);
        var sb = new StringBuilder();
        sb.Append("# Testbed batch report\n\n");
        sb.Append($"**Map** {settings.MapName} · **Weather** {settings.WeatherDescription} · ")
          .Append($"**Runs** {settings.RunsPerOption} per option, with and without NPCs ({results.Count} in all) · ")
          .Append($"**Time limit** {Clock(settings.TimeLimitTicks)} · ")
          .Append($"**Seeds** {settings.FirstSeed}–{settings.FirstSeed + (ulong)Math.Max(0, settings.RunsPerOption - 1)} · ")
          .Append($"**Each seed varies** the Rex's start (±{BatchRunner.RexJitterX} tiles across, ±{BatchRunner.RexJitterY} down, facing S/SE/SW) ")
          .Append($"and how long the survivor takes to react to the breach (0–{BatchRunner.MaxReactionTicks / Ticks.PerSecond} s)\n\n");

        sb.Append("## Verdict\n\n| Check | Result | Detail |\n|---|---|---|\n");
        foreach (var check in Verdict(results))
            sb.Append($"| {check.Name} | {Mark(check.Passed)} | {check.Detail} |\n");

        sb.Append("\n## Got away, by option\n\n");
        sb.Append("*Got away* = escaped through a hatch, or still alive and safe from the Rex when time ran out.\n\n");
        sb.Append("| Option | NPCs | Got away | Escaped | Safe at end | Hunted at end | Died | Run down | Median time to escape | Main cause of death |\n");
        sb.Append("|---|---|---|---:|---:|---:|---:|---:|---:|---|\n");
        foreach (var row in rows)
        {
            sb.Append($"| {row.Plan.Describe()} | {(row.WithNpcs ? "with" : "without")} | `{Bar(row.GotAway, row.Runs)}` {row.GotAwayPercent}% ")
              .Append($"| {row.Escaped} | {row.SafeAtEnd} | {row.HuntedAtEnd} | {row.Died} | {row.RunDown} ")
              .Append($"| {(row.MedianEscapeTicks is { } e ? Clock(e) : "–")} ")
              .Append($"| {(row.DeathCauses.Count > 0 ? $"{row.DeathCauses[0].Cause} ({row.DeathCauses[0].Count})" : "–")} |\n");
        }

        sb.Append("\n## What happened, option by option\n\n");
        foreach (var plan in rows.Select(r => r.Plan).Distinct())
        {
            sb.Append($"**{plan.Describe()}**\n\n");
            foreach (var row in rows.Where(r => r.Plan == plan))
                sb.Append("- ").Append(Story(row)).Append('\n');
            sb.Append('\n');
        }

        var opening = results.Where(r => r.HuntedTick is not null).ToList();
        sb.Append("## The opening\n\n");
        sb.Append($"- The Rex broke through the fence in {Percent(results.Count(r => r.BreachTick is not null), results.Count)}% of runs");
        if (Median(results.Where(r => r.BreachTick is not null).Select(r => r.BreachTick!.Value)) is { } breach)
            sb.Append($", a median {Clock(breach)} in");
        sb.Append(".\n");
        sb.Append($"- It hunted the player at some point in {Percent(opening.Count, results.Count)}% of runs");
        if (Median(opening.Select(r => r.HuntedTick!.Value)) is { } huntedAt)
            sb.Append($", first a median {Clock(huntedAt)} in");
        sb.Append(".\n");
        var started = results.Where(r => r.StartedTick is not null && r.BreachTick is not null && r.StartedTick >= r.BreachTick).ToList();
        if (Median(started.Select(r => r.StartedTick!.Value - r.BreachTick!.Value)) is { } reaction)
            sb.Append($"- Survivors set off a median {Clock(reaction)} after the breach.\n");
        int runDown = results.Count(r => r.CaughtByCharge);
        int died = results.Count(r => r.Outcome == RunOutcome.Died);
        if (died > 0)
            sb.Append($"- {runDown} of the {died} deaths came within {BatchRunner.RunDownTicks / Ticks.PerSecond} s of a charge (run down in the open).\n");
        sb.Append('\n');

        var reactions = ReactionRows(results);
        if (reactions.Count > 0 && results.Any(r => r.ReactionTicks > 0))
        {
            sb.Append("## Reacting late\n\n");
            sb.Append("How long the survivor sat in the vehicle after the breach before setting off, and how often those runs died ")
              .Append("(with and without NPCs together). If the late starters die far more often, the option is decided in its first few seconds.\n\n");
            sb.Append("| Option |");
            for (int band = 0; band <= ReactionBandTicks.Count; band++)
                sb.Append($" set off {ReactionBandLabel(band)} after |");
            sb.Append("\n|---|");
            for (int band = 0; band <= ReactionBandTicks.Count; band++)
                sb.Append("---:|");
            sb.Append('\n');
            foreach (var row in reactions)
            {
                sb.Append($"| {row.Plan.Describe()} |");
                foreach (var (bandDied, bandRuns) in row.Bands)
                    sb.Append(bandRuns == 0 ? " – |" : $" {Percent(bandDied, bandRuns)}% died ({bandDied} of {bandRuns}) |");
                sb.Append('\n');
            }
            sb.Append('\n');
        }

        sb.Append("## How to read this\n\n");
        sb.Append("- **Escaped**: reached an exit (jungle trail, floor hatch, culvert). The run ends there.\n");
        sb.Append("- **Safe at end**: alive at the time limit, out of the Rex's reach and not its focus (the design doc's escape check).\n");
        sb.Append("- **Hunted at end**: alive at the time limit, but the Rex could still get to them or was still after them.\n");
        sb.Append("- **Died**: killed before the time limit. **Run down**: of those, killed within 3 s of the Rex charging.\n");
        sb.Append("- **Reacting late**: the same option, split by how long the survivor waited after the breach. A big jump from left to right means where the Rex is when you step out decides it.\n");
        sb.Append($"- **The seeds make a difference** fails when every run of an option ends the same way within {SameRunTicks / Ticks.PerSecond} s: then 30 runs are one run, repeated.\n");
        sb.Append($"- **The bar**: every option should sometimes get away and sometimes fail, and none should be {DominanceMarginPoints}+ points ahead of the rest.\n");
        sb.Append("- Every option runs on the same seeds, so a difference between options is the option, not the dice.\n");
        sb.Append("- The scripted players follow their plan blindly; a real player adapts. Read low numbers as \"this plan, done blindly\".\n");
        return sb.ToString();
    }

    /// <summary>One sentence about a row: "With NPCs: 35 of 50 got away (all through the jungle trail, median 1:12). 13 died: ..."</summary>
    public static string Story(Row row)
    {
        var parts = new List<string>
        {
            $"{(row.WithNpcs ? $"With NPCs ({row.Npcs})" : "Without NPCs")}: {row.GotAway} of {row.Runs} got away",
        };
        if (row.Escaped > 0)
        {
            string exits = row.Exits.Count == 1
                ? $"all through the {row.Exits[0].Exit}"
                : string.Join(", ", row.Exits.Select(e => $"{e.Count} through the {e.Exit}"));
            parts[0] += $" ({exits}, median {Clock(row.MedianEscapeTicks ?? 0)}";
            parts[0] += row.SafeAtEnd > 0 ? $"; {row.SafeAtEnd} more safe at the end)" : ")";
        }
        else if (row.SafeAtEnd > 0)
        {
            parts[0] += " (safe when time ran out)";
        }
        parts[0] += ".";
        if (row.Died > 0)
        {
            string wounds = row.Wounds.Count == 0 ? ""
                : " (the fatal wound: " + string.Join(", ", row.Wounds.Take(3).Select(w => $"{w.Wound} {w.Count}"))
                  + (row.Wounds.Count > 3 ? ", …" : "") + ")";
            parts.Add($"{row.Died} died, a median {Clock(row.MedianDeathTicks ?? 0)} in: "
                + string.Join(", ", row.DeathCauses.Select(c => $"{c.Cause} ×{c.Count}")) + wounds + ".");
            if (row.RunDown > 0)
                parts.Add($"{row.RunDown} of them were run down by a charge.");
        }
        if (row.HuntedAtEnd > 0)
            parts.Add($"{row.HuntedAtEnd} still had the Rex on them at the end.");
        parts.Add($"The Rex hunted the player in {row.Hunted} of {row.Runs} runs.");
        if (row.AllAlike)
            parts.Add("⚠️ Every run went the same way: the seeds changed nothing here.");
        if (row.Npcs > 0)
            parts.Add($"NPCs: {row.NpcsDiedPerRun.ToString("0.0", CultureInfo.InvariantCulture)} died and "
                + $"{row.NpcsEscapedPerRun.ToString("0.0", CultureInfo.InvariantCulture)} escaped per run, of {row.Npcs}.");
        return string.Join(" ", parts);
    }

    /// <summary>One CSV row per run, for spreadsheets.</summary>
    public static string Csv(IReadOnlyList<RunResult> results)
    {
        var sb = new StringBuilder("option,npcs,seed,outcome,got_away,end_seconds,exit,death_cause,breach_seconds,hunted_seconds,npcs_total,npcs_died,npcs_escaped,started_seconds,run_down,reaction_seconds\n");
        foreach (var r in results)
        {
            sb.Append(r.Plan.Key()).Append(',')
              .Append(r.WithNpcs ? "with" : "without").Append(',')
              .Append(r.Seed).Append(',')
              .Append(OutcomeKey(r.Outcome)).Append(',')
              .Append(r.GotAway ? "yes" : "no").Append(',')
              .Append(Seconds(r.EndTick)).Append(',')
              .Append(CsvText(r.Exit)).Append(',')
              .Append(CsvText(r.DeathCause)).Append(',')
              .Append(r.BreachTick is { } b ? Seconds(b) : "").Append(',')
              .Append(r.HuntedTick is { } h ? Seconds(h) : "").Append(',')
              .Append(r.Npcs).Append(',').Append(r.NpcsDied).Append(',').Append(r.NpcsEscaped).Append(',')
              .Append(r.StartedTick is { } st ? Seconds(st) : "").Append(',')
              .Append(r.CaughtByCharge ? "yes" : "no").Append(',')
              .Append(Seconds(r.ReactionTicks)).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>A short plain-text summary for the console: the verdict and one line per row.</summary>
    public static string ConsoleSummary(IReadOnlyList<RunResult> results)
    {
        var sb = new StringBuilder();
        sb.Append("VERDICT\n");
        foreach (var check in Verdict(results))
            sb.Append($"  [{(check.Passed switch { true => "PASS", false => "FAIL", null => " ?? " })}] {check.Name}: {check.Detail}\n");
        sb.Append('\n');
        sb.Append($"  {"Option",-24} {"NPCs",-8} {"Got away",-16} {"Escaped",8} {"Safe",6} {"Hunted",7} {"Died",6} {"Run down",9}  {"Escape time",11}\n");
        foreach (var row in Rows(results))
        {
            sb.Append($"  {row.Plan.Describe(),-24} {(row.WithNpcs ? "with" : "without"),-8} ")
              .Append($"{Bar(row.GotAway, row.Runs)} {row.GotAwayPercent,3}%  ")
              .Append($"{row.Escaped,8} {row.SafeAtEnd,6} {row.HuntedAtEnd,7} {row.Died,6} {row.RunDown,9}  ")
              .Append($"{(row.MedianEscapeTicks is { } e ? Clock(e) : "-"),11}\n");
        }
        return sb.ToString();
    }

    /// <summary>A ten-block bar: ███████░░░ for 70%.</summary>
    public static string Bar(int count, int total)
    {
        int filled = total == 0 ? 0 : (int)Math.Round(10.0 * count / total, MidpointRounding.AwayFromZero);
        return new string('█', filled) + new string('░', 10 - filled);
    }

    /// <summary>Minutes and seconds: 1:05.</summary>
    public static string Clock(long ticks)
    {
        long seconds = ticks / Ticks.PerSecond;
        return $"{seconds / 60}:{seconds % 60:D2}";
    }

    private static string Seconds(long ticks) =>
        (ticks / (double)Ticks.PerSecond).ToString("0.0", CultureInfo.InvariantCulture);

    private static string Mark(bool? passed) => passed switch
    {
        true => "✅ yes",
        false => "❌ no",
        null => "⚠️ can't tell",
    };

    private static string Label(Row row) => $"{row.Plan.Describe()} ({(row.WithNpcs ? "with" : "without")} NPCs)";

    private static int Percent(int count, int total) =>
        total == 0 ? 0 : (int)Math.Round(100.0 * count / total, MidpointRounding.AwayFromZero);

    private static long? Median(IEnumerable<long> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted.Count == 0 ? null : sorted[(sorted.Count - 1) / 2];
    }

    private static IReadOnlyList<(string, int)> Tally(IEnumerable<string> values) =>
        values.GroupBy(v => v).Select(g => (g.Key, g.Count())).OrderByDescending(t => t.Item2).ThenBy(t => t.Key).ToList();

    private static string OutcomeKey(RunOutcome outcome) => outcome switch
    {
        RunOutcome.Escaped => "escaped",
        RunOutcome.SafeAtEnd => "safe_at_end",
        RunOutcome.HuntedAtEnd => "hunted_at_end",
        RunOutcome.Died => "died",
        _ => outcome.ToString(),
    };

    private static string CsvText(string? text) =>
        text is null ? "" : text.Contains(',') || text.Contains('"') ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
}
