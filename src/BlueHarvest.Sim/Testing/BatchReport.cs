using System.Globalization;
using System.Text;
using BlueHarvest.Sim.Core;

namespace BlueHarvest.Sim.Testing;

/// <summary>
/// Turns batch results into things a person can judge at a glance: a Markdown report (verdict
/// first, then a table with bars, then what happened per option), a CSV with one row per run, and a
/// short plain-text summary for the console. The verdict applies the design doc's bar: every option
/// can work, every option can fail, and none dominates.
/// </summary>
public static class BatchReport
{
    /// <summary>An option "dominates" when it gets away this many points more often than the next best.</summary>
    public const int DominanceMarginPoints = 25;

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
        int Npcs)
    {
        public int GotAwayPercent => Percent(GotAway, Runs);
    }

    /// <summary>One verdict line: a check, whether it passed, and why.</summary>
    public sealed record Check(string Name, bool Passed, string Detail);

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
                    Tally(runs.Where(r => r.Outcome == RunOutcome.Died).Select(r => r.DeathCause ?? "unknown")),
                    Tally(runs.Where(r => r.Exit is not null).Select(r => r.Exit!)),
                    runs.Count(r => r.HuntedTick is not null),
                    runs.Count == 0 ? 0 : runs.Average(r => r.NpcsDied),
                    runs.Count == 0 ? 0 : runs.Average(r => r.NpcsEscaped),
                    runs.Max(r => r.Npcs));
            })
            .ToList();

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

        var dominance = new List<string>();
        var spreads = new List<string>();
        foreach (bool npcs in new[] { true, false })
        {
            var ranked = rows.Where(r => r.WithNpcs == npcs).OrderByDescending(r => r.GotAwayPercent).ToList();
            if (ranked.Count < 2)
                continue;
            int gap = ranked[0].GotAwayPercent - ranked[1].GotAwayPercent;
            string line = $"{(npcs ? "with" : "without")} NPCs, best is {ranked[0].Plan.Describe()} at {ranked[0].GotAwayPercent}%, "
                + $"next {ranked[1].Plan.Describe()} at {ranked[1].GotAwayPercent}%";
            spreads.Add(line);
            if (gap >= DominanceMarginPoints)
                dominance.Add(line);
        }
        checks.Add(new Check("No option dominates", dominance.Count == 0,
            dominance.Count == 0
                ? string.Join("; ", spreads) + $" (dominant = {DominanceMarginPoints}+ points ahead)"
                : string.Join("; ", dominance) + $" ({DominanceMarginPoints}+ points ahead)"));

        int breached = results.Count(r => r.BreachTick is not null);
        var breachTime = Median(results.Where(r => r.BreachTick is not null).Select(r => r.BreachTick!.Value));
        checks.Add(new Check("The Rex breaks out at the start", results.Count > 0 && breached == results.Count,
            $"fence broken in {Percent(breached, results.Count)}% of runs"
            + (breachTime is { } t ? $", median {Clock(t)} in" : "")));

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
          .Append($"**Seeds** {settings.FirstSeed}–{settings.FirstSeed + (ulong)Math.Max(0, settings.RunsPerOption - 1)}\n\n");

        sb.Append("## Verdict\n\n| Check | Result | Detail |\n|---|---|---|\n");
        foreach (var check in Verdict(results))
            sb.Append($"| {check.Name} | {(check.Passed ? "✅ yes" : "❌ no")} | {check.Detail} |\n");

        sb.Append("\n## Got away, by option\n\n");
        sb.Append("*Got away* = escaped through a hatch, or still alive and safe from the Rex when time ran out.\n\n");
        sb.Append("| Option | NPCs | Got away | Escaped | Safe at end | Hunted at end | Died | Median time to escape | Main cause of death |\n");
        sb.Append("|---|---|---|---:|---:|---:|---:|---:|---|\n");
        foreach (var row in rows)
        {
            sb.Append($"| {row.Plan.Describe()} | {(row.WithNpcs ? "with" : "without")} | `{Bar(row.GotAway, row.Runs)}` {row.GotAwayPercent}% ")
              .Append($"| {row.Escaped} | {row.SafeAtEnd} | {row.HuntedAtEnd} | {row.Died} ")
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
        sb.Append(".\n\n");

        sb.Append("## How to read this\n\n");
        sb.Append("- **Escaped**: reached an exit (jungle trail, floor hatch, culvert). The run ends there.\n");
        sb.Append("- **Safe at end**: alive at the time limit, out of the Rex's reach and not its focus (the design doc's escape check).\n");
        sb.Append("- **Hunted at end**: alive at the time limit, but the Rex could still get to them or was still after them.\n");
        sb.Append("- **Died**: killed before the time limit; the cause is the wound that did it.\n");
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
            parts.Add($"{row.Died} died, a median {Clock(row.MedianDeathTicks ?? 0)} in: "
                + string.Join(", ", row.DeathCauses.Select(c => $"{c.Cause} ×{c.Count}")) + ".");
        }
        if (row.HuntedAtEnd > 0)
            parts.Add($"{row.HuntedAtEnd} still had the Rex on them at the end.");
        parts.Add($"The Rex hunted the player in {row.Hunted} of {row.Runs} runs.");
        if (row.Npcs > 0)
            parts.Add($"NPCs: {row.NpcsDiedPerRun.ToString("0.0", CultureInfo.InvariantCulture)} died and "
                + $"{row.NpcsEscapedPerRun.ToString("0.0", CultureInfo.InvariantCulture)} escaped per run, of {row.Npcs}.");
        return string.Join(" ", parts);
    }

    /// <summary>One CSV row per run, for spreadsheets.</summary>
    public static string Csv(IReadOnlyList<RunResult> results)
    {
        var sb = new StringBuilder("option,npcs,seed,outcome,got_away,end_seconds,exit,death_cause,breach_seconds,hunted_seconds,npcs_total,npcs_died,npcs_escaped\n");
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
              .Append(r.Npcs).Append(',').Append(r.NpcsDied).Append(',').Append(r.NpcsEscaped).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>A short plain-text summary for the console: the verdict and one line per row.</summary>
    public static string ConsoleSummary(IReadOnlyList<RunResult> results)
    {
        var sb = new StringBuilder();
        sb.Append("VERDICT\n");
        foreach (var check in Verdict(results))
            sb.Append($"  [{(check.Passed ? "PASS" : "FAIL")}] {check.Name}: {check.Detail}\n");
        sb.Append('\n');
        sb.Append($"  {"Option",-24} {"NPCs",-8} {"Got away",-16} {"Escaped",8} {"Safe",6} {"Hunted",7} {"Died",6}  {"Escape time",11}\n");
        foreach (var row in Rows(results))
        {
            sb.Append($"  {row.Plan.Describe(),-24} {(row.WithNpcs ? "with" : "without"),-8} ")
              .Append($"{Bar(row.GotAway, row.Runs)} {row.GotAwayPercent,3}%  ")
              .Append($"{row.Escaped,8} {row.SafeAtEnd,6} {row.HuntedAtEnd,7} {row.Died,6}  ")
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
