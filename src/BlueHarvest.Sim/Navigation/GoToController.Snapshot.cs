using System.Text.Json.Nodes;
using BlueHarvest.Sim.World;
using static BlueHarvest.Sim.Snapshots.SnapshotJson;

namespace BlueHarvest.Sim.Navigation;

public sealed partial class GoToController
{
    /// <summary>The planned path and where it's got to, for a snapshot (the goal belongs to whoever made this).</summary>
    internal JsonObject Save(Simulation sim) => new()
    {
        ["tolerance"] = _tolerance,
        ["path"] = new JsonArray(_path.Select(d => (JsonNode?)Name(d)).ToArray()),
        ["next"] = _next,
        ["steps"] = _stepsSincePlan,
        ["backoff"] = _backoff,
        ["planned"] = OptTile(sim, PlannedGoal),
    };

    /// <summary>Puts a saved plan back into a controller made the same way.</summary>
    internal void Restore(JsonObject o, Simulation sim)
    {
        if (Val<int>(o, "tolerance") != _tolerance)
            throw Bad("a saved path was planned with another tolerance");
        _path = Arr(o, "path").Select(d => EnumOf<Direction>(d, "a step of a path")).ToList();
        _next = Val<int>(o, "next");
        if (_next < 0 || _next > _path.Count)
            throw Bad("a saved path's next step is outside it");
        _stepsSincePlan = Val<int>(o, "steps");
        _backoff = Val<int>(o, "backoff");
        PlannedGoal = ReadOptTile(sim, o["planned"], "a path's goal");
    }

    /// <summary>The tolerance a saved plan was made with, so the controller can be made again.</summary>
    internal static int SavedTolerance(JsonObject o) => Val<int>(o, "tolerance");
}
