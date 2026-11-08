namespace BlueHarvest.Sim.Perception;

/// <summary>The kinds of noise the simulation makes. AI matches on these, so use the constants, not literals.</summary>
public static class NoiseKinds
{
    public const string Footsteps = "footsteps";
    public const string Bump = "bump";
    public const string Crashing = "crashing";
    public const string HeavyBlow = "a heavy blow";
    public const string Jaws = "jaws snapping";
    public const string Door = "a door";
    public const string MetalCrunch = "crunching metal";
    public const string MetalScrape = "scraping metal";
    public const string MetalTear = "tearing metal";
    public const string Climbing = "climbing";
    public const string Collapse = "a structure collapsing";
    public const string FirstAid = "rustling";
    public const string Flare = "a hissing flare";
    public const string Rope = "a creaking rope";
    public const string Scrambling = "scrambling";
    public const string Talking = "talking";

    /// <summary>Someone hurt cries out. A creature that hears it knows it's prey.</summary>
    public const string Scream = "a scream";
}
