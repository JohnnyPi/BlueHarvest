#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BlueHarvest.Sim;
using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.World;

namespace BlueHarvest.Game.Rendering;

/// <summary>How a tile is drawn under the fog of war.</summary>
public enum FogState : byte
{
    /// <summary>In view now: drawn as it is, lit by the light grid.</summary>
    Visible,

    /// <summary>Seen before: drawn as you last saw it, cool and unlit, with nobody on it.</summary>
    Remembered,

    /// <summary>Never seen, but on the park plan (fences, buildings, the track): a faint blueprint.</summary>
    Plan,

    /// <summary>Never seen and not on the plan: black.</summary>
    Unknown,
}

/// <summary>Where a large creature was when the player last made it out.</summary>
public sealed record Ghost(int ActorId, string Name, IReadOnlyList<BodyTile> Body, TilePos Tip, long Tick);

/// <summary>
/// The player's map memory, kept in the front end only: what's in view now (from the player's own
/// sight scores), what was seen before and what it looked like then, the park plan as built, which
/// buildings' roofs have been seen, and where each large creature was last seen. It never feeds back
/// into the simulation, so replays and the state hash don't change.
/// </summary>
public sealed class FogOfWar
{
    /// <summary>
    /// A tile counts as in view when a moving person standing there would score at least this to the
    /// player's eyes: half the human detect threshold, enough to make out the ground. At night in the
    /// testbed's rain that's about 13 tiles; in lamplight, the full rain-shortened sight range.
    /// </summary>
    public const int GlimpseScore = 6;

    private readonly int _width;
    private readonly int _height;
    private readonly bool[] _visible;
    private readonly long[] _seenTick;
    private readonly CoverType[] _rememberedCover;
    private readonly DoorState[] _rememberedDoor;
    private readonly CoverType[] _plan;
    private readonly int[] _roofGroup;
    private readonly List<List<int>> _roofBorders = new();
    private bool[] _roofVisible = Array.Empty<bool>();
    private long[] _roofSeenTick = Array.Empty<long>();
    private readonly Dictionary<int, Ghost> _ghosts = new();

    private readonly bool _usePlan;

    /// <param name="plan">
    /// Whether the player starts with the site plan (walls, fences, tracks drawn faintly before they are seen).
    /// False on the island, where nothing is known until it is seen.
    /// </param>
    public FogOfWar(Simulation sim, bool plan = true)
    {
        _usePlan = plan;
        var map = sim.Map;
        _width = map.Width;
        _height = map.Height;
        int count = _width * _height;
        _visible = new bool[count];
        _seenTick = new long[count];
        Array.Fill(_seenTick, -1L);
        _rememberedCover = new CoverType[count];
        _rememberedDoor = new DoorState[count];
        _plan = new CoverType[count];
        _roofGroup = new int[count];
        Array.Fill(_roofGroup, -1);

        for (int y = 0; y < _height; y++)
            for (int x = 0; x < _width; x++)
            {
                var p = new TilePos(x, y);
                _plan[y * _width + x] = plan ? sim.Cover[p] : CoverType.Grass;
            }
        FindBuildings(map);
    }

    /// <summary>
    /// The streamed map scrolled by (dx, dy) tiles: what was at (x, y) is now at (x − dx, y − dy).
    /// What the player has seen moves with it; tiles that scroll in are unknown. Buildings are found
    /// again on the new map (so which roofs have been seen is forgotten), and ghosts move or are dropped.
    /// </summary>
    public void Shift(int dx, int dy, Simulation sim)
    {
        Scroll(_visible, dx, dy, false);
        Scroll(_seenTick, dx, dy, -1L);
        Scroll(_rememberedCover, dx, dy, CoverType.Grass);
        Scroll(_rememberedDoor, dx, dy, DoorState.Open);
        for (int y = 0; y < _height; y++)
            for (int x = 0; x < _width; x++)
                _plan[y * _width + x] = _usePlan ? sim.Cover[new TilePos(x, y)] : CoverType.Grass;
        Array.Fill(_roofGroup, -1);
        _roofBorders.Clear();
        FindBuildings(sim.Map);
        PlayerRoofGroup = -1;
        foreach (int id in _ghosts.Keys.ToList())
        {
            Ghost g = _ghosts[id];
            var body = g.Body.Select(b => new BodyTile(new TilePos(b.Position.X - dx, b.Position.Y - dy), b.Part)).ToList();
            var tip = new TilePos(g.Tip.X - dx, g.Tip.Y - dy);
            if (sim.Map.InBounds(tip) && body.All(b => sim.Map.InBounds(b.Position))) _ghosts[id] = g with { Body = body, Tip = tip };
            else _ghosts.Remove(id);
        }
    }

    private void Scroll<T>(T[] cells, int dx, int dy, T blank)
    {
        // walk so that every source cell is read before it is overwritten
        int yStart = dy >= 0 ? 0 : _height - 1, yStep = dy >= 0 ? 1 : -1;
        int xStart = dx >= 0 ? 0 : _width - 1, xStep = dx >= 0 ? 1 : -1;
        for (int n = 0, y = yStart; n < _height; n++, y += yStep)
        {
            int sy = y + dy;
            for (int m = 0, x = xStart; m < _width; m++, x += xStep)
            {
                int sx = x + dx;
                cells[y * _width + x] = sx >= 0 && sy >= 0 && sx < _width && sy < _height ? cells[sy * _width + sx] : blank;
            }
        }
    }

    /// <summary>The roof group (one per building) the player is under, or -1 outdoors.</summary>
    public int PlayerRoofGroup { get; private set; } = -1;

    /// <summary>Where each large creature was last made out, by actor id.</summary>
    public IReadOnlyDictionary<int, Ghost> Ghosts => _ghosts;

    public bool IsVisible(TilePos p) => _visible[Index(p)];

    public long SeenTick(TilePos p) => _seenTick[Index(p)];

    public CoverType RememberedCover(TilePos p) => _rememberedCover[Index(p)];

    public DoorState RememberedDoor(TilePos p) => _rememberedDoor[Index(p)];

    public CoverType PlanCover(TilePos p) => _plan[Index(p)];

    public int RoofGroupAt(TilePos p) => _roofGroup[Index(p)];

    /// <summary>How a tile is drawn. A roof you're not under is in view when any of its walls is.</summary>
    public FogState StateAt(TilePos p, bool roofShown)
    {
        int i = Index(p);
        int group = _roofGroup[i];
        if (roofShown && group >= 0)
        {
            if (_roofVisible[group])
                return FogState.Visible;
            if (_roofSeenTick[group] >= 0)
                return FogState.Remembered;
            return FogState.Plan;
        }
        if (_visible[i])
            return FogState.Visible;
        if (_seenTick[i] >= 0)
            return FogState.Remembered;
        return MapLook.OnPlan(_plan[i]) ? FogState.Plan : FogState.Unknown;
    }

    /// <summary>Works out what's in view after a turn and adds it to memory.</summary>
    public void Update(Simulation sim, Actor player, Percepts percepts)
    {
        long now = sim.CurrentTick;
        Array.Clear(_visible);

        var senses = sim.SensesOf(player);
        int range = senses.SightRangeTiles;
        var at = player.Position;
        int bodyHeight = Species.Human.BodyHeightDm;
        for (int y = Math.Max(0, at.Y - range); y <= Math.Min(_height - 1, at.Y + range); y++)
        {
            for (int x = Math.Max(0, at.X - range); x <= Math.Min(_width - 1, at.X + range); x++)
            {
                var p = new TilePos(x, y);
                bool near = Math.Abs(x - at.X) <= 1 && Math.Abs(y - at.Y) <= 1;
                if (!near && PerceptionSystem.SightScore(sim, player, p, bodyHeight, moving: true) < GlimpseScore)
                    continue;
                int i = y * _width + x;
                _visible[i] = true;
                _seenTick[i] = now;
                _rememberedCover[i] = sim.Cover[p];
                _rememberedDoor[i] = sim.Cover[p] == CoverType.Door ? sim.Map.Structures.DoorAt(p) : DoorState.Open;
            }
        }

        PlayerRoofGroup = sim.Map.IsIndoor(at) ? _roofGroup[Index(at)] : -1;
        for (int g = 0; g < _roofBorders.Count; g++)
        {
            _roofVisible[g] = g != PlayerRoofGroup && _roofBorders[g].Any(i => _visible[i]);
            if (_roofVisible[g])
                _roofSeenTick[g] = now;
        }

        foreach (var actor in sim.Actors)
        {
            if (!actor.IsLarge || actor.IsDead || !percepts.Detects(actor))
                continue;
            _ghosts[actor.Id] = new Ghost(actor.Id, actor.Name, actor.Body.ToList(), sim.EyeTile(actor), now);
        }
    }

    /// <summary>Groups roofed tiles into buildings and lists the tiles around each one (its walls and doors).</summary>
    private void FindBuildings(WorldMap map)
    {
        var queue = new Queue<int>();
        for (int start = 0; start < _roofGroup.Length; start++)
        {
            if (_roofGroup[start] >= 0 || !map.IsIndoor(PosOf(start)))
                continue;
            int group = _roofBorders.Count;
            var border = new HashSet<int>();
            _roofGroup[start] = group;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                var p = PosOf(i);
                foreach (var d in Directions.All)
                {
                    var n = p.Offset(d);
                    if (!map.InBounds(n))
                        continue;
                    int j = Index(n);
                    if (!map.IsIndoor(n))
                    {
                        border.Add(j);
                        continue;
                    }
                    if (_roofGroup[j] >= 0)
                        continue;
                    _roofGroup[j] = group;
                    queue.Enqueue(j);
                }
            }
            _roofBorders.Add(border.ToList());
        }
        _roofVisible = new bool[_roofBorders.Count];
        _roofSeenTick = new long[_roofBorders.Count];
        Array.Fill(_roofSeenTick, -1L);
    }

    private int Index(TilePos p) => p.Y * _width + p.X;

    private TilePos PosOf(int i) => new(i % _width, i / _width);
}
