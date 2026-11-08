using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Core;

namespace BlueHarvest.Sim.Scenarios;

/// <summary>
/// Who the named NPCs are: their traits and what they carry. Anyone not listed gets traits drawn
/// from their own random stream and nothing useful. Data, like Species.
/// </summary>
public static class NpcRoster
{
    public static (Traits Traits, Loadout Loadout) For(string name, Rng rng) => name switch
    {
        // A paleontologist: knows the Rex sees motion, keeps her head, stops to help.
        "Dr. Okafor" => (new Traits(Courage: 65, Selfishness: 15, PanicThreshold: 75, KnowsMotionVision: true), Loadout.Doctor),

        // A ranger: brave and practical, but not an expert on dinosaurs.
        "Marisol" => (new Traits(Courage: 70, Selfishness: 30, PanicThreshold: 60, KnowsMotionVision: false), Loadout.Ranger),

        // Out of his depth: frightened easily, looks after himself.
        "Pete" => (new Traits(Courage: 25, Selfishness: 75, PanicThreshold: 35, KnowsMotionVision: false), Loadout.ParkStaff),

        _ => (new Traits(
            Courage: rng.NextInt(20, 81),
            Selfishness: rng.NextInt(10, 81),
            PanicThreshold: rng.NextInt(30, 81),
            KnowsMotionVision: rng.Chance(1, 4)), Loadout.ParkStaff),
    };
}
