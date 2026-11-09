using BlueHarvest.Sim.Perception;

namespace BlueHarvest.Sim.Actors;

/// <summary>Everything an actor's kind decides: how it moves, senses, how big, loud and strong it is.</summary>
/// <param name="BodyHeightDm">Height of the top of the body; what others need to see to spot it.</param>
/// <param name="FootstepLoudness">Noise of one step on grass, before cover modifiers.</param>
/// <param name="Body">Shape on the grid. Anything but a single tile turns in 45° steps.</param>
/// <param name="Force">Strength for smashing cover; compared with each cover's BreakStrength.</param>
/// <param name="ScreamLoudness">How loud it cries out when bitten or struck; 0 if it doesn't.</param>
/// <param name="ReachUpDm">How far above the ground it stands on its jaws (or hands) reach, in decimeters.</param>
/// <param name="ReachDownDm">How far below the ground it stands on it can reach.</param>
/// <param name="BiteDamage">Integrity a bite takes off a vehicle.</param>
/// <param name="OpensDoors">Can open and close doors.</param>
/// <param name="ClimbsLadders">Can climb ladders.</param>
/// <param name="BiteWounds">Red, bleeding wounds its bite deals to a victim (each on a different section).</param>
/// <param name="ScentStrength">How strongly its body scent marks the ground where it walks or waits; blood is stronger.</param>
/// <param name="VibrationStrength">How hard one of its steps shakes ordinary ground (mass and gait).</param>
public sealed record Species(
    string Name,
    MovementProfile Movement,
    SenseProfile Senses,
    int BodyHeightDm,
    int FootstepLoudness,
    BodyPlan Body,
    int Force,
    int ScreamLoudness = 0,
    int ReachUpDm = 20,
    int ReachDownDm = 10,
    int BiteDamage = 0,
    bool OpensDoors = false,
    bool ClimbsLadders = false,
    int BiteWounds = 0,
    int ScentStrength = 0,
    int VibrationStrength = 0)
{
    public static readonly Species Human = new("human", MovementProfile.Human, SenseProfile.Human,
        BodyHeightDm: 17, FootstepLoudness: 18, Body: BodyPlan.SingleTile, Force: 0, ScreamLoudness: 35,
        ReachUpDm: 20, ReachDownDm: 10, OpensDoors: true, ClimbsLadders: true, ScentStrength: 25, VibrationStrength: 10);

    /// <summary>Single tile for now; a 1×3 body with free turning comes with its AI.</summary>
    public static readonly Species Raptor = new("raptor", MovementProfile.Raptor, SenseProfile.Raptor,
        BodyHeightDm: 15, FootstepLoudness: 12, Body: BodyPlan.SingleTile, Force: 5,
        ReachUpDm: 20, ReachDownDm: 20, BiteDamage: 5, BiteWounds: 1, ScentStrength: 20, VibrationStrength: 8);

    /// <summary>
    /// Smashes sparse jungle and fences; stopped by dense jungle and walls; shoves vehicles.
    /// Reaches 5 m up: a 6 m ledge is safe, a 4 m one isn't.
    /// </summary>
    public static readonly Species Rex = new("rex", MovementProfile.Rex, SenseProfile.Rex,
        BodyHeightDm: 45, FootstepLoudness: 45, Body: BodyPlan.Rex, Force: 80,
        ReachUpDm: 50, ReachDownDm: 20, BiteDamage: 20, BiteWounds: 2, ScentStrength: 40, VibrationStrength: 60);
}
