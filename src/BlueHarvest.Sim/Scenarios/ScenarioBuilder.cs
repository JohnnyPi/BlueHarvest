using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Ai;
using BlueHarvest.Sim.Lighting;
using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim.Scenarios;

/// <summary>Turns map data into a running simulation: actors at their markers, lights, ambient light.</summary>
public static class ScenarioBuilder
{
    /// <summary>
    /// Spawns the player, NPCs and the Rex from markers and adds the lights. Pass a controller to
    /// drive the player; otherwise the player wanders randomly. The Rex gets its AI (a CreatureBrain);
    /// NPCs hold still until their own AI arrives in build step 9. Vehicles get their names and
    /// integrity from the map; headlights on them are mounted and move with them. People whose marker
    /// is on a deck tile start on the deck.
    /// </summary>
    public static Simulation Build(MapData data, ulong seed, IActorController? player = null)
    {
        var sim = new Simulation(data.Map, seed) { AmbientLightPercent = data.AmbientLight };
        foreach (var spec in data.VehicleSpecs)
        {
            var vehicle = sim.VehicleAt(spec.At)
                ?? throw new FormatException($"The vehicle entry at {spec.At} isn't on a vehicle.");
            sim.SetVehicleIntegrity(vehicle, spec.Integrity);
            if (!string.IsNullOrWhiteSpace(spec.Name))
                vehicle.Name = spec.Name.Trim();
        }
        bool hasPlayer = false;
        int npcCount = 0;

        for (int i = 0; i < data.Markers.Count; i++)
        {
            var marker = data.Markers[i];
            var ai = sim.Rng.Fork($"ai:{i}");
            switch (marker.Kind)
            {
                case MarkerKind.Player:
                    if (hasPlayer)
                        throw new FormatException($"The map has more than one player marker (second at {marker.Position}).");
                    hasPlayer = true;
                    sim.AddActor(marker.Name ?? "Survivor", '@', marker.Position, Species.Human,
                        player ?? new RandomWalker(ai), onDeck: OnDeck(data.Map, marker.Position));
                    break;

                case MarkerKind.Npc:
                    npcCount++;
                    sim.AddActor(marker.Name ?? $"Survivor {npcCount}", 'h', marker.Position, Species.Human,
                        new HoldStillController(), onDeck: OnDeck(data.Map, marker.Position));
                    break;

                case MarkerKind.Rex:
                    sim.AddActor(marker.Name ?? "Rex", 'T', marker.Position, Species.Rex, new CreatureBrain(AiProfile.Rex, ai));
                    break;

                case MarkerKind.Light:
                    sim.AddLight(LightSource.Emergency(marker.Position));
                    break;

                case MarkerKind.Headlight:
                    var facing = HeadlightFacing(data.Map, marker.Position);
                    sim.AddLight(facing is { } f
                        ? LightSource.Headlight(marker.Position, f)
                        : new LightSource(marker.Position, HeightDm: 10, RadiusTiles: 4, IntensityPercent: 50));
                    break;
            }
        }

        if (!hasPlayer)
            throw new FormatException("The map has no player marker.");
        return sim;
    }

    /// <summary>People placed on a deck tile start up on the deck.</summary>
    private static bool OnDeck(WorldMap map, TilePos p) => map.Structures.HasDeck(p);

    /// <summary>
    /// Which way a headlight on a vehicle's front tile points: away from the vehicle, along its length.
    /// Picks the cardinal direction with open ground ahead and the longest run of vehicle tiles behind.
    /// Null if the tile isn't part of a vehicle.
    /// </summary>
    public static Direction? HeadlightFacing(WorldMap map, TilePos position)
    {
        if (!map.InBounds(position) || map.Cover[position] != CoverType.Vehicle)
            return null;

        Direction? best = null;
        int bestRun = 0;
        foreach (var direction in Directions.Cardinal)
        {
            var ahead = position.Offset(direction);
            if (map.InBounds(ahead) && map.Cover[ahead] == CoverType.Vehicle)
                continue;

            int run = 0;
            var back = position.Offset(direction.Opposite());
            while (map.InBounds(back) && map.Cover[back] == CoverType.Vehicle)
            {
                run++;
                back = back.Offset(direction.Opposite());
            }

            if (run > bestRun)
            {
                best = direction;
                bestRun = run;
            }
        }
        return best;
    }
}
