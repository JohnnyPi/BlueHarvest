using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Ai;
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
    int NpcsEscaped)
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
    /// <summary>Plays one run to an escape, a death, or the time limit.</summary>
    public static RunResult RunOnce(string mapText, EscapePlan plan, bool withNpcs, ulong seed, long maxTicks,
        WeatherSettings? weather = null)
    {
        var data = MapFile.Parse(mapText);
        if (!withNpcs)
            data = data with { Markers = data.Markers.Where(m => m.Kind != MarkerKind.Npc).ToList() };

        var bot = new EscapeBot(plan);
        var sim = ScenarioBuilder.Build(data, seed, bot);
        if (weather is not null)
            sim.SetWeather(weather);

        var player = sim.Actors.First(a => a.Controller == bot);
        var rex = sim.Actors.FirstOrDefault(a => a.Species == Species.Rex);
        var brain = rex?.Controller as CreatureBrain;
        var npcs = sim.Actors.Where(a => a.Controller is HumanBrain).ToList();
        long? breach = null, hunted = null;
        sim.CoverBroken += (_, type, by) =>
        {
            if (by is not null && by == rex && type is CoverType.Fence or CoverType.WeakFence)
                breach ??= sim.CurrentTick;
        };

        var playerKey = ContactKey.ForActor(player.Id);
        while (sim.CurrentTick < maxTicks && !player.IsGone)
        {
            if (sim.Step() == StepResult.Idle)
                break;
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
            npcs.Count, npcs.Count(n => n.IsDead), npcs.Count(n => n.HasEscaped));
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
