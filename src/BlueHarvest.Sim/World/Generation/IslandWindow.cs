namespace BlueHarvest.Sim.World.Generation;

/// <summary>
/// Turns the island's 1 m tiles into the game's map tiles: cover types with rules, heights in 0.5 m
/// levels, roofs over building interiors, closed doors, the current in the rivers, and where the tide
/// and the rivers' floods can reach.
/// </summary>
public sealed class IslandTiles
{
    /// <summary>Water deeper than this (m) can't be waded.</summary>
    public const double WadeDepth = 1.2;

    /// <summary>The tide can cover sea-edge ground from this far below mean sea level (dm)…</summary>
    public const int TidalLowestDm = -(WaterLevels.WadeDepthDm + WaterLevels.SpringAmplitudeDm + WaterLevels.DailyAmplitudeDm);

    /// <summary>…to this far above it.</summary>
    public const int TidalHighestDm = WaterLevels.SpringAmplitudeDm + WaterLevels.DailyAmplitudeDm;

    /// <summary>A flood can reach river banks up to this far above the river's usual surface (dm).</summary>
    public const int FloodHighestDm = 40;

    /// <summary>Ground a flood can cover and leave as it was: land with nothing built on it.</summary>
    private static readonly HashSet<CoverType> FloodableCovers =
    [
        CoverType.Grass, CoverType.Scrub, CoverType.SparseJungle, CoverType.DenseJungle, CoverType.Mud, CoverType.TallGrass,
        CoverType.Ferns, CoverType.Reeds, CoverType.Sand, CoverType.BareRock,
    ];

    private readonly HashSet<int> _indoorLabels = [];
    private readonly IslandShape _shape;

    public IslandTiles(IslandWorld world)
    {
        _shape = world.Shape;
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
                // only open water runs: a bridge or a pier over the river has no current on its deck
                map.Flow[p] = type is CoverType.ShallowWater or CoverType.DeepWater
                    ? new WaterFlow(chunk.FlowX[k], chunk.FlowY[k])
                    : WaterFlow.None;
                map.Water[p] = WaterFor(chunk, k, cover, type);
                map.Damp[p] = DampAt(chunk.X0 + i + 0.5, chunk.Y0 + j + 0.5);
            }
        }
    }

    /// <summary>
    /// Where water can come and go on a tile: the sea's edge between the lowest low and the highest
    /// high water, a river's bed, and its banks up to <see cref="FloodHighestDm"/> above its surface.
    /// </summary>
    private WaterTile WaterFor(IslandChunk chunk, int k, Cover cover, CoverType type)
    {
        double ground = chunk.Height[k];
        if (cover is Cover.Shallow or Cover.Reef or Cover.Deep or Cover.Beach)
        {
            Direction? facing = ShoreFacing(chunk.X0 + k % chunk.Size + 0.5, chunk.Y0 + k / chunk.Size + 0.5);
            int dm = (int)Math.Round(ground * 10);
            bool tidal = cover != Cover.Deep && dm >= TidalLowestDm && dm <= TidalHighestDm
                && (cover != Cover.Beach || type == CoverType.Sand);
            if (!tidal)
                return facing is null ? WaterTile.None : new WaterTile(WaterKind.None, 0, type, -1, facing);
            return new WaterTile(WaterKind.Sea, (short)dm, cover == Cover.Reef ? CoverType.BareRock : CoverType.Sand, -1, facing);
        }
        int river = chunk.River[k];
        if (river < 0 || float.IsNaN(chunk.RiverLevel[k]))
            return WaterTile.None;
        int rel = (int)Math.Round((ground - chunk.RiverLevel[k]) * 10);
        if (cover == Cover.River)
            return new WaterTile(WaterKind.River, (short)Math.Min(-1, rel), CoverType.Mud, (short)river, null);
        if (!FloodableCovers.Contains(type) || rel > FloodHighestDm)
            return WaterTile.None;
        return new WaterTile(WaterKind.River, (short)Math.Max(1, rel), type, (short)river, null);
    }

    /// <summary>
    /// How damp the ground is by climate, 0–100, from the annual rain of its coarse cell: none below
    /// 500 mm a year, a third at 2500 mm, all but soaked from 6500 mm (cloud forest).
    /// </summary>
    private int DampAt(double x, double y)
    {
        IslandShape s = _shape;
        int cx = Math.Clamp((int)Math.Floor(x / s.Cell), 0, s.Nx - 1), cy = Math.Clamp((int)Math.Floor(y / s.Cell), 0, s.Ny - 1);
        return Math.Clamp((int)((s.Rain[cy * s.Nx + cx] - 500) / 60), 0, 100);
    }

    /// <summary>
    /// The way the nearest stretch of coast looks out to sea, from the coarse grid's coast normals
    /// within a cell and a half; null far from any coast.
    /// </summary>
    private Direction? ShoreFacing(double x, double y)
    {
        IslandShape s = _shape;
        double cell = s.Cell;
        int cx = (int)Math.Floor(x / cell), cy = (int)Math.Floor(y / cell);
        double best = double.MaxValue;
        float nx = 0, ny = 0;
        for (int yy = cy - 1; yy <= cy + 1; yy++)
        {
            for (int xx = cx - 1; xx <= cx + 1; xx++)
            {
                if (xx < 0 || yy < 0 || xx >= s.Nx || yy >= s.Ny) continue;
                int i = yy * s.Nx + xx;
                if (s.CoastNx[i] == 0 && s.CoastNy[i] == 0) continue;
                double ddx = (xx + 0.5) * cell - x, ddy = (yy + 0.5) * cell - y, d2 = ddx * ddx + ddy * ddy;
                if (d2 < best) { best = d2; nx = s.CoastNx[i]; ny = s.CoastNy[i]; }
            }
        }
        if (best == double.MaxValue) return null;
        // the nearest of the eight directions (the one the normal points along most)
        Direction facing = Direction.North;
        double most = double.MinValue;
        foreach (Direction d in Directions.All)
        {
            var (dx, dy) = d.Delta();
            double along = (nx * dx + ny * dy) / (dx != 0 && dy != 0 ? Math.Sqrt(2) : 1);
            if (along > most) { most = along; facing = d; }
        }
        return facing;
    }
}

/// <summary>
/// A square game map that follows the player across the island: the island streamed as chunks.
/// The map's tile (0, 0) is at (OriginX, OriginY) metres on the island. When the player nears an
/// edge, <see cref="Recenter"/> scrolls the map by whole chunks and builds the chunks that come into
/// view; what stays in view keeps any changes made to it (smashed fences, opened doors). Changes to
/// chunks that scroll out are kept in <see cref="Changes"/> and put back when they scroll in again.
/// </summary>
public sealed class IslandWindow
{
    private const int Chunk = IslandChunker.ChunkSize;

    private readonly IslandChunker _chunker;
    private readonly IslandTiles _tiles;

    /// <summary>The window's tiles exactly as the generator made them: what <see cref="Map"/> is compared with to find changes.</summary>
    private readonly WorldMap _pristine;

    /// <summary>Per map tile, row-major: the chunker's <see cref="Biome"/> and its own <see cref="Cover"/>. For looks; see <see cref="BiomeAt"/>.</summary>
    private readonly byte[] _biome, _ground;

    /// <param name="chunksAcross">Window size in 64 m chunks: 5 gives a 320 × 320 m map.</param>
    /// <param name="centerX">Where to centre the window, in island metres.</param>
    /// <param name="changes">What has been done to the island so far (a loaded game); nothing if left out.</param>
    public IslandWindow(IslandWorld world, IslandChunker chunker, double centerX, double centerY, int chunksAcross = 5, ChunkChanges? changes = null)
    {
        if (chunksAcross < 2) throw new ArgumentOutOfRangeException(nameof(chunksAcross), "A window needs at least 2 chunks a side.");
        World = world;
        _chunker = chunker;
        _tiles = new IslandTiles(world);
        ChunksAcross = chunksAcross;
        Map = new WorldMap(chunksAcross * Chunk, chunksAcross * Chunk);
        _pristine = new WorldMap(chunksAcross * Chunk, chunksAcross * Chunk);
        _biome = new byte[Size * Size];
        _ground = new byte[Size * Size];
        Changes = changes ?? new ChunkChanges();
        (int cx, int cy) = OriginChunkFor(centerX, centerY);
        OriginX = cx * Chunk; OriginY = cy * Chunk;
        for (int y = 0; y < chunksAcross; y++)
            for (int x = 0; x < chunksAcross; x++) Fill(x, y);
    }

    public IslandWorld World { get; }
    public WorldMap Map { get; }

    /// <summary>
    /// Changes to chunks that are outside the window now. A chunk's changes are taken out when it
    /// scrolls in (the map then holds them) and put back when it scrolls out. For everything changed
    /// on the island, the window included, use <see cref="AllChanges"/>.
    /// </summary>
    public ChunkChanges Changes { get; }
    public int ChunksAcross { get; }
    public int Size => ChunksAcross * Chunk;
    /// <summary>Island position of the map's first tile, in metres; always a multiple of 64.</summary>
    public int OriginX { get; private set; }
    public int OriginY { get; private set; }

    /// <summary>How close to the edge (tiles) the player may get before the window should move.</summary>
    public int Margin => Chunk;

    public TilePos ToLocal(double worldX, double worldY) => new((int)Math.Floor(worldX) - OriginX, (int)Math.Floor(worldY) - OriginY);

    /// <summary>
    /// The biome the generator gave a map tile (<see cref="Biome.Ocean"/> off the map). For the renderer's
    /// looks only: nothing in the simulation reads it, and it says how the ground grew, not what is on it now.
    /// </summary>
    public Biome BiomeAt(TilePos p) => Map.InBounds(p) ? (Biome)_biome[p.Y * Size + p.X] : Biome.Ocean;

    /// <summary>
    /// The generator's own cover for a map tile, finer than its <see cref="CoverType"/> (a reef, cloud
    /// forest, a ruin's wall, a runway); <see cref="Cover.Deep"/> off the map. As the generator made it:
    /// a tile that has burnt or broken since keeps its old ground here, so use it only as a hint about a
    /// tile whose cover still matches (<see cref="IslandTiles.ToGameCover"/>). For looks only.
    /// </summary>
    public Cover GroundAt(TilePos p) => Map.InBounds(p) ? (Cover)_ground[p.Y * Size + p.X] : Cover.Deep;

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
        int cx = OriginX / Chunk + chunkX, cy = OriginY / Chunk + chunkY;
        int left = chunkX * Chunk, top = chunkY * Chunk;
        IslandChunk chunk = _chunker.Build(cx, cy);
        _tiles.Write(chunk, _pristine, left, top);
        _tiles.Write(chunk, Map, left, top);
        for (int j = 0; j < Chunk; j++)
        {
            Array.Copy(chunk.Biome, j * Chunk, _biome, (top + j) * Size + left, Chunk);
            Array.Copy(chunk.Cover, j * Chunk, _ground, (top + j) * Size + left, Chunk);
        }
        // then whatever was done here before the chunk last scrolled out
        foreach (TileChange change in Changes.For(cx, cy))
        {
            var p = new TilePos(left + change.Index % Chunk, top + change.Index / Chunk);
            Map.Cover[p] = change.Cover;
            Map.Structures.SetRoof(p, change.RoofDm);
            Map.Structures.SetDoor(p, change.Door);
            Map.Structures.SetDeck(p, change.DeckDm);
            Map.Structures.SetLadder(p, change.Ladder);
        }
        Changes.Remove(cx, cy);
    }

    /// <summary>The tiles of one of the window's chunks that are no longer as the generator made them.</summary>
    private List<TileChange> ChangedTiles(int chunkX, int chunkY)
    {
        var changed = new List<TileChange>();
        int left = chunkX * Chunk, top = chunkY * Chunk;
        for (int j = 0; j < Chunk; j++)
        {
            for (int i = 0; i < Chunk; i++)
            {
                var p = new TilePos(left + i, top + j);
                // a vehicle is a thing standing on the ground, not a change to it; nor is the tide or a flood
                CoverType cover = Map.Cover[p] == CoverType.Vehicle || Map.Water[p].IsWaterState(Map.Cover[p]) ? _pristine.Cover[p] : Map.Cover[p];
                StructureMap now = Map.Structures, was = _pristine.Structures;
                if (cover == _pristine.Cover[p] && now.RoofDm(p) == was.RoofDm(p) && now.DoorAt(p) == was.DoorAt(p)
                    && now.DeckDm(p) == was.DeckDm(p) && now.HasLadder(p) == was.HasLadder(p)) continue;
                changed.Add(new TileChange((ushort)(j * Chunk + i), cover, (short)now.RoofDm(p), now.DoorAt(p), (short)now.DeckDm(p), now.HasLadder(p)));
            }
        }
        return changed;
    }

    /// <summary>
    /// Everything changed on the island so far: the changes kept for chunks outside the window, plus
    /// the changed tiles of the chunks in it now. A copy; this is what a saved game will write.
    /// </summary>
    public ChunkChanges AllChanges()
    {
        var all = new ChunkChanges();
        foreach ((int cx, int cy) in Changes.Chunks) all.Set(cx, cy, Changes.For(cx, cy));
        for (int y = 0; y < ChunksAcross; y++)
            for (int x = 0; x < ChunksAcross; x++)
                all.Set(OriginX / Chunk + x, OriginY / Chunk + y, ChangedTiles(x, y));
        return all;
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
        int sx = dx / Chunk, sy = dy / Chunk;
        // the chunks about to scroll out: keep what was done to them
        for (int y = 0; y < ChunksAcross; y++)
        {
            for (int x = 0; x < ChunksAcross; x++)
            {
                int newX = x - sx, newY = y - sy;
                if (newX >= 0 && newY >= 0 && newX < ChunksAcross && newY < ChunksAcross) continue;
                Changes.Set(OriginX / Chunk + x, OriginY / Chunk + y, ChangedTiles(x, y));
            }
        }
        Map.Scroll(dx, dy);
        _pristine.Scroll(dx, dy);
        GridScroll.Scroll(_biome, Size, Size, dx, dy);
        GridScroll.Scroll(_ground, Size, Size, dx, dy);
        OriginX += dx; OriginY += dy;
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
