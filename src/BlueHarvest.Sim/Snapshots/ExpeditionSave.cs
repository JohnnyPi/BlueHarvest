using System.Text.Json;
using System.Text.Json.Nodes;
using BlueHarvest.Sim.World.Generation;
using static BlueHarvest.Sim.Snapshots.SnapshotJson;

namespace BlueHarvest.Sim.Snapshots;

/// <summary>
/// A saved expedition, read back (<see cref="ExpeditionSnapshot.Read"/>): what it says about itself, so a caller can
/// check it, regenerate its island and put it back. Reading checks only that it's a whole snapshot; whether it can be
/// put back here is <see cref="ExpeditionSnapshot.Refusal"/>.
/// </summary>
public sealed class ExpeditionSave
{
    internal ExpeditionSave(JsonObject document)
    {
        Document = document;
        Format = Val<string>(document, "format");
        if (Format != ExpeditionSnapshot.Format)
        {
            // another format: only say what it is; nothing else in it can be relied on
            GameVersion = "";
            Fingerprint = "";
            Packs = [];
            Island = new IslandParams();
            Launch = new IslandStart(0, 0, 0);
            return;
        }
        GameVersion = Val<string>(document, "game");
        Generator = Val<int>(document, "generator");
        Fingerprint = Val<string>(document, "fingerprint");
        Packs = Arr(document, "packs")
            .Select(p => Obj(p, "a pack"))
            .Select(p => (Val<string>(p, "id"), Val<string>(p, "version")))
            .ToList();
        JsonObject island = Obj(document, "island");
        try
        {
            Island = Obj(island, "params").Deserialize<IslandParams>() ?? throw Bad("the island's parameters are empty");
        }
        catch (JsonException e)
        {
            throw Bad($"the island's parameters don't read ({e.Message})");
        }
        JsonObject launch = Obj(island, "launch");
        Launch = new IslandStart(Val<double>(launch, "x"), Val<double>(launch, "y"), Val<ulong>(launch, "runSeed"),
            Val<int>(launch, "startHour"), Val<int>(launch, "chunksAcross"));
        Tick = Val<long>(Obj(document, "clock"), "tick");
    }

    internal JsonObject Document { get; }

    /// <summary>The snapshot format (<see cref="ExpeditionSnapshot.Format"/> for one this build writes).</summary>
    public string Format { get; }

    /// <summary>The game version that saved it (<see cref="SimInfo.Version"/>).</summary>
    public string GameVersion { get; }

    /// <summary>The island generator's version it was saved with (<see cref="IslandGen.Version"/>).</summary>
    public int Generator { get; }

    /// <summary>The content fingerprint it was saved with.</summary>
    public string Fingerprint { get; }

    /// <summary>The pack lock: each pack it was saved with, and its version, in load order.</summary>
    public IReadOnlyList<(string Id, string Version)> Packs { get; }

    /// <summary>The island it was played on: regenerate it from these to put the save back.</summary>
    public IslandParams Island { get; }

    /// <summary>How the expedition started.</summary>
    public IslandStart Launch { get; }

    /// <summary>The tick it was saved at.</summary>
    public long Tick { get; }
}
