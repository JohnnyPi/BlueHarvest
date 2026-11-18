using BlueHarvest.Sim.Core;

namespace BlueHarvest.Sim.World;

/// <summary>A wild feature the island map can show once it's found: ruins, wrecks and caves.</summary>
public enum FeatureKind : byte { Ruin, Wreck, Cave }

/// <summary>How a feature came to be known. Append only.</summary>
public enum FoundHow : byte
{
    /// <summary>Its island-map cell was seen (ruins, wrecks), or it came within sight (a cave's opening).</summary>
    Seen,
    /// <summary>Walked into: standing in a cave's mouth or on its skylight.</summary>
    Entered,
    /// <summary>Marked on a map that was read (step 25b-2).</summary>
    FromMap,
}

/// <param name="Id">The feature's id in <c>IslandWorks.Ruins</c>, <c>Wrecks</c> or <c>Caves</c>.</param>
/// <param name="Tick">When it was found.</param>
public readonly record struct FoundFeature(FeatureKind Kind, int Id, FoundHow How, long Tick);

/// <summary>Where a marker on the island map came from. Append only.</summary>
public enum MapNoteKind : byte { Memory, Document, Echo, Note }

/// <summary>A marker on the island map with its text, at a place in island metres.</summary>
public sealed record MapNote(MapNoteKind Kind, double X, double Y, string Text, long Tick);

/// <summary>One step of a trail: an island-map cell walked into, and when.</summary>
public readonly record struct TrailStep(int Cell, long Tick);

/// <summary>One fifth of a facility's area, as a park map charts it (step 25b-2).</summary>
public readonly record struct ChartedSheet(int Area, int Sheet);

/// <summary>
/// What one survivor knows of the island (step 25b-1): the island map they fill in as they explore.
/// Kept in island-map cells (the shape's grid, 128 m at the standard scale). Seen cells show what's in
/// them (terrain, rivers, roads, fences, buildings); ruins, wrecks and caves appear once found; power
/// lines and ley lines never do. Knowledge can be merged, so a later run that recovers an earlier
/// survivor's map gets everything they found (recovery itself comes with step 34).
/// </summary>
public sealed class MapKnowledge
{
    private readonly ulong[] _seen;
    private readonly SortedDictionary<(FeatureKind Kind, int Id), FoundFeature> _features = new();
    private readonly SortedSet<(int Area, int Sheet)> _sheets = new();
    private readonly List<TrailStep> _trail = new();
    private readonly List<IReadOnlyList<TrailStep>> _olderTrails = new();
    private readonly SortedSet<int> _sites = new();
    private readonly SortedSet<int> _rooms = new();
    private readonly List<MapNote> _notes = new();
    private readonly SortedSet<int> _mapsRead = new();

    public MapKnowledge(int nx, int ny)
    {
        if (nx <= 0 || ny <= 0)
            throw new ArgumentOutOfRangeException(nameof(nx), "The island map needs at least one cell.");
        Nx = nx;
        Ny = ny;
        _seen = new ulong[(nx * ny + 63) / 64];
    }

    /// <summary>The island map's size in cells.</summary>
    public int Nx { get; }
    public int Ny { get; }

    /// <summary>How many cells have been seen.</summary>
    public int SeenCount { get; private set; }

    public bool HasSeen(int cell) => cell >= 0 && cell < Nx * Ny && (_seen[cell >> 6] & (1UL << (cell & 63))) != 0;

    public bool HasSeen(int cx, int cy) => cx >= 0 && cy >= 0 && cx < Nx && cy < Ny && HasSeen(cy * Nx + cx);

    /// <summary>Marks a cell seen. True if it wasn't before.</summary>
    public bool See(int cell)
    {
        if (cell < 0 || cell >= Nx * Ny || HasSeen(cell))
            return false;
        _seen[cell >> 6] |= 1UL << (cell & 63);
        SeenCount++;
        return true;
    }

    /// <summary>Every seen cell, in order.</summary>
    public IEnumerable<int> SeenCells()
    {
        for (int w = 0; w < _seen.Length; w++)
        {
            ulong bits = _seen[w];
            while (bits != 0)
            {
                int b = System.Numerics.BitOperations.TrailingZeroCount(bits);
                yield return w * 64 + b;
                bits &= bits - 1;
            }
        }
    }

    /// <summary>Found ruins, wrecks and caves, by kind and id.</summary>
    public IReadOnlyCollection<FoundFeature> Features => _features.Values;

    public bool Knows(FeatureKind kind, int id) => _features.ContainsKey((kind, id));

    public FoundFeature? Feature(FeatureKind kind, int id) => _features.TryGetValue((kind, id), out FoundFeature f) ? f : null;

    /// <summary>Records a found feature. True if it's new; a feature already known keeps how it was first found.</summary>
    public bool Find(FoundFeature feature) => _features.TryAdd((feature.Kind, feature.Id), feature);

    /// <summary>Park-map sheets charted (filled in by step 25b-2).</summary>
    public IReadOnlyCollection<ChartedSheet> Sheets => _sheets.Select(s => new ChartedSheet(s.Area, s.Sheet)).ToList();

    public bool HasCharted(int area, int sheet) => _sheets.Contains((area, sheet));

    /// <summary>Charts a sheet. True if it wasn't charted before.</summary>
    public bool Chart(int area, int sheet) => _sheets.Add((area, sheet));

    /// <summary>The cells this survivor walked through, in order; a cell is listed again only after leaving it.</summary>
    public IReadOnlyList<TrailStep> Trail => _trail;

    /// <summary>Trails of earlier survivors whose maps were merged in, oldest first; drawn fainter than <see cref="Trail"/>.</summary>
    public IReadOnlyList<IReadOnlyList<TrailStep>> OlderTrails => _olderTrails;

    /// <summary>Notes being in a cell. True if it's a new step of the trail (not the cell of the last one).</summary>
    public bool Walk(int cell, long tick)
    {
        if (_trail.Count > 0 && _trail[^1].Cell == cell)
            return false;
        _trail.Add(new TrailStep(cell, tick));
        return true;
    }

    /// <summary>Sites entered, as indices into <c>IslandWorks.Sites</c>.</summary>
    public IReadOnlyCollection<int> Sites => _sites;

    /// <summary>Rooms entered, as room-index ids.</summary>
    public IReadOnlyCollection<int> Rooms => _rooms;

    public bool EnterSite(int site) => _sites.Add(site);

    public bool EnterRoom(int room) => _rooms.Add(room);

    /// <summary>
    /// Park maps (item ids) this survivor has read: each rolls once for something marked on it, on its
    /// first read. Item ids belong to one run, so these aren't merged into another survivor's map.
    /// </summary>
    public IReadOnlyCollection<int> MapsRead => _mapsRead;

    /// <summary>Notes a park map read. True on its first read.</summary>
    public bool MarkRead(int mapItemId) => _mapsRead.Add(mapItemId);

    /// <summary>Markers with text: memories, documents, echoes and the survivor's own notes. Echoes and memories add markers, never map areas.</summary>
    public IReadOnlyList<MapNote> Notes => _notes;

    /// <summary>Adds a marker. False if the same marker is already there.</summary>
    public bool Note(MapNote note)
    {
        if (_notes.Contains(note))
            return false;
        _notes.Add(note);
        return true;
    }

    /// <summary>
    /// Takes in everything another map knows: the union of seen cells, features, sheets, places and
    /// markers. The other survivor's trail (and any trails merged into theirs) are kept as older
    /// trails, never joined to this one. Maps read stay with their reader (item ids belong to one run).
    /// Merging the same map again changes nothing.
    /// </summary>
    public void Merge(MapKnowledge other)
    {
        if (other.Nx != Nx || other.Ny != Ny)
            throw new ArgumentException($"Can't merge a {other.Nx}×{other.Ny} map into a {Nx}×{Ny} one.", nameof(other));
        if (ReferenceEquals(other, this))
            return;
        for (int w = 0; w < _seen.Length; w++)
            _seen[w] |= other._seen[w];
        SeenCount = 0;
        foreach (ulong bits in _seen)
            SeenCount += System.Numerics.BitOperations.PopCount(bits);
        foreach (FoundFeature f in other._features.Values)
            _features.TryAdd((f.Kind, f.Id), f);
        _sheets.UnionWith(other._sheets);
        _sites.UnionWith(other._sites);
        _rooms.UnionWith(other._rooms);
        foreach (MapNote note in other._notes)
            Note(note);
        foreach (IReadOnlyList<TrailStep> trail in other._olderTrails.Append(other._trail))
        {
            if (trail.Count > 0 && !_olderTrails.Any(t => t.SequenceEqual(trail)) && !_trail.SequenceEqual(trail))
                _olderTrails.Add(trail.ToArray());
        }
    }

    /// <summary>Adds everything to a state hash, in a fixed order.</summary>
    public void AddTo(StateHasher hasher)
    {
        hasher.Add(Nx).Add(Ny).Add(SeenCount);
        foreach (ulong bits in _seen)
            hasher.Add(bits);
        hasher.Add(_features.Count);
        foreach (FoundFeature f in _features.Values)
            hasher.Add((int)f.Kind).Add(f.Id).Add((int)f.How).Add(f.Tick);
        hasher.Add(_sheets.Count);
        foreach ((int area, int sheet) in _sheets)
            hasher.Add(area).Add(sheet);
        hasher.Add(_trail.Count);
        foreach (TrailStep s in _trail)
            hasher.Add(s.Cell).Add(s.Tick);
        hasher.Add(_olderTrails.Count);
        foreach (IReadOnlyList<TrailStep> trail in _olderTrails)
        {
            hasher.Add(trail.Count);
            foreach (TrailStep s in trail)
                hasher.Add(s.Cell).Add(s.Tick);
        }
        hasher.Add(_sites.Count);
        foreach (int s in _sites)
            hasher.Add(s);
        hasher.Add(_rooms.Count);
        foreach (int r in _rooms)
            hasher.Add(r);
        hasher.Add(_mapsRead.Count);
        foreach (int m in _mapsRead)
            hasher.Add(m);
        hasher.Add(_notes.Count);
        foreach (MapNote n in _notes)
            hasher.Add((int)n.Kind).Add((long)Math.Round(n.X * 100)).Add((long)Math.Round(n.Y * 100)).Add(n.Text).Add(n.Tick);
    }
}
