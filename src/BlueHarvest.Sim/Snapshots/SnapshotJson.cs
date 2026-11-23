using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json.Nodes;
using BlueHarvest.Content;
using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.World;
using BlueHarvest.Sim.World.Generation;

namespace BlueHarvest.Sim.Snapshots;

/// <summary>
/// Reading and writing the snapshot's pieces as JSON: items with what's in them, the island's changed tiles, a
/// survivor's map. Every read checks what it reads and refuses a snapshot that doesn't hold together.
/// </summary>
internal static class SnapshotJson
{
    // ---------- reading, checked ----------

    public static JsonObject Obj(JsonObject o, string key) =>
        o[key] as JsonObject ?? throw Bad($"'{key}' is missing or isn't an object");

    public static JsonArray Arr(JsonObject o, string key) =>
        o[key] as JsonArray ?? throw Bad($"'{key}' is missing or isn't a list");

    public static JsonArray Arr(JsonNode? node, string what) =>
        node as JsonArray ?? throw Bad($"{what} isn't a list");

    public static JsonObject Obj(JsonNode? node, string what) =>
        node as JsonObject ?? throw Bad($"{what} isn't an object");

    public static T Val<T>(JsonObject o, string key) => Val<T>(o[key], $"'{key}'");

    public static T Val<T>(JsonNode? node, string what)
    {
        if (node is JsonValue v && v.TryGetValue(out T? value) && value is not null)
            return value;
        throw Bad($"{what} is missing or isn't a {typeof(T).Name}");
    }

    public static T Opt<T>(JsonObject o, string key, T fallback) => o[key] is null ? fallback : Val<T>(o, key);

    public static string? OptString(JsonObject o, string key) => o[key] is null ? null : Val<string>(o, key);

    public static TEnum EnumOf<TEnum>(JsonObject o, string key) where TEnum : struct, System.Enum =>
        EnumOf<TEnum>(o[key], $"'{key}'");

    public static TEnum EnumOf<TEnum>(JsonNode? node, string what) where TEnum : struct, System.Enum
    {
        string name = Val<string>(node, what);
        return System.Enum.TryParse(name, ignoreCase: false, out TEnum value) && System.Enum.IsDefined(value)
            ? value
            : throw Bad($"{what} is '{name}', which isn't a {typeof(TEnum).Name}");
    }

    public static string Name<TEnum>(TEnum value) where TEnum : struct, System.Enum =>
        System.Enum.GetName(value) ?? throw new InvalidOperationException($"{value} has no name to save.");

    public static JsonArray Pair(long x, long y) => new(x, y);

    public static (long X, long Y) ReadPair(JsonObject o, string key)
    {
        JsonArray a = Arr(o, key);
        if (a.Count != 2)
            throw Bad($"'{key}' isn't a pair");
        return (Val<long>(a[0], $"'{key}'[0]"), Val<long>(a[1], $"'{key}'[1]"));
    }

    public static SnapshotRefusedException Bad(string why) => new($"This save is damaged or not a snapshot: {why}.");

    // ---------- items ----------

    /// <summary>An item's durable ID in a save: <c>inst:&lt;n&gt;</c> (contracts §2).</summary>
    public static string InstanceId(int id) => string.Create(CultureInfo.InvariantCulture, $"inst:{id}");

    public static int ReadInstanceId(JsonNode? node, string what)
    {
        string text = Val<string>(node, what);
        return text.StartsWith("inst:", StringComparison.Ordinal)
            && int.TryParse(text.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out int id) && id > 0
            ? id
            : throw Bad($"{what} is '{text}', not an inst: ID");
    }

    public static JsonObject WriteItem(Item item)
    {
        var o = new JsonObject
        {
            ["id"] = InstanceId(item.Id),
            ["kind"] = item.Kind.Value,
            ["amount"] = item.Amount,
        };
        if (item.Tag is { } tag)
            o["tag"] = tag;
        if (item.Level != 0)
            o["level"] = item.Level;
        if (item.Locked)
            o["locked"] = true;
        if (item.IsOpen)
            o["open"] = true;
        if (item.Unsafe)
            o["unsafe"] = true;
        if (item.Lit)
            o["lit"] = true;
        if (item.LitUntilTick != 0)
            o["litUntil"] = item.LitUntilTick;
        if (item.Contents.Count > 0)
            o["contents"] = new JsonArray(item.Contents.Select(i => (JsonNode?)WriteItem(i)).ToArray());
        return o;
    }

    /// <summary>Reads an item and what's in it, each ID once only (<paramref name="seen"/>), every kind from the catalog.</summary>
    public static Item ReadItem(JsonNode? node, ItemCatalog catalog, HashSet<int> seen, int made)
    {
        JsonObject o = Obj(node, "an item");
        int id = ReadInstanceId(o["id"], "an item's 'id'");
        if (id > made)
            throw Bad($"item inst:{id} is past the {made} items made");
        if (!seen.Add(id))
            throw Bad($"item inst:{id} is saved twice");
        string kind = Val<string>(o, "kind");
        ItemDef def = (ContentId.TryParse(kind, out ContentId kindId) ? catalog.Find(kindId) : null)
            ?? throw new SnapshotRefusedException($"This save has a {kind}, which the content packs here don't have.");
        var item = new Item(id, def, Val<int>(o, "amount"))
        {
            Tag = OptString(o, "tag"),
            Level = Opt(o, "level", 0),
            Locked = Opt(o, "locked", false),
            IsOpen = Opt(o, "open", false),
            Unsafe = Opt(o, "unsafe", false),
            Lit = Opt(o, "lit", false),
            LitUntilTick = Opt(o, "litUntil", 0L),
        };
        if (o["contents"] is { } contents)
        {
            foreach (JsonNode? inner in Arr(contents, $"what inst:{id} holds"))
                item.Append(ReadItem(inner, catalog, seen, made));
        }
        return item;
    }

    // ---------- the island's changed tiles ----------

    public static JsonArray WriteChanges(ChunkChanges changes)
    {
        var chunks = new JsonArray();
        foreach ((int cx, int cy) in changes.Chunks)
        {
            var tiles = new JsonArray();
            foreach (TileChange t in changes.For(cx, cy))
                tiles.Add(new JsonArray((int)t.Index, Name(t.Cover), (int)t.RoofDm, Name(t.Door), (int)t.DeckDm, t.Ladder));
            chunks.Add(new JsonObject { ["chunk"] = Pair(cx, cy), ["tiles"] = tiles });
        }
        return chunks;
    }

    public static ChunkChanges ReadChanges(JsonArray chunks)
    {
        var changes = new ChunkChanges();
        foreach (JsonNode? node in chunks)
        {
            JsonObject c = Obj(node, "a changed chunk");
            (long cx, long cy) = ReadPair(c, "chunk");
            var tiles = new List<TileChange>();
            foreach (JsonNode? tileNode in Arr(c, "tiles"))
            {
                JsonArray t = Arr(tileNode, "a changed tile");
                if (t.Count != 6)
                    throw Bad("a changed tile doesn't have its six fields");
                int index = Val<int>(t[0], "a tile's index");
                if (index < 0 || index >= IslandChunker.ChunkSize * IslandChunker.ChunkSize)
                    throw Bad($"tile index {index} is outside a chunk");
                tiles.Add(new TileChange((ushort)index, EnumOf<CoverType>(t[1], "a tile's cover"), (short)Val<int>(t[2], "a tile's roof"),
                    EnumOf<DoorState>(t[3], "a tile's door"), (short)Val<int>(t[4], "a tile's deck"), Val<bool>(t[5], "a tile's ladder")));
            }
            if (changes.For((int)cx, (int)cy).Count > 0)
                throw Bad($"chunk {cx},{cy} is saved twice");
            changes.Set((int)cx, (int)cy, tiles);
        }
        return changes;
    }

    // ---------- a survivor's map ----------

    public static JsonObject WriteKnowledge(MapKnowledge k)
    {
        var seen = new byte[k.SeenWords.Count * 8];
        for (int w = 0; w < k.SeenWords.Count; w++)
            BinaryPrimitives.WriteUInt64LittleEndian(seen.AsSpan(w * 8, 8), k.SeenWords[w]);
        return new JsonObject
        {
            ["nx"] = k.Nx,
            ["ny"] = k.Ny,
            ["seen"] = Convert.ToBase64String(seen),
            ["features"] = new JsonArray(k.Features.Select(f => (JsonNode?)new JsonArray(Name(f.Kind), f.Id, Name(f.How), f.Tick)).ToArray()),
            ["sheets"] = new JsonArray(k.Sheets.Select(s => (JsonNode?)new JsonArray(s.Area, s.Sheet)).ToArray()),
            ["trail"] = WriteTrail(k.Trail),
            ["olderTrails"] = new JsonArray(k.OlderTrails.Select(t => (JsonNode?)WriteTrail(t)).ToArray()),
            ["sites"] = new JsonArray(k.Sites.Select(s => (JsonNode?)s).ToArray()),
            ["rooms"] = new JsonArray(k.Rooms.Select(r => (JsonNode?)r).ToArray()),
            ["mapsRead"] = new JsonArray(k.MapsRead.Select(m => (JsonNode?)InstanceId(m)).ToArray()),
            ["notes"] = new JsonArray(k.Notes.Select(n => (JsonNode?)new JsonObject
            {
                ["kind"] = Name(n.Kind),
                ["x"] = n.X,
                ["y"] = n.Y,
                ["text"] = n.Text,
                ["tick"] = n.Tick,
            }).ToArray()),
        };
    }

    private static JsonArray WriteTrail(IReadOnlyList<TrailStep> trail) =>
        new(trail.Select(s => (JsonNode?)new JsonArray(s.Cell, s.Tick)).ToArray());

    private static List<TrailStep> ReadTrail(JsonNode? node, string what)
    {
        var steps = new List<TrailStep>();
        foreach (JsonNode? s in Arr(node, what))
        {
            JsonArray step = Arr(s, $"a step of {what}");
            if (step.Count != 2)
                throw Bad($"a step of {what} isn't (cell, tick)");
            steps.Add(new TrailStep(Val<int>(step[0], "a trail cell"), Val<long>(step[1], "a trail tick")));
        }
        return steps;
    }

    public static MapKnowledge ReadKnowledge(JsonObject o, IslandWorld world)
    {
        int nx = Val<int>(o, "nx"), ny = Val<int>(o, "ny");
        if (nx != world.Shape.Nx || ny != world.Shape.Ny)
            throw Bad($"the saved map is {nx}×{ny} cells, and this island is {world.Shape.Nx}×{world.Shape.Ny}");
        var k = new MapKnowledge(nx, ny);
        byte[] seen;
        try
        {
            seen = Convert.FromBase64String(Val<string>(o, "seen"));
        }
        catch (FormatException)
        {
            throw Bad("the seen cells aren't base64");
        }
        if (seen.Length % 8 != 0)
            throw Bad("the seen cells aren't whole words");
        var words = new ulong[seen.Length / 8];
        for (int w = 0; w < words.Length; w++)
            words[w] = BinaryPrimitives.ReadUInt64LittleEndian(seen.AsSpan(w * 8, 8));
        try
        {
            k.RestoreSeen(words);
        }
        catch (FormatException e)
        {
            throw Bad(e.Message);
        }
        foreach (JsonNode? node in Arr(o, "features"))
        {
            JsonArray f = Arr(node, "a found feature");
            if (f.Count != 4)
                throw Bad("a found feature isn't (kind, id, how, tick)");
            k.Find(new FoundFeature(EnumOf<FeatureKind>(f[0], "a feature's kind"), Val<int>(f[1], "a feature's id"),
                EnumOf<FoundHow>(f[2], "how a feature was found"), Val<long>(f[3], "when a feature was found")));
        }
        foreach (JsonNode? node in Arr(o, "sheets"))
        {
            JsonArray s = Arr(node, "a charted sheet");
            if (s.Count != 2)
                throw Bad("a charted sheet isn't (area, sheet)");
            k.Chart(Val<int>(s[0], "a sheet's area"), Val<int>(s[1], "a sheet's number"));
        }
        foreach (TrailStep step in ReadTrail(o["trail"], "the trail"))
        {
            if (!k.Walk(step.Cell, step.Tick))
                throw Bad("the trail steps twice in one cell");
        }
        foreach (JsonNode? trail in Arr(o, "olderTrails"))
            k.RestoreOlderTrail(ReadTrail(trail, "an older trail"));
        foreach (JsonNode? s in Arr(o, "sites"))
            k.EnterSite(Val<int>(s, "a site"));
        foreach (JsonNode? r in Arr(o, "rooms"))
            k.EnterRoom(Val<int>(r, "a room"));
        foreach (JsonNode? m in Arr(o, "mapsRead"))
            k.MarkRead(ReadInstanceId(m, "a map read"));
        foreach (JsonNode? node in Arr(o, "notes"))
        {
            JsonObject n = Obj(node, "a map note");
            k.Note(new MapNote(EnumOf<MapNoteKind>(n, "kind"), Val<double>(n, "x"), Val<double>(n, "y"), Val<string>(n, "text"), Val<long>(n, "tick")));
        }
        return k;
    }
}
