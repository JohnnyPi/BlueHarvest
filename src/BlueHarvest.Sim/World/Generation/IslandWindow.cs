namespace BlueHarvest.Sim.World.Generation;

/// <summary>
/// Turns the island's 1 m tiles into the game's map tiles: cover types with rules, heights in 0.5 m
/// levels, roofs over building interiors and closed doors.
/// </summary>
public sealed class IslandTiles
{
    /// <summary>Water deeper than this (m) can't be waded.</summary>
    public const double WadeDepth = 1.2;

    private readonly HashSet<int> _indoorLabels = [];

    public IslandTiles(IslandWorld world)
    {
        // a tile is indoors when its label is a building's or one of its rooms'
        foreach (Prim q in world.Works.Prims.InOrder())
        {
            if (q.Kind != PrimKind.Building) continue;
            _indoorLabels.Add(q.Label);
            foreach (Room r in q.Rooms!) _indoorLabels.Add(r.Label);
        }
    }

    /// <summary>The game cover for an island cover. Water depends on how deep it is.</summary>
    public static CoverType ToGameCover(Cover cover, double depth) => cover switch
    {
        Cover.Deep => CoverType.DeepWater,
        Cover.Shallow or Cover.Reef => depth > WadeDepth ? CoverType.DeepWater : CoverType.ShallowWater,
        Cover.River or Cover.Lake => depth > WadeDepth ? CoverType.DeepWater : CoverType.ShallowWater,
        Cover.Beach => CoverType.Sand,
        Cover.Grass => CoverType.Grass,
        Cover.Scrub => CoverType.Scrub,
        Cover.Sparse => CoverType.SparseJungle,
        Cover.Dense or Cover.Cloud => CoverType.DenseJungle,
        Cover.Rock => CoverType.BareRock,
        Cover.Lava => CoverType.Lava,
        Cover.Mud or Cover.Moat => CoverType.Mud,
        Cover.TallGrass => CoverType.TallGrass,
        Cover.Fern => CoverType.Ferns,
        Cover.Reeds => CoverType.Reeds,
        Cover.Boulder => CoverType.Rock,
        Cover.Road or Cover.Bridge or Cover.Runway or Cover.Rail => CoverType.Track,
        Cover.Yard or Cover.Floor or Cover.Helipad or Cover.Pier or Cover.Elevator or Cover.Cave => CoverType.Floor,
        Cover.Wall or Cover.BigWall or Cover.Ruin => CoverType.Wall,
        Cover.Door => CoverType.Door,
        Cover.Fence => CoverType.Fence,
        Cover.Pylon or Cover.Mast => CoverType.Support,
        Cover.Rubble or Cover.Wreck => CoverType.Rubble,
        Cover.Rack or Cover.Console or Cover.Furniture or Cover.Tank => CoverType.Equipment,
        _ => CoverType.Grass,
    };

    /// <summary>Height in decimetres on the game's 0.5 m grid; never below sea level.</summary>
    public static short ToHeightDm(double metres) => (short)Math.Clamp((int)Math.Floor(metres * 2 + 0.5) * HeightMap.DecimetersPerLevel, 0, short.MaxValue);

    /// <summary>Writes a chunk into a game map with the chunk's first tile at (left, top). Tiles outside the map are skipped.</summary>
    public void Write(IslandChunk chunk, WorldMap map, int left, int top)
    {
        for (int j = 0; j < chunk.Size; j++)
        {
            for (int i = 0; i < chunk.Size; i++)
            {
                var p = new TilePos(left + i, top + j);
                if (!map.InBounds(p)) continue;
                int k = j * chunk.Size + i;
                var cover = (Cover)chunk.Cover[k];
                float water = chunk.Water[k];
                bool wet = !float.IsNaN(water);
                double depth = wet ? water - chunk.Height[k] : 0;
                CoverType type = ToGameCover(cover, depth);
                // water stands at its surface; a bridge is a deck a little above it
                double surface = wet && type is CoverType.ShallowWater or CoverType.DeepWater ? water
                    : wet && cover == Cover.Bridge ? water + 1 : chunk.Height[k];
                map.Heights[p] = ToHeightDm(surface);
                map.Cover[p] = type;
                bool indoors = chunk.Label[k] >= 0 && _indoorLabels.Contains(chunk.Label[k])
                    && type is CoverType.Floor or CoverType.Wall or CoverType.Door or CoverType.Equipment;
                map.Structures.SetRoof(p, indoors ? StructureMap.DefaultRoofDm : 0);
                map.Structures.SetDoor(p, type == CoverType.Door ? DoorState.Closed : DoorState.Open);
                map.Structures.SetDeck(p, 0);
                map.Structures.SetLadder(p, false);
            }
        }
    }
}

/// <summary>
/// A square game map that follows the player across the island: the island streamed as chunks.
/// The map's tile (0, 0) is at (OriginX, OriginY) metres on the island. When the player nears an
/// edge, <see cref="Recenter"/> scrolls the map by whole chunks and builds the chunks that come into
/// view; what stays in view keeps any changes made to it (smashed fences, opened doors).
/// </summary>
public sealed class IslandWindow
{
    private const int Chunk = IslandChunker.ChunkSize;

    private readonly IslandChunker _chunker;
    private readonly IslandTiles _tiles;

    /// <param name="chunksAcross">Window size in 64 m chunks: 5 gives a 320 × 320 m map.</param>
    /// <param name="centerX">Where to centre the window, in island metres.</param>
    public IslandWindow(IslandWorld world, IslandChunker chunker, double centerX, double centerY, int chunksAcross = 5)
    {
        if (chunksAcross < 2) throw new ArgumentOutOfRangeException(nameof(chunksAcross), "A window needs at least 2 chunks a side.");
        World = world;
        _chunker = chunker;
        _tiles = new IslandTiles(world);
        ChunksAcross = chunksAcross;
        Map = new WorldMap(chunksAcross * Chunk, chunksAcross * Chunk);
        (int cx, int cy) = OriginChunkFor(centerX, centerY);
        OriginX = cx * Chunk; OriginY = cy * Chunk;
        for (int y = 0; y < chunksAcross; y++)
            for (int x = 0; x < chunksAcross; x++) Fill(x, y);
    }

    public IslandWorld World { get; }
    public WorldMap Map { get; }
    public int ChunksAcross { get; }
    public int Size => ChunksAcross * Chunk;
    /// <summary>Island position of the map's first tile, in metres; always a multiple of 64.</summary>
    public int OriginX { get; private set; }
    public int OriginY { get; private set; }

    /// <summary>How close to the edge (tiles) the player may get before the window should move.</summary>
    public int Margin => Chunk;

    public TilePos ToLocal(double worldX, double worldY) => new((int)Math.Floor(worldX) - OriginX, (int)Math.Floor(worldY) - OriginY);

    /// <summary>The centre of a tile, in island metres.</summary>
    public (double X, double Y) ToWorld(TilePos p) => (OriginX + p.X + 0.5, OriginY + p.Y + 0.5);

    private (int X, int Y) OriginChunkFor(double worldX, double worldY)
    {
        // put the chunk holding the point as near the middle as a whole number of chunks allows
        int cx = (int)Math.Floor(worldX / Chunk), cy = (int)Math.Floor(worldY / Chunk);
        return (cx - (ChunksAcross - 1) / 2, cy - (ChunksAcross - 1) / 2);
    }

    private void Fill(int chunkX, int chunkY)
    {
        IslandChunk chunk = _chunker.Build(OriginX / Chunk + chunkX, OriginY / Chunk + chunkY);
        _tiles.Write(chunk, Map, chunkX * Chunk, chunkY * Chunk);
    }

    /// <summary>Whether a position is within <see cref="Margin"/> of the window's edge.</summary>
    public bool NearEdge(TilePos p) => p.X < Margin || p.Y < Margin || p.X >= Size - Margin || p.Y >= Size - Margin;

    /// <summary>
    /// Moves the window so that this tile is back in the middle, by whole chunks. Returns how far the
    /// map scrolled, in tiles: whatever was at local (x, y) is now at (x − Dx, y − Dy). (0, 0) if the
    /// window did not need to move. Everything that holds positions on this map must shift by the same amount.
    /// </summary>
    public (int Dx, int Dy) Recenter(TilePos around)
    {
        (double wx, double wy) = ToWorld(around);
        (int cx, int cy) = OriginChunkFor(wx, wy);
        int dx = cx * Chunk - OriginX, dy = cy * Chunk - OriginY;
        if (dx == 0 && dy == 0) return (0, 0);
        Map.Scroll(dx, dy);
        OriginX += dx; OriginY += dy;
        int sx = dx / Chunk, sy = dy / Chunk;
        for (int y = 0; y < ChunksAcross; y++)
        {
            for (int x = 0; x < ChunksAcross; x++)
            {
                // a chunk is new if the tiles now here were outside the old window
                int oldX = x + sx, oldY = y + sy;
                if (oldX < 0 || oldY < 0 || oldX >= ChunksAcross || oldY >= ChunksAcross) Fill(x, y);
            }
        }
        return (dx, dy);
    }

    /// <summary>
    /// Keeps a simulation running on this window's map centred on an actor (the player): when they come
    /// within <see cref="Margin"/> of the edge, scrolls the map, builds the new chunks and shifts
    /// everything in the simulation by the same amount. Call it after each of the player's turns.
    /// Returns whether the window moved.
    /// </summary>
    public bool Follow(Simulation sim, Actors.Actor actor)
    {
        if (!ReferenceEquals(sim.Map, Map)) throw new ArgumentException("The simulation is not running on this window's map.", nameof(sim));
        if (!NearEdge(actor.Position)) return false;
        (int dx, int dy) = Recenter(actor.Position);
        if (dx == 0 && dy == 0) return false;
        sim.ShiftWindow(dx, dy);
        return true;
    }

    /// <summary>The window as a stand-alone map with the player at a tile: what the .bhmap export writes.</summary>
    public MapData ToMapData(TilePos player, string playerName = "Survivor") =>
        new(Map, [new MapMarker(MarkerKind.Player, player, playerName)]);

    /// <summary>
    /// A .bhmap of the island around a point: a square of <paramref name="chunksAcross"/> chunks with the
    /// player standing at the nearest tile a person can stand on.
    /// </summary>
    public static string ExportBhmap(IslandWorld world, IslandChunker chunker, double centerX, double centerY, int chunksAcross = 5)
    {
        var window = new IslandWindow(world, chunker, centerX, centerY, chunksAcross);
        return MapFile.Serialize(window.ToMapData(window.NearestStandable(window.ToLocal(centerX, centerY))));
    }

    /// <summary>The nearest tile to a point that isn't blocked (not deep water, a wall or a fence); the point itself if none is.</summary>
    public TilePos NearestStandable(TilePos from)
    {
        var start = new TilePos(Math.Clamp(from.X, 0, Size - 1), Math.Clamp(from.Y, 0, Size - 1));
        for (int r = 0; r < Size; r++)
        {
            for (int y = start.Y - r; y <= start.Y + r; y++)
            {
                for (int x = start.X - r; x <= start.X + r; x++)
                {
                    if (Math.Max(Math.Abs(x - start.X), Math.Abs(y - start.Y)) != r) continue;
                    var p = new TilePos(x, y);
                    if (Map.InBounds(p) && !Map.CoverAt(p).BlocksMovement) return p;
                }
            }
        }
        return start;
    }
}
