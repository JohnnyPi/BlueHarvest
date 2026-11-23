using System.Text.Json;
using System.Text.Json.Nodes;
using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.Navigation;
using BlueHarvest.Sim.World;
using static BlueHarvest.Sim.Snapshots.SnapshotJson;

namespace BlueHarvest.Sim.Ai;

public sealed partial class CreatureBrain
{
    /// <summary>Everything the brain carries from one turn to the next, for a snapshot.</summary>
    internal JsonObject Save(Simulation sim) => new()
    {
        ["type"] = "creature",
        ["profile"] = JsonSerializer.SerializeToNode(Memory.Profile),
        ["rng"] = RngState(_wander.Stream),
        ["memory"] = Memory.Save(sim),
        ["path"] = _goTo?.Save(sim),
        ["exactPath"] = _goToExact?.Save(sim),
        ["waterPath"] = _goToWater?.Save(sim),
        ["goal"] = Tile(sim, _goal),
        ["waterGoal"] = Tile(sim, _waterGoal),
        ["water"] = OptTile(sim, _water),
        ["lastThink"] = _lastThinkTick,
        ["lastBiteSeen"] = _lastBiteSeenTick,
        ["focus"] = ContactMemory.SaveKey(sim, _focusKey),
        ["biteTarget"] = ContactMemory.SaveKey(sim, _biteTargetKey),
        ["stuck"] = _stuckTurns,
        ["activity"] = Name(Activity),
    };

    /// <summary>A brain put back as it was saved.</summary>
    internal static CreatureBrain Load(JsonObject o, Simulation sim)
    {
        var brain = new CreatureBrain(Settings<AiProfile>(o["profile"], "a creature's profile"), new Rng(1));
        ReadRng(brain._wander.Stream, o["rng"], "a creature's random stream");
        brain.Memory.Restore(Arr(o, "memory"), sim);
        brain._goTo = LoadPath(o["path"], sim, () => brain._goal);
        brain._goToExact = LoadPath(o["exactPath"], sim, () => brain._goal);
        brain._goToWater = LoadPath(o["waterPath"], sim, () => brain._waterGoal);
        brain._goal = ReadTile(sim, o["goal"], "a creature's goal");
        brain._waterGoal = ReadTile(sim, o["waterGoal"], "a creature's water goal");
        brain._water = ReadOptTile(sim, o["water"], "a creature's water");
        brain._lastThinkTick = Val<long>(o, "lastThink");
        brain._lastBiteSeenTick = Val<long>(o, "lastBiteSeen");
        brain._focusKey = ContactMemory.ReadOptKey(sim, o["focus"]);
        brain._biteTargetKey = ContactMemory.ReadOptKey(sim, o["biteTarget"]);
        brain._stuckTurns = Val<int>(o, "stuck");
        brain.Activity = EnumOf<Activity>(o, "activity");
        return brain;
    }

    private static GoToController? LoadPath(JsonNode? node, Simulation sim, Func<TilePos> goal)
    {
        if (node is null)
            return null;
        JsonObject o = Obj(node, "a creature's path");
        var path = new GoToController(goal, GoToController.SavedTolerance(o));
        path.Restore(o, sim);
        return path;
    }
}
