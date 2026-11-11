namespace BlueHarvest.Sim.World;

/// <summary>
/// A connected patch of deck tiles: a catwalk, a tower top, an upper floor. It stands on the support
/// posts beneath it; when fewer than half of them are left standing it collapses, and everyone on
/// it falls.
/// </summary>
public sealed class Platform
{
    private readonly List<TilePos> _tiles;

    internal Platform(int id, IEnumerable<TilePos> tiles, int initialSupports)
    {
        Id = id;
        _tiles = tiles.OrderBy(t => t.Y).ThenBy(t => t.X).ToList();
        InitialSupports = initialSupports;
    }

    public int Id { get; }

    /// <summary>Its deck tiles, top row first.</summary>
    public IReadOnlyList<TilePos> Tiles => _tiles;

    /// <summary>Support posts under it when the simulation started. 0 means it's built into something solid and can't fall.</summary>
    public int InitialSupports { get; }

    public bool Collapsed { get; internal set; }

    /// <summary>The map scrolled: moves the deck's tiles by (−dx, −dy) and forgets the ones now outside it.</summary>
    internal void Shift(int dx, int dy, WorldMap map)
    {
        for (int i = 0; i < _tiles.Count; i++) _tiles[i] = new TilePos(_tiles[i].X - dx, _tiles[i].Y - dy);
        _tiles.RemoveAll(t => !map.InBounds(t));
    }
}
