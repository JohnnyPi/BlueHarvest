namespace BlueHarvest.Sim.World.Generation;

/// <summary>A seed that was generated and then rejected by the checks.</summary>
public sealed record SkippedSeed(uint Seed, IslandValidation Validation)
{
    /// <summary>For a status line: "skipped seed 81234: lowland 28% of dry land, need 30%".</summary>
    public string Describe() => $"skipped seed {Seed}: {Validation.FailureSummary}";
}

/// <summary>
/// The island a seed search settled on. Found is false when no seed passed: World is then the last seed
/// tried, still validated, so its failures can be shown.
/// </summary>
public sealed record SeedSearch(IslandWorld World, IReadOnlyList<SkippedSeed> Skipped, bool Found);

/// <summary>
/// Finding an island that passes the seed checks (step 24d): the front end's "new island" asks for one.
/// Only failures reject a seed (decision 4); warnings never do.
/// </summary>
public static class IslandSeeds
{
    /// <summary>How many seeds a new-island roll tries before it gives up and shows the last one.</summary>
    public const int MaxTries = 10;

    /// <summary>
    /// Generates the seeds in order until one passes <see cref="IslandWorld.Validate"/>, keeping every other
    /// setting. A single seed is generated and validated, and returned whether it passes or not.
    /// validate replaces the checks (for tests). progress is told what stage is starting, as for Generate.
    /// </summary>
    public static SeedSearch FirstPassing(IslandParams settings, IReadOnlyList<uint> seeds, Action<string>? progress = null,
        Func<IslandWorld, IslandValidation>? validate = null)
    {
        if (seeds.Count == 0) throw new ArgumentException("No seeds to try.", nameof(seeds));
        var skipped = new List<SkippedSeed>();
        for (int k = 0; ; k++)
        {
            IslandWorld world = IslandWorld.Generate(settings with { Seed = seeds[k] }, progress);
            progress?.Invoke("Checking the island");
            IslandValidation v = validate is null ? world.Validate() : validate(world);
            if (v.Passed || k == seeds.Count - 1) return new SeedSearch(world, skipped, v.Passed);
            skipped.Add(new SkippedSeed(seeds[k], v));
        }
    }
}
