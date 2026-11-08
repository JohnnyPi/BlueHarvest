using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim.Actors;

/// <summary>An actor caught in a bite, and where.</summary>
public readonly record struct BiteHit(Actor Victim, TilePos At);

/// <summary>
/// What one bite did. The biter's AI reads it on its next turn: it felt what its jaws closed on,
/// so that counts as perceived.
/// </summary>
/// <param name="Investigative">A test bite on something not yet identified.</param>
/// <param name="Zone">The tiles the jaws reached.</param>
/// <param name="Hits">Actors caught. People inside a vehicle that isn't breached yet aren't.</param>
/// <param name="CoverBitten">Tiles of bitable cover (vehicles) the jaws closed on.</param>
/// <param name="VehiclesBitten">Vehicles the bite damaged (or would have, if already breached).</param>
public sealed record BiteResult(
    long Tick,
    bool Investigative,
    IReadOnlyList<TilePos> Zone,
    IReadOnlyList<BiteHit> Hits,
    IReadOnlyList<TilePos> CoverBitten,
    IReadOnlyList<Vehicle> VehiclesBitten);
