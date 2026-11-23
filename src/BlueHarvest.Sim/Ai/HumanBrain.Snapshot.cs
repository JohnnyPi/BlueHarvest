using System.Text.Json;
using System.Text.Json.Nodes;
using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Core;
using static BlueHarvest.Sim.Snapshots.SnapshotJson;

namespace BlueHarvest.Sim.Ai;

public sealed partial class HumanBrain
{
    /// <summary>Everything the brain carries from one turn to the next, for a snapshot.</summary>
    internal JsonObject Save(Simulation sim)
    {
        var o = new JsonObject
        {
            ["type"] = "human",
            ["traits"] = JsonSerializer.SerializeToNode(Traits),
            ["profile"] = JsonSerializer.SerializeToNode(Memory.Profile),
            ["rng"] = RngState(_rng),
            ["memory"] = Memory.Save(sim),
            ["shelterPath"] = _goToShelter.Save(sim),
            ["personPath"] = _goToPerson.Save(sim),
            ["shelterGoal"] = Tile(sim, _shelterGoal),
            ["personGoal"] = Tile(sim, _personGoal),
            ["lastThink"] = _lastThinkTick,
            ["fearPeak"] = _fearPeak,
            ["fearPeakTick"] = _fearPeakTick,
            ["shelterPlanned"] = _shelterPlannedTick,
            ["shelter"] = OptTile(sim, _shelter),
            ["fear"] = Fear,
            ["panicking"] = Panicking,
            ["mood"] = Name(Mood),
            ["refusing"] = RefusingOrder,
        };
        if (Threat is { } threat)
        {
            // the threat is one of the remembered contacts, unless it has been forgotten since
            int inMemory = Memory.All.ToList().IndexOf(threat);
            o["threat"] = inMemory >= 0 ? inMemory : ContactMemory.SaveContact(sim, threat);
        }
        return o;
    }

    /// <summary>A brain put back as it was saved.</summary>
    internal static HumanBrain Load(JsonObject o, Simulation sim)
    {
        var brain = new HumanBrain(Settings<Traits>(o["traits"], "a person's traits"), Settings<AiProfile>(o["profile"], "a person's profile"),
            new Rng(1));
        ReadRng(brain._rng, o["rng"], "a person's random stream");
        brain.Memory.Restore(Arr(o, "memory"), sim);
        brain._goToShelter.Restore(Obj(o, "shelterPath"), sim);
        brain._goToPerson.Restore(Obj(o, "personPath"), sim);
        brain._shelterGoal = ReadTile(sim, o["shelterGoal"], "a person's shelter goal");
        brain._personGoal = ReadTile(sim, o["personGoal"], "a person's person goal");
        brain._lastThinkTick = Val<long>(o, "lastThink");
        brain._fearPeak = Val<int>(o, "fearPeak");
        brain._fearPeakTick = Val<long>(o, "fearPeakTick");
        brain._shelterPlannedTick = Val<long>(o, "shelterPlanned");
        brain._shelter = ReadOptTile(sim, o["shelter"], "a person's shelter");
        brain.Fear = Val<int>(o, "fear");
        brain.Panicking = Val<bool>(o, "panicking");
        brain.Mood = EnumOf<Mood>(o, "mood");
        brain.RefusingOrder = Val<bool>(o, "refusing");
        if (o["threat"] is JsonValue index)
        {
            int k = Val<int>(index, "a person's threat");
            if (k < 0 || k >= brain.Memory.All.Count)
                throw Bad("a person's threat isn't one they remember");
            brain.Threat = brain.Memory.All[k];
        }
        else if (o["threat"] is { } detached)
        {
            brain.Threat = ContactMemory.ReadContact(sim, detached);
        }
        return brain;
    }
}
