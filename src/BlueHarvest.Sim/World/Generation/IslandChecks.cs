using System.Globalization;

namespace BlueHarvest.Sim.World.Generation;

/// <summary>What failing a check means: Fail rejects the seed, Warn only reports it (decision 4).</summary>
public enum CheckSeverity : byte
{
    Fail,
    Warn,
}

/// <summary>
/// What a check can read. Later steps append fields with defaults (start sites in 32, escapes in 33,
/// safe zones in 47), so existing checks keep working.
/// </summary>
public sealed record IslandCheckContext(IslandStats Stats, IslandReach Reach);

/// <summary>One rule: Test returns null when the island passes, or a short reason when it doesn't.</summary>
public sealed record IslandCheck(string Key, string Name, CheckSeverity Severity, Func<IslandCheckContext, string?> Test);

/// <summary>A check the island didn't pass, and why.</summary>
public sealed record CheckResult(string Key, string Name, CheckSeverity Severity, string Reason)
{
    public override string ToString() => $"{Key}: {Reason}";
}

/// <summary>Every check an island didn't pass. It passes when nothing failed; warnings never reject it.</summary>
public sealed record IslandValidation(IReadOnlyList<CheckResult> Failures, IReadOnlyList<CheckResult> Warnings)
{
    public bool Passed => Failures.Count == 0;

    /// <summary>The failures in one line ("lowland 28% of dry land, need 30%; beach 9% of the coast, need 15%"), or "".</summary>
    public string FailureSummary => string.Join("; ", Failures.Select(f => f.Reason));

    /// <summary>The warnings in one line, or "".</summary>
    public string WarningSummary => string.Join("; ", Warnings.Select(f => f.Reason));
}

/// <summary>
/// Seed validation (step 24c): the rules a generated island must pass, as data. Thresholds were set on
/// 16 Nov 2026 from 200 seeds at the 75% base scale, with a margin below the worst seed seen.
/// The runner, the explorer and later steps all read this one table.
/// </summary>
public static class IslandChecks
{
    /// <summary>Land as a share of the island frame (seen 26–37%).</summary>
    public const double LandMinShare = 0.22, LandMaxShare = 0.42;

    /// <summary>Dry land below <see cref="IslandStats.LowlandBelowM"/> (seen 36% and up).</summary>
    public const double LowlandMinShare = 0.30;

    /// <summary>Beach share of the main island's coast (decided: at least about 15%; seen 15.2% and up).</summary>
    public const double BeachMinShare = 0.15;

    /// <summary>Dry land that is walkable and joined to the main walkable ground (seen 95.9% and up).</summary>
    public const double WalkableConnectedMinShare = 0.93;

    /// <summary>Islets wanted by the design (seen 1–10).</summary>
    public const int IsletsMin = 3, IsletsMax = 8;

    public static IReadOnlyList<IslandCheck> All { get; } =
    [
        new IslandCheck("land", "Land", CheckSeverity.Fail, c =>
            c.Stats.LandFraction < LandMinShare || c.Stats.LandFraction > LandMaxShare
                ? $"land {P(c.Stats.LandFraction)} of the frame, need {P(LandMinShare)}–{P(LandMaxShare)}" : null),
        new IslandCheck("lowland", "Lowland", CheckSeverity.Fail, c =>
            c.Stats.LowlandShare < LowlandMinShare
                ? $"lowland {P(c.Stats.LowlandShare)} of dry land, need {P(LowlandMinShare)}" : null),
        new IslandCheck("beach", "Beach", CheckSeverity.Fail, c =>
            c.Stats.BeachShare < BeachMinShare
                ? $"beach {P(c.Stats.BeachShare)} of the coast, need {P(BeachMinShare)}" : null),
        new IslandCheck("walkable", "Walkable ground", CheckSeverity.Fail, c =>
            c.Stats.WalkableConnectedShare < WalkableConnectedMinShare
                ? $"walkable ground {P(c.Stats.WalkableConnectedShare)} of dry land, need {P(WalkableConnectedMinShare)}" : null),
        new IslandCheck("facilities", "Facilities", CheckSeverity.Fail, c => Missing(c.Stats, major: true)),
        new IslandCheck("facilities-minor", "Minor facilities short", CheckSeverity.Warn, c => Missing(c.Stats, major: false)),
        new IslandCheck("paddocks", "Paddocks", CheckSeverity.Fail, c =>
            c.Stats.PaddocksShort > 0 ? $"{c.Stats.PaddocksPlaced} of {c.Stats.PaddocksWanted} paddocks placed" : null),
        new IslandCheck("anchors", "Key-site routes", CheckSeverity.Fail, c => Routes(c.Reach)),
        new IslandCheck("rivers", "Rivers", CheckSeverity.Warn, c =>
            c.Stats.RiversToSea < c.Stats.Rivers || c.Stats.InlandPitCells > 0
                ? $"{c.Stats.Rivers - c.Stats.RiversToSea} rivers don't reach the sea, {c.Stats.InlandPitCells} inland pit cells" : null),
        new IslandCheck("islets", "Islets", CheckSeverity.Warn, c =>
            c.Stats.Islets < IsletsMin || c.Stats.Islets > IsletsMax
                ? $"{c.Stats.Islets} islets, the design asks for {IsletsMin}–{IsletsMax}" : null),
    ];

    /// <summary>Runs every check.</summary>
    public static IslandValidation Run(IslandCheckContext context)
    {
        var failures = new List<CheckResult>();
        var warnings = new List<CheckResult>();
        foreach (IslandCheck check in All)
        {
            string? reason = check.Test(context);
            if (reason is null) continue;
            var result = new CheckResult(check.Key, check.Name, check.Severity, reason);
            (check.Severity == CheckSeverity.Fail ? failures : warnings).Add(result);
        }
        return new IslandValidation(failures, warnings);
    }

    /// <summary>
    /// Major kinds must be placed in full and minor kinds at least once (major: true, a Fail); a minor kind
    /// placed but short is only a Warn (major: false).
    /// </summary>
    private static string? Missing(IslandStats s, bool major)
    {
        var shortOf = s.Facilities.Where(f => f.Short > 0
            && (major ? f.Major || f.Placed == 0 : !f.Major && f.Placed > 0)).ToList();
        if (shortOf.Count == 0) return null;
        return string.Join(", ", shortOf.Select(f => $"{f.Name} {f.Placed} of {f.Wanted}")) + " placed";
    }

    private static string? Routes(IslandReach reach)
    {
        if (reach.FullyConnected) return null;
        var parts = new List<string>();
        if (reach.TunnelGaps > 0) parts.Add($"{reach.TunnelGaps} need a tunnel");
        if (reach.DeepGaps > 0) parts.Add($"{reach.DeepGaps} cross deep water");
        if (reach.GroundGaps > 0) parts.Add($"{reach.GroundGaps} have no ground or no route");
        return $"{reach.Gaps} of {reach.Routes.Count} key-site pairs can't be walked ({string.Join(", ", parts)})";
    }

    private static string P(double share) => (100 * share).ToString("0.#", CultureInfo.InvariantCulture) + "%";
}
