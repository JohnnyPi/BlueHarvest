using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BlueHarvest.Content;
using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.World;
using BlueHarvest.Sim.World.Generation;
using static BlueHarvest.Sim.Snapshots.SnapshotJson;

namespace BlueHarvest.Sim.Snapshots;

/// <summary>
/// The expedition snapshot (P3b): an island expedition saved as gzipped JSON, and put back exactly. Loading
/// regenerates the island from its parameters, then lays the snapshot over it (contracts §8). A save from another
/// format, generator version or set of packs is refused, never regenerated or substituted.
/// <para>
/// <b>What's in it:</b> the header and pack lock; the island (its parameters, how the run started, where the window
/// is, every changed tile); the session's counters (revision, last event ID, processed requests); the clock; the
/// weather; every item, lying or carried, with what's in it; and the simulation's own state
/// (<see cref="Simulation.CaptureState"/>): every actor with body, health, needs, kit, mind and island map, the turn
/// order, the random streams, lights, fire, power, water, vehicles, platforms and every trace on the ground. Put
/// back and played on, a game goes exactly as it would have without stopping (P3b-2).
/// </para>
/// </summary>
public static class ExpeditionSnapshot
{
    /// <summary>The format this build writes and reads.</summary>
    public const string Format = "blueharvest.expedition/1";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    // ---------- saving ----------

    /// <summary>The session's island expedition as a snapshot: gzipped JSON. Saving changes nothing.</summary>
    public static byte[] Save(WorldSession session)
    {
        byte[] json = Encoding.UTF8.GetBytes(Capture(session).ToJsonString());
        using var packed = new MemoryStream();
        using (var gzip = new GZipStream(packed, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(json);
        return packed.ToArray();
    }

    /// <summary>The snapshot as JSON text, indented: for reading by eye and for tests.</summary>
    public static string ToJson(WorldSession session) =>
        Capture(session).ToJsonString(Indented);

    internal static JsonObject Capture(WorldSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        Simulation sim = session.Surface;
        IslandWindow window = session.Island
            ?? throw new InvalidOperationException("Only island expeditions are saved: this session plays on a scenario map.");
        IslandStart launch = session.Launch!;
        if (sim.IslandOrigin != (window.OriginX, window.OriginY))
            throw new InvalidOperationException("The simulation and its window disagree on where the map is on the island.");

        var lying = new JsonArray();
        foreach (WorldItem w in sim.Items.All)
        {
            int order = 0;
            foreach (WorldItem there in sim.Items.At(w.X, w.Y))
            {
                if (ReferenceEquals(there, w))
                    break;
                order++;
            }
            lying.Add(new JsonObject
            {
                ["space"] = "surface", // until interiors arrive (P10)
                ["x"] = w.X,
                ["y"] = w.Y,
                ["deck"] = w.OnDeck,
                ["order"] = order,
                ["item"] = WriteItem(w.Item),
            });
        }

        (long washBase, long washSince) = sim.WeatherWash;
        WeatherSettings weather = sim.Weather.Settings;
        return new JsonObject
        {
            ["format"] = Format,
            ["game"] = SimInfo.Version,
            ["generator"] = IslandGen.Version,
            ["packs"] = new JsonArray(session.Content.Packs
                .Select(p => (JsonNode?)new JsonObject { ["id"] = p.Id, ["version"] = p.Version.ToString() }).ToArray()),
            ["fingerprint"] = session.Fingerprint,
            ["island"] = new JsonObject
            {
                ["params"] = JsonSerializer.SerializeToNode(window.World.Params),
                ["launch"] = new JsonObject
                {
                    ["x"] = launch.X,
                    ["y"] = launch.Y,
                    ["runSeed"] = launch.RunSeed,
                    ["startHour"] = launch.StartHour,
                    ["chunksAcross"] = launch.ChunksAcross,
                },
                ["origin"] = Pair(window.OriginX, window.OriginY),
                ["changes"] = WriteChanges(window.AllChanges()),
            },
            ["session"] = new JsonObject
            {
                ["revision"] = session.WorldRevision,
                ["lastEventId"] = session.LastEventId,
                ["processed"] = new JsonArray(session.ProcessedRequests.Select(r => (JsonNode?)r).ToArray()),
            },
            ["clock"] = new JsonObject
            {
                ["tick"] = sim.CurrentTick,
                ["clockStartMinute"] = sim.Clock is { } clock ? clock.StartMinute : null,
                ["windowOrigin"] = Pair(sim.WindowOrigin.X, sim.WindowOrigin.Y),
                ["windowShifts"] = sim.WindowShifts,
            },
            ["weather"] = new JsonObject
            {
                ["rain"] = weather.RainPercent,
                ["windFrom"] = Name(weather.WindFrom),
                ["windSpeed"] = weather.WindSpeed,
                ["fog"] = weather.FogPercent,
                ["storm"] = weather.Storm,
                ["cloud"] = weather.CloudPercent,
                ["washBase"] = washBase,
                ["washSince"] = washSince,
                ["follow"] = sim.FollowingWeatherSource,
            },
            ["items"] = new JsonObject
            {
                ["made"] = sim.Items.Made,
                ["lying"] = lying,
            },
            ["state"] = sim.CaptureState(),
        };
    }

    // ---------- reading ----------

    /// <summary>Reads a saved snapshot. Throws <see cref="SnapshotRefusedException"/> if it isn't one, or is damaged.</summary>
    public static ExpeditionSave Read(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        JsonNode? root;
        try
        {
            using var packed = new MemoryStream(data);
            using var gzip = new GZipStream(packed, CompressionMode.Decompress);
            using var json = new MemoryStream();
            gzip.CopyTo(json);
            root = JsonNode.Parse(json.ToArray());
        }
        catch (Exception e) when (e is InvalidDataException or JsonException)
        {
            throw new SnapshotRefusedException($"This isn't a saved expedition ({e.Message}).");
        }
        return new ExpeditionSave(Obj(root, "the snapshot"));
    }

    /// <summary>
    /// Why a save can't be put back with this content and this build, or null if it can: another snapshot format,
    /// another island generator, or other packs (each difference named).
    /// </summary>
    public static string? Refusal(ExpeditionSave save, ContentRegistry content)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(content);
        if (save.Format != Format)
            return $"This save is in format '{save.Format}'; this build reads '{Format}'.";
        if (save.Generator != IslandGen.Version)
            return $"This save's island was made by generator {save.Generator}; this build's is {IslandGen.Version}, so its island would differ.";
        if (save.Fingerprint == content.Fingerprint)
            return null;
        var here = content.Packs.Select(p => (p.Id, Version: p.Version.ToString())).ToList();
        var differences = new List<string>();
        foreach ((string id, string version) in save.Packs)
        {
            var match = here.FirstOrDefault(p => p.Id == id);
            if (match.Id is null)
                differences.Add($"{id} {version} isn't here");
            else if (match.Version != version)
                differences.Add($"{id} is {match.Version} here, {version} in the save");
        }
        foreach ((string id, string version) in here)
        {
            if (!save.Packs.Any(p => p.Id == id))
                differences.Add($"{id} {version} is here but wasn't in the save");
        }
        if (differences.Count == 0)
            differences.Add("the packs have the same versions, but their content differs");
        return $"This save was made with other content packs ({string.Join("; ", differences)}).";
    }

    // ---------- putting it back ----------

    /// <summary>
    /// Puts a save back in a new session: the island must be the one regenerated from <see cref="ExpeditionSave.Island"/>.
    /// The survivor is controlled by <paramref name="player"/>. Throws <see cref="SnapshotRefusedException"/> if the save
    /// can't be put back here (see <see cref="Refusal"/>) or doesn't hold together. Nothing put back raises an event.
    /// </summary>
    public static Simulation Restore(WorldSession session, ExpeditionSave save, IslandWorld world, IslandChunker chunker,
        IActorController player, FeatureIndex? features = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(save);
        if (session.HasSurface)
            throw new InvalidOperationException("A save is put back in a new session.");
        if (Refusal(save, session.Content) is { } why)
            throw new SnapshotRefusedException(why);
        if (save.Island != world.Params)
            throw new SnapshotRefusedException($"This save is for island {save.Island.Seed} at {save.Island.Scale}%; the island given is {world.Params.Seed} at {world.Params.Scale}%, or was made with other settings.");

        JsonObject doc = save.Document;
        IslandStart launch = save.Launch;
        using (session.Quietly())
        {
            // the window, where it was, with every change made to the island
            JsonObject island = Obj(doc, "island");
            (long ox, long oy) = ReadPair(island, "origin");
            ChunkChanges changes = ReadChanges(Arr(island, "changes"));
            int chunk = IslandChunker.ChunkSize;
            double centreX = ox + (launch.ChunksAcross - 1) / 2 * chunk + chunk / 2.0;
            double centreY = oy + (launch.ChunksAcross - 1) / 2 * chunk + chunk / 2.0;
            var window = new IslandWindow(world, chunker, centreX, centreY, launch.ChunksAcross, changes);
            if (window.OriginX != ox || window.OriginY != oy)
                throw Bad($"the window can't be put back at {ox}, {oy}");
            Simulation sim = IslandExpedition.Begin(session, window, launch);

            // the clock first: everything laid over the island reads it
            JsonObject clock = Obj(doc, "clock");
            (long wx, long wy) = ReadPair(clock, "windowOrigin");
            sim.RestoreClock(Val<long>(clock, "tick"), clock["clockStartMinute"] is null ? null : Val<long>(clock, "clockStartMinute"),
                new TilePos((int)wx, (int)wy), Val<int>(clock, "windowShifts"));

            // every item, lying where it lies, in the order it was put there
            JsonObject items = Obj(doc, "items");
            int made = Val<int>(items, "made");
            var ledger = new ItemLedger(session.Catalog);
            ledger.RestoreMade(made);
            var seen = new HashSet<int>();
            var lying = new List<(int Order, long X, long Y, bool Deck, Item Item)>();
            foreach (JsonNode? node in Arr(items, "lying"))
            {
                JsonObject l = Obj(node, "a lying item");
                if (OptString(l, "space") != "surface")
                    throw Bad($"an item lies in space '{OptString(l, "space")}', and this build has only the surface");
                lying.Add((Val<int>(l, "order"), Val<long>(l, "x"), Val<long>(l, "y"), Val<bool>(l, "deck"),
                    ReadItem(l["item"], session.Catalog, seen, made)));
            }
            foreach (var entry in lying.OrderBy(e => e.Order))
                ledger.Lay(entry.Item, entry.X, entry.Y, entry.Deck);
            IslandExpedition.LayOver(sim, world, window, launch.RunSeed, ledger);

            // the map as it was saved, undoing what laying the run over it changed (doors the power set, the tide)
            window.Reapply(ReadChanges(Arr(island, "changes")));

            // the weather as it was, scent clock and all
            JsonObject weather = Obj(doc, "weather");
            sim.RestoreWeather(new WeatherSettings(Val<int>(weather, "rain"), EnumOf<Direction>(weather, "windFrom"),
                    Val<int>(weather, "windSpeed"), Val<int>(weather, "fog"), Val<bool>(weather, "storm"), Val<int>(weather, "cloud")),
                Val<long>(weather, "washBase"), Val<long>(weather, "washSince"), Val<bool>(weather, "follow"));

            // everything else: the actors and their minds, the turns, the streams, lights, fire, power, water, traces
            sim.RestoreState(Obj(doc, "state"), player, world, features, seen, made);

            // and the session's counters last, over anything the rebuilding counted
            JsonObject counters = Obj(doc, "session");
            session.RestoreCounters(Val<ulong>(counters, "revision"), Val<ulong>(counters, "lastEventId"),
                Arr(counters, "processed").Select(p => Val<string>(p, "a processed request")).ToList());
            return sim;
        }
    }
}
