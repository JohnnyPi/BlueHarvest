namespace BlueHarvest.Sim.World.Generation;

/// <summary>
/// One tile that is no longer as the island generator makes it: what is there now. The ground's
/// height never changes, so it isn't kept.
/// </summary>
/// <param name="Index">The tile within its chunk, row-major: y · 64 + x.</param>
public readonly record struct TileChange(ushort Index, CoverType Cover, short RoofDm, DoorState Door, short DeckDm, bool Ladder);

/// <summary>
/// What has been done to the island, chunk by chunk: the tiles that differ from what the generator
/// makes (smashed fences, opened doors, later burnt ground). The streamed map (IslandWindow) puts a
/// chunk's changes here when the chunk scrolls out of it and takes them back when it scrolls in
/// again, so the island stays as you left it. The island's seed plus this is the island as it is
/// now, which is what a saved game will hold.
/// </summary>
public sealed class ChunkChanges
{
    private readonly Dictionary<(int X, int Y), TileChange[]> _chunks = new();

    /// <summary>How many chunks have changes.</summary>
    public int ChunkCount => _chunks.Count;

    /// <summary>How many tiles are changed, over every chunk.</summary>
    public int TileCount => _chunks.Values.Sum(c => c.Length);

    /// <summary>The chunks with changes, in a fixed order (by row, then column).</summary>
    public IReadOnlyList<(int X, int Y)> Chunks => _chunks.Keys.OrderBy(k => k.Y).ThenBy(k => k.X).ToList();

    /// <summary>The changed tiles of the chunk at chunk coordinates (cx, cy); none if it is untouched.</summary>
    public IReadOnlyList<TileChange> For(int cx, int cy) =>
        _chunks.TryGetValue((cx, cy), out TileChange[]? changes) ? changes : Array.Empty<TileChange>();

    /// <summary>Records a chunk's changed tiles, replacing what was there. An empty list forgets the chunk.</summary>
    public void Set(int cx, int cy, IReadOnlyList<TileChange> changes)
    {
        if (changes.Count == 0) _chunks.Remove((cx, cy));
        else _chunks[(cx, cy)] = changes.ToArray();
    }

    /// <summary>Forgets a chunk's changes. Returns whether there were any.</summary>
    public bool Remove(int cx, int cy) => _chunks.Remove((cx, cy));
}
