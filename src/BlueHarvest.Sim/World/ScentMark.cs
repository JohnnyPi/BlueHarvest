using BlueHarvest.Sim.Perception;

namespace BlueHarvest.Sim.World;

/// <summary>
/// Scent left on a tile: body scent where someone walked or waited, or blood where someone bleeding
/// passed. It fades with time, faster in rain (see <see cref="Weather.Wash"/>).
/// </summary>
/// <param name="ActorId">Whose scent it is.</param>
/// <param name="Strength">How strong it was when laid.</param>
/// <param name="Blood">Blood rather than body scent: stronger and longer-lasting, and it tells a predator its prey is hurt.</param>
/// <param name="WashStamp">The weather's scent clock when it was laid.</param>
public readonly record struct ScentMark(TilePos Position, long Tick, int ActorId, int Strength, bool Blood, long WashStamp)
{
    /// <summary>Its life in dry weather, in ticks.</summary>
    public int LifeTicks => Blood ? ScentRules.BloodLifeTicks : ScentRules.BodyLifeTicks;

    /// <summary>How strong it is now, given the scent clock now: fading in a straight line to 0 at the end of its life.</summary>
    public int StrengthAt(long washNow)
    {
        long age = Math.Max(0, washNow - WashStamp) / 100;
        long life = LifeTicks;
        return age >= life ? 0 : (int)(Strength * (life - age) / life);
    }
}
