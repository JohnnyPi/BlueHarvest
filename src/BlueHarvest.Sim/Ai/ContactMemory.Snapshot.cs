using System.Text.Json.Nodes;
using BlueHarvest.Sim.World;
using static BlueHarvest.Sim.Snapshots.SnapshotJson;

namespace BlueHarvest.Sim.Ai;

public sealed partial class ContactMemory
{
    /// <summary>Everything remembered, oldest first, places in island metres (sounds by the tile they came from).</summary>
    internal JsonArray Save(Simulation sim) => new(_contacts.Select(c => (JsonNode?)SaveContact(sim, c)).ToArray());

    internal static JsonObject SaveContact(Simulation sim, Contact c)
    {
        var o = new JsonObject
        {
            ["key"] = SaveKey(sim, c.Key),
            ["class"] = Name(c.Class),
            ["estimate"] = Tile(sim, c.Estimate),
            ["z"] = c.EstimateZ,
            ["first"] = c.FirstTick,
            ["updated"] = c.LastUpdatedTick,
            ["moving"] = c.Moving,
            ["scream"] = c.LastScreamTick,
            ["searched"] = c.Searched,
            ["cue"] = c.Cue,
            ["confidence"] = c.ConfidenceAtUpdate,
            ["uncertainty"] = c.UncertaintyAtUpdate,
        };
        return o;
    }

    internal static Contact ReadContact(Simulation sim, JsonNode? node)
    {
        JsonObject o = Obj(node, "a remembered contact");
        return new Contact(ReadKey(sim, o["key"]), Val<long>(o, "first"))
        {
            Class = EnumOf<ContactClass>(o, "class"),
            Estimate = ReadTile(sim, o["estimate"], "a contact's estimate"),
            EstimateZ = Val<int>(o, "z"),
            LastUpdatedTick = Val<long>(o, "updated"),
            Moving = Val<bool>(o, "moving"),
            LastScreamTick = Val<long>(o, "scream"),
            Searched = Val<bool>(o, "searched"),
            Cue = Val<string>(o, "cue"),
            ConfidenceAtUpdate = Val<int>(o, "confidence"),
            UncertaintyAtUpdate = Val<int>(o, "uncertainty"),
        };
    }

    /// <summary>A contact's key: an actor or light by number, a sound by the tile it came from (island metres).</summary>
    internal static JsonObject SaveKey(Simulation sim, ContactKey key) => new()
    {
        ["source"] = Name(key.Source),
        ["id"] = key.Source == ContactSource.Sound
            ? Tile(sim, new TilePos(key.Id % sim.Map.Width, key.Id / sim.Map.Width))
            : key.Id,
    };

    internal static ContactKey ReadKey(Simulation sim, JsonNode? node)
    {
        JsonObject o = Obj(node, "a contact's key");
        ContactSource source = EnumOf<ContactSource>(o, "source");
        if (source != ContactSource.Sound)
            return new ContactKey(source, Val<int>(o, "id"));
        TilePos at = ReadTile(sim, o["id"], "a sound's tile");
        return new ContactKey(source, at.Y * sim.Map.Width + at.X);
    }

    internal static JsonObject? SaveKey(Simulation sim, ContactKey? key) => key is { } k ? SaveKey(sim, k) : null;

    internal static ContactKey? ReadOptKey(Simulation sim, JsonNode? node) => node is null ? null : ReadKey(sim, node);

    internal void Restore(JsonArray contacts, Simulation sim)
    {
        _contacts.Clear();
        foreach (JsonNode? node in contacts)
            _contacts.Add(ReadContact(sim, node));
    }
}
