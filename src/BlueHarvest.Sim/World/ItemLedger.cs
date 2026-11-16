using BlueHarvest.Sim.Actors;

namespace BlueHarvest.Sim.World;

/// <summary>An item lying in the world, where it lies in island metres, and whether on a deck there.</summary>
public sealed record WorldItem(Item Item, long X, long Y, bool OnDeck);

/// <summary>
/// Every item of the run that lies in the world, for the whole island (not just the map window), and
/// the maker of every item: ids are handed out in the order items are made, from 1. Items lying off
/// the map stay where they are while the window scrolls. Carried items are on their carriers, and
/// items in containers are inside them; only what lies on the ground (or a deck) is listed here.
/// </summary>
public sealed class ItemLedger
{
    private readonly Dictionary<(long X, long Y), List<WorldItem>> _byPlace = new();
    private readonly SortedDictionary<int, WorldItem> _byId = new();
    private int _lastId;

    /// <summary>How many items have been made so far (lying, carried or anywhere).</summary>
    public int Made => _lastId;

    /// <summary>How many items lie in the world at the top level.</summary>
    public int Count => _byId.Count;

    /// <summary>Everything lying in the world, in id order.</summary>
    public IEnumerable<WorldItem> All => _byId.Values;

    /// <summary>
    /// Makes a new item: a stack of one, or at its kind's starting fill, unless an amount is given
    /// (clamped to the kind's maximum). It lies nowhere until it is laid, carried or put in something.
    /// </summary>
    public Item Create(ItemKind kind, int? amount = null, string? tag = null, int level = 0)
    {
        var def = ItemCatalog.Of(kind);
        int fill = Math.Clamp(amount ?? (def.Stacks ? 1 : def.StartAmount), def.Stacks ? 1 : 0, def.MaxAmount);
        return new Item(++_lastId, kind, fill) { Tag = tag, Level = level };
    }

    /// <summary>What lies at a spot (both levels), in the order it was put there.</summary>
    public IReadOnlyList<WorldItem> At(long x, long y) =>
        _byPlace.TryGetValue((x, y), out var list) ? list : Array.Empty<WorldItem>();

    /// <summary>The lying item with this id, or null (it may be carried, inside something, or gone).</summary>
    public WorldItem? Find(int id) => _byId.TryGetValue(id, out var lying) ? lying : null;

    /// <summary>Lays an item at a spot. It must not be lying anywhere already.</summary>
    public void Lay(Item item, long x, long y, bool onDeck)
    {
        if (_byId.ContainsKey(item.Id))
            throw new InvalidOperationException($"{item} is already lying in the world.");
        var lying = new WorldItem(item, x, y, onDeck);
        _byId.Add(item.Id, lying);
        if (!_byPlace.TryGetValue((x, y), out var list))
            _byPlace[(x, y)] = list = new List<WorldItem>();
        list.Add(lying);
    }

    /// <summary>Takes a lying item out of the world (picked up). False if it wasn't lying anywhere.</summary>
    public bool Lift(Item item)
    {
        if (!_byId.TryGetValue(item.Id, out var lying))
            return false;
        _byId.Remove(item.Id);
        var list = _byPlace[(lying.X, lying.Y)];
        list.Remove(lying);
        if (list.Count == 0)
            _byPlace.Remove((lying.X, lying.Y));
        return true;
    }
}
