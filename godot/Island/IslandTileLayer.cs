#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueHarvest.Sim.World.Generation;
using Godot;

namespace BlueHarvest.Game.Island;

/// <summary>
/// The island at 1 m per tile, streamed: builds the 64 m chunks in view on worker threads and shows
/// each as a small picture over the island map. One unit is one grid cell, like the map under it.
/// Chunks out of view are kept until there are too many, then the farthest are dropped.
/// </summary>
public partial class IslandTileLayer : Node2D
{
    private const int MaxInFlight = 4;
    private const int MaxKept = 500;

    private sealed class Shown
    {
        public required IslandChunk Chunk;
        public required Sprite2D Sprite;
    }

    private IslandWorld? _world;
    private Task<IslandChunker>? _chunkerJob;
    private IslandChunker? _chunker;
    private int _generation; // bumps when the island changes, so late results for the old one are dropped
    private readonly Dictionary<(int X, int Y), Shown> _shown = new();
    private readonly HashSet<(int X, int Y)> _pending = new();
    private readonly ConcurrentQueue<(int Generation, int X, int Y, IslandChunk Chunk, byte[] Pixels)> _done = new();

    /// <summary>Chunks built and waiting to be built, for the status line.</summary>
    public int ShownCount => _shown.Count;
    public int PendingCount => _pending.Count;

    public void SetWorld(IslandWorld? world)
    {
        _generation++;
        foreach (Shown s in _shown.Values) s.Sprite.QueueFree();
        _shown.Clear();
        _pending.Clear();
        _world = world;
        _chunker = null;
        _chunkerJob = world == null ? null : Task.Run(() => new IslandChunker(world));
    }

    /// <summary>The tile at a world position in metres, if its chunk has been built.</summary>
    public bool TryGetTile(double xm, double ym, out IslandChunk chunk, out int index)
    {
        int cx = (int)Math.Floor(xm / IslandChunker.ChunkSize), cy = (int)Math.Floor(ym / IslandChunker.ChunkSize);
        if (_shown.TryGetValue((cx, cy), out Shown? s))
        {
            chunk = s.Chunk;
            int tx = Math.Clamp((int)Math.Floor(xm) - chunk.X0, 0, chunk.Size - 1), ty = Math.Clamp((int)Math.Floor(ym) - chunk.Y0, 0, chunk.Size - 1);
            index = ty * chunk.Size + tx;
            return true;
        }
        chunk = null!;
        index = 0;
        return false;
    }

    /// <summary>
    /// Call every frame while the tile view is on, with the visible rectangle in cells.
    /// Takes finished chunks, then asks for the missing ones nearest the middle of the view.
    /// </summary>
    public void Stream(Rect2 viewCells)
    {
        if (_world == null) return;
        if (_chunker == null)
        {
            if (_chunkerJob is not { IsCompletedSuccessfully: true }) return;
            _chunker = _chunkerJob.Result;
        }
        float cell = (float)_world.Shape.Cell;
        const int size = IslandChunker.ChunkSize;

        while (_done.TryDequeue(out var r))
        {
            if (r.Generation != _generation) continue;
            _pending.Remove((r.X, r.Y));
            if (_shown.ContainsKey((r.X, r.Y))) continue;
            Image image = Image.CreateFromData(size, size, false, Image.Format.Rgba8, r.Pixels);
            var sprite = new Sprite2D
            {
                Centered = false,
                Texture = ImageTexture.CreateFromImage(image),
                TextureFilter = TextureFilterEnum.Nearest,
                Position = new Vector2(r.X * size / cell, r.Y * size / cell),
                Scale = new Vector2(1f / cell, 1f / cell),
            };
            AddChild(sprite);
            _shown[(r.X, r.Y)] = new Shown { Chunk = r.Chunk, Sprite = sprite };
        }

        // chunks in view, within the island's frame
        float perCell = cell / size;
        int x0 = Math.Max(0, Mathf.FloorToInt(viewCells.Position.X * perCell)), y0 = Math.Max(0, Mathf.FloorToInt(viewCells.Position.Y * perCell));
        int x1 = Math.Min((int)(_world.WidthM / size) - 1, Mathf.FloorToInt(viewCells.End.X * perCell));
        int y1 = Math.Min((int)(_world.HeightM / size) - 1, Mathf.FloorToInt(viewCells.End.Y * perCell));
        Vector2 mid = viewCells.GetCenter() * perCell;

        if (_pending.Count < MaxInFlight)
        {
            var wanted = new List<(int X, int Y)>();
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if (!_shown.ContainsKey((x, y)) && !_pending.Contains((x, y))) wanted.Add((x, y));
            foreach ((int x, int y) in wanted.OrderBy(c => new Vector2(c.X + 0.5f, c.Y + 0.5f).DistanceSquaredTo(mid)).Take(MaxInFlight - _pending.Count))
            {
                _pending.Add((x, y));
                IslandChunker chunker = _chunker;
                int generation = _generation;
                Task.Run(() =>
                {
                    try
                    {
                        IslandChunk chunk = chunker.Build(x, y);
                        _done.Enqueue((generation, x, y, chunk, IslandMapImage.RenderChunk(chunk)));
                    }
                    catch (Exception e)
                    {
                        GD.PrintErr($"Chunk {x},{y} failed: {e}");
                    }
                });
            }
        }

        if (_shown.Count > MaxKept)
        {
            foreach (var key in _shown.Keys.OrderByDescending(c => new Vector2(c.X + 0.5f, c.Y + 0.5f).DistanceSquaredTo(mid)).Take(_shown.Count - MaxKept + 50).ToList())
            {
                _shown[key].Sprite.QueueFree();
                _shown.Remove(key);
            }
        }
    }
}
