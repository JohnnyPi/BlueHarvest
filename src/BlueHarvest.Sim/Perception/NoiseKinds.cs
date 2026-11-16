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
    public const string Sparks = "crackling sparks";
    public const string SnappingWire = "snapping wire";
    public const string Generator = "a running generator";
    public const string ElectricCrack = "an electric crack";
    public const string Splashing = "splashing";
    public const string Fire = "crackling flames";
    public const string TanksBursting = "fuel tanks bursting";
    public const string DoorSliding = "a door sliding";
    public const string DoorForced = "a door being forced";

    /// <summary>Someone hurt cries out. A creature that hears it knows it's prey.</summary>
    public const string Scream = "a scream";

    /// <summary>Picking things up, stowing them, searching a body.</summary>
    public const string Rummaging = "rummaging";

    /// <summary>Something put down.</summary>
    public const string Thump = "a soft thump";

    /// <summary>A locker, crate, drawer or fridge opening or shutting.</summary>
    public const string Opening = "a door or lid opening";

    /// <summary>A lock wrenched open with a crowbar.</summary>
    public const string Prying = "metal wrenched apart";

    /// <summary>A fire extinguisher's roar.</summary>
    public const string Extinguisher = "a fire extinguisher";

    /// <summary>Fuel splashed out of a can.</summary>
    public const string Pouring = "liquid splashing";

    /// <summary>Someone drinking from water or filling a bottle: slurping and gurgling.</summary>
    public const string Drinking = "water gurgling";

    /// <summary>A radio switched on: hiss and crackle.</summary>
    public const string RadioStatic = "radio static";
}
