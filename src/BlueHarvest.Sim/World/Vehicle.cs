namespace BlueHarvest.Sim.World;

/// <summary>How a map names a vehicle and sets its starting integrity: by any one of its tiles.</summary>
public sealed record VehicleSpec(TilePos At, int Integrity, string? Name = null);

/// <summary>
/// A vehicle: a connected patch of vehicle cover the simulation tracks as one object.
/// People standing on its tiles are inside it. Its body protects them from bites until its
/// integrity runs out (breached). A strong enough body can shove it a tile, occupants, headlights
/// and all.
/// </summary>
public sealed class Vehicle
{
    public const int MaxIntegrity = 100;

    private readonly List<TilePos> _tiles;

    internal Vehicle(int id, string name, IEnumerable<TilePos> tiles, IReadOnlyDictionary<TilePos, CoverType> under)
    {
        Id = id;
        Name = name;
        _tiles = tiles.OrderBy(t => t.Y).ThenBy(t => t.X).ToList();
        Under = new Dictionary<TilePos, CoverType>(under);
    }

    public int Id { get; }

    public string Name { get; internal set; }

    /// <summary>Tiles it covers, top row first.</summary>
    public IReadOnlyList<TilePos> Tiles => _tiles;

    /// <summary>0-100. At 0 it's breached: jaws reach whoever is inside.</summary>
    public int Integrity { get; internal set; } = MaxIntegrity;

    public bool Breached => Integrity <= 0;

    /// <summary>Indices into Simulation.Lights of lights mounted on it (headlights). They move with it.</summary>
    public IReadOnlyList<int> Lights => LightIndices;

    internal List<int> LightIndices { get; } = new();

    /// <summary>The ground cover under each of its tiles, put back when it moves off.</summary>
    internal Dictionary<TilePos, CoverType> Under { get; private set; }

    /// <summary>Its first tile (top row, leftmost); how maps refer to it.</summary>
    public TilePos Anchor => _tiles[0];

    internal void MoveBy(int dx, int dy, Dictionary<TilePos, CoverType> newUnder)
    {
        for (int i = 0; i < _tiles.Count; i++)
            _tiles[i] = new TilePos(_tiles[i].X + dx, _tiles[i].Y + dy);
        Under = newUnder;
    }
}
