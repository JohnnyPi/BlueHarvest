using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Ai;
using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.Scenarios;
using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim.Testing;

/// <summary>How one batch run ended for the scripted player.</summary>
public enum RunOutcome : byte
{
    /// <summary>Reached an exit.</summary>
    Escaped,

    /// <summary>Alive at the time limit, and the escape check holds: out of the Rex's reach and not its focus.</summary>
    SafeAtEnd,

    /// <summary>Alive at the time limit, but the Rex could still reach them or is still after them.</summary>
    HuntedAtEnd,

    Died,
}

/// <summary>One batch run: the plan, the seed, how it ended, and a few timings.</summary>
/// <param name="EndTick">When it escaped or died; the time limit otherwise.</param>
/// <param name="BreachTick">When the Rex first broke a fence tile (the sagging section or any other), if it did.</param>
/// <param name="HuntedTick">When the Rex first made the player its focus as prey, if it did.</param>
/// <param name="StartedTick">When the scripted player set off (after the breach and its reaction time).</param>
/// <param name="CaughtByCharge">Died within 3 s of the Rex charging: it ran them down.</param>
public sealed record RunResult(
    EscapePlan Plan,
    bool WithNpcs,
    ulong Seed,
    RunOutcome Outcome,
    long EndTick,
    string? Exit,
    string? DeathCause,
    long? BreachTick,
    long? HuntedTick,
    int Npcs,
    int NpcsDied,
    int NpcsEscaped,
    long? StartedTick = null,
    bool CaughtByCharge = false,
    int ReactionTicks = 0)
{
    /// <summary>Escaped through an exit, or safe when time ran out: the option worked.</summary>
    public bool GotAway => Outcome is RunOutcome.Escaped or RunOutcome.SafeAtEnd;
}

/// <summary>What a batch was run with, for the report's header.</summary>
public sealed record BatchSettings(string MapName, string WeatherDescription, int RunsPerOption, long TimeLimitTicks, ulong FirstSeed);

/// <summary>
/// Plays the testbed headless with a scripted player per escape option, many seeds each, with and
/// without the NPCs, and records how each run ended. Every run parses its own copy of the map, so
/// runs share nothing and can go in parallel; seeds are the same for every option, so options are
/// compared on the same Rex decisions where their paths haven't diverged.
/// </summary>
public static class BatchRunner
{
    /// <summary>How far the Rex's start moves either way, per seed.</summary>
    public const int RexJitterX = 2, RexJitterY = 1;

    /// <summary>A death this soon after the Rex charged counts as being run down.</summary>
    public const int RunDownTicks = 300;

    /// <summary>The longest the survivor takes to react to the breach, per seed.</summary>
    public const int MaxReactionTicks = 1_000;

    /// <summary>What a seed varies before the run starts: the Rex's start and the survivor's reaction time.</summary>
    public readonly record struct Variation(int RexDx, int RexDy, Direction RexFacing, int ReactionTicks);

    /// <summary>The variation for a seed: the same for every plan, so plans are compared on the same starts.</summary>
    public static Variation VariationFor(ulong seed)
    {
        var rng = new Rng(seed).Fork("batch");
        int dx = rng.NextInt(-RexJitterX, RexJitterX + 1);
        int dy = rng.NextInt(-RexJitterY, RexJitterY + 1);
        var facing = new[] { Direction.South, Direction.SouthEast, Direction.SouthWest }[rng.NextInt(3)];
        return new Variation(dx, dy, facing, rng.NextInt(0, MaxReactionTicks + 1));
    }

    /// <summary>Plays one run to an escape, a death, or the time limit.</summary>
    /// <param name="vary">Apply the seed's variation (Rex start, reaction time). Off for tests that need the map as drawn.</param>
    public static RunResult RunOnce(string mapText, EscapePlan plan, bool withNpcs, ulong seed, long maxTicks,
        WeatherSettings? weather = null, bool vary = true)
    {
        var data = MapFile.Parse(mapText);
        if (!withNpcs)
            data = data with { Markers = data.Markers.Where(m => m.Kind != MarkerKind.Npc).ToList() };

        var variation = vary ? VariationFor(seed) : new Variation(0, 0, Direction.South, 0);
        data = data with
        {
            Markers = data.Markers.Select(m => m.Kind == MarkerKind.Rex
                ? m with { Position = new TilePos(m.Position.X + variation.RexDx, m.Position.Y + variation.RexDy) }
                : m).ToList(),
        };

        var bot = new EscapeBot(plan, variation.ReactionTicks);
        var sim = ScenarioBuilder.Build(data, seed, bot, variation.RexFacing);
        if (weather is not null)
            sim.SetWeather(weather);

        var player = sim.Actors.First(a => a.Controller == bot);
        var rex = sim.Actors.FirstOrDefault(a => a.Species == Species.Rex);
        var brain = rex?.Controller as CreatureBrain;
        var npcs = sim.Actors.Where(a => a.Controller is HumanBrain).ToList();
        long? breach = null, hunted = null;
        long lastCharge = long.MinValue / 2;
        bool caughtByCharge = false;
        sim.CoverBroken += (_, type, by) =>
        {
            if (by is not null && by == rex && type is CoverType.Fence or CoverType.WeakFence)
                breach ??= sim.CurrentTick;
        };
        sim.ActorDied += (who, _) =>
        {
            if (who == player)
                caughtByCharge = sim.CurrentTick - lastCharge <= RunDownTicks;
        };

        var playerKey = ContactKey.ForActor(player.Id);
        while (sim.CurrentTick < maxTicks && !player.IsGone)
        {
            if (sim.Step() == StepResult.Idle)
                break;
            if (rex is { Charging: true })
                lastCharge = sim.CurrentTick;
            if (hunted is null && brain?.Focus is { Class: ContactClass.Prey } focus && focus.Key == playerKey)
                hunted = sim.CurrentTick;
        }

        RunOutcome outcome;
        long end;
        if (player.HasEscaped)
        {
            outcome = RunOutcome.Escaped;
            end = player.EscapedTick ?? sim.CurrentTick;
        }
        else if (player.IsDead)
        {
            outcome = RunOutcome.Died;
            end = player.Health.DiedTick;
        }
        else
        {
            end = Math.Min(sim.CurrentTick, maxTicks);
            bool safe = rex is null || brain is null || rex.IsDead
                || EscapeCheck.Evaluate(sim, rex, brain, player).Escaped;
            outcome = safe ? RunOutcome.SafeAtEnd : RunOutcome.HuntedAtEnd;
        }

        return new RunResult(plan, withNpcs, seed, outcome, end, player.EscapedVia?.Name,
            player.IsDead ? player.Health.DeathCause : null, breach, hunted,
            npcs.Count, npcs.Count(n => n.IsDead), npcs.Count(n => n.HasEscaped),
            bot.StartedTick, outcome == RunOutcome.Died && caughtByCharge, variation.ReactionTicks);
    }

    /// <summary>
    /// Every plan, with and without NPCs, seeds <paramref name="firstSeed"/> onwards. Runs in parallel;
    /// results come back in a fixed order (plan, NPCs on then off, seed) whatever order they finish in.
    /// </summary>
    /// <param name="progress">Called after each run with (runs done, runs in all); may be called from any thread.</param>
    public static IReadOnlyList<RunResult> RunAll(string mapText, IReadOnlyList<EscapePlan> plans, int runsPerOption,
        long maxTicks, WeatherSettings? weather = null, ulong firstSeed = 1, Action<int, int>? progress = null)
    {
        var jobs = new List<(EscapePlan Plan, bool Npcs, ulong Seed)>();
        foreach (var plan in plans)
        {
            foreach (bool npcs in new[] { true, false })
            {
                for (int i = 0; i < runsPerOption; i++)
                    jobs.Add((plan, npcs, firstSeed + (ulong)i));
            }
        }

        var results = new RunResult[jobs.Count];
        int done = 0;
        Parallel.For(0, jobs.Count, i =>
        {
            var (plan, npcs, seed) = jobs[i];
            results[i] = RunOnce(mapText, plan, npcs, seed, maxTicks, weather);
            progress?.Invoke(Interlocked.Increment(ref done), jobs.Count);
        });
        return results;
    }
}
