using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim.Ai;

/// <summary>Where a contact's cues come from.</summary>
public enum ContactSource : byte
{
    /// <summary>An actor, by id: seen, heard or bitten.</summary>
    Actor,

    /// <summary>A light source, by its index in Simulation.Lights.</summary>
    Light,

    /// <summary>A noise nobody made (no actor id), by packed tile index y × width + x.</summary>
    Sound,
}

/// <summary>Identifies a contact across turns.</summary>
public readonly record struct ContactKey(ContactSource Source, int Id)
{
    public static ContactKey ForActor(int actorId) => new(ContactSource.Actor, actorId);
}

/// <summary>What the creature thinks a contact is.</summary>
public enum ContactClass : byte
{
    /// <summary>Not identified yet: worth investigating, and test-biting when in reach.</summary>
    Unknown,

    /// <summary>Food. Hunted.</summary>
    Prey,

    /// <summary>Identified as uninteresting (a light that turned out to be a pole, a vehicle with nobody in it). Ignored.</summary>
    Object,
}

/// <summary>
/// One remembered thing: an estimated position, a confidence and a guess at what it is.
/// Confidence fades and the estimate spreads while no fresh cue arrives; both are computed from
/// the time of the last update, so nothing accumulates rounding.
/// </summary>
public sealed class Contact
{
    internal Contact(ContactKey key, long tick)
    {
        Key = key;
        FirstTick = tick;
        LastUpdatedTick = tick;
    }

    public ContactKey Key { get; }

    public ContactClass Class { get; internal set; }

    /// <summary>Best guess at where it is.</summary>
    public TilePos Estimate { get; internal set; }

    /// <summary>
    /// Absolute height, in decimeters, of what it stood on when last perceived: the ground for noises
    /// and lights, a deck for someone seen up on one.
    /// </summary>
    public int EstimateZ { get; internal set; }

    public long FirstTick { get; }

    public long LastUpdatedTick { get; internal set; }

    /// <summary>Whether it was moving when last seen.</summary>
    public bool Moving { get; internal set; }

    /// <summary>The creature went where it was (or bit there) and found nothing since.</summary>
    public bool Searched { get; internal set; }

    /// <summary>How it was last perceived, for debug display: "seen", "heard footsteps", "a light".</summary>
    public string Cue { get; internal set; } = "";

    internal int ConfidenceAtUpdate { get; set; }

    internal int UncertaintyAtUpdate { get; set; }

    /// <summary>0-100: how sure the creature is something is at the estimate.</summary>
    public int Confidence(long now, AiProfile profile)
    {
        long faded = Math.Max(0, now - LastUpdatedTick) * profile.ConfidenceDecayPerSecond / Ticks.PerSecond;
        return (int)Math.Max(0, ConfidenceAtUpdate - faded);
    }

    /// <summary>Radius in tiles the creature thinks it could be within, around the estimate.</summary>
    public int Uncertainty(long now, AiProfile profile) =>
        UncertaintyAtUpdate + (int)(Math.Max(0, now - LastUpdatedTick) / Math.Max(1, profile.UncertaintyGrowthTicks));
}
