using BlueHarvest.Sim.Perception;

namespace BlueHarvest.Sim.Actors;

/// <summary>Everything an actor's kind decides: how it moves, senses, how big, loud and strong it is.</summary>
/// <param name="BodyHeightDm">Height of the top of the body; what others need to see to spot it.</param>
/// <param name="FootstepLoudness">Noise of one step on grass, before cover modifiers.</param>
/// <param name="Body">Shape on the grid. Anything but a single tile turns in 45° steps.</param>
/// <param name="Force">Strength for smashing cover; compared with each cover's BreakStrength.</param>
public sealed record Species(
    string Name,
    MovementProfile Movement,
    SenseProfile Senses,
    int BodyHeightDm,
    int FootstepLoudness,
    BodyPlan Body,
    int Force)
{
    public static readonly Species Human = new("human", MovementProfile.Human, SenseProfile.Human,
        BodyHeightDm: 17, FootstepLoudness: 18, Body: BodyPlan.SingleTile, Force: 0);

    /// <summary>Single tile for now; a 1×3 body with free turning comes with its AI.</summary>
    public static readonly Species Raptor = new("raptor", MovementProfile.Raptor, SenseProfile.Raptor,
        BodyHeightDm: 15, FootstepLoudness: 12, Body: BodyPlan.SingleTile, Force: 5);

    /// <summary>Smashes sparse jungle and fences; stopped by dense jungle, walls and vehicles.</summary>
    public static readonly Species Rex = new("rex", MovementProfile.Rex, SenseProfile.Rex,
        BodyHeightDm: 45, FootstepLoudness: 45, Body: BodyPlan.Rex, Force: 80);
}
