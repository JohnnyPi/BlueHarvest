using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.World;
using BlueHarvest.Sim.World.Generation;
using BlueHarvest.Sim.ContentIntegration;

namespace BlueHarvest.Sim;

/// <summary>
/// Map knowledge (step 25b-1): on an island run each survivor fills in their own island map as they
/// explore (<see cref="MapKnowledge"/>), updated after each of their actions by the island-scale view
/// rule (<see cref="MapSight"/>). Off unless a run turns it on for an actor, so the testbed's state
/// hash and batches don't change; in the state hash once on.
/// </summary>
public sealed partial class Simulation
{
    /// <summary>The ambient light at or above which it counts as daylight for seeing across the island, %.</summary>
    public const int DaylightPercent = 50;

    private IslandWorld? _mapWorld;
    private FeatureIndex? _mapFeatures;
    private readonly SortedDictionary<int, MapKnowledge> _knowledge = new();
    /// <summary>Where each survivor last looked from and how far: no need to look again until one changes.</summary>
    private readonly Dictionary<int, (long X, long Y, double Range)> _lastSight = new();

    /// <summary>Raised when a survivor finds a ruin, a wreck or a cave they didn't know of.</summary>
    public event Action<Actor, FoundFeature>? FeatureFound;

    /// <summary>Raised the first time a survivor enters a site (an index into the island's sites).</summary>
    public event Action<Actor, int>? SiteFound;

    /// <summary>
    /// Raised when a survivor with map knowledge picks up a park map: the sheet it charts, and whether
    /// that sheet is new to them (false: they'd charted it already, and it adds nothing).
    /// </summary>
    public event Action<Actor, Item, ChartedSheet, bool>? MapCharted;

    /// <summary>
    /// Raised on a survivor's first read of a park map, with what it turned up: a wreck, ruin or cave
    /// marked on it (found "from a map"), or null. Later reads raise nothing.
    /// </summary>
    public event Action<Actor, Item, FoundFeature?>? ParkMapRead;

    /// <summary>
    /// Turns map knowledge on for one actor (the survivor), starting from an empty map or from one
    /// given (a recovered map merged in beforehand, say). Call it once the map's place on the island is
    /// known (after <see cref="UsePower"/> or <see cref="FollowWeather"/>); it looks once at once.
    /// </summary>
    public MapKnowledge UseMapKnowledge(Actor actor, IslandWorld world, FeatureIndex? features = null, MapKnowledge? start = null)
    {
        if (start is not null && (start.Nx != world.Shape.Nx || start.Ny != world.Shape.Ny))
            throw new ArgumentException("The map given is for an island of another size.", nameof(start));
        _mapWorld = world;
        _mapFeatures = features ?? _mapFeatures ?? FeatureIndex.Build(world);
        MapKnowledge k = start ?? new MapKnowledge(world.Shape.Nx, world.Shape.Ny);
        _knowledge[actor.Id] = k;
        _lastSight.Remove(actor.Id);
        UpdateKnowledge(actor);
        return k;
    }

    /// <summary>What an actor knows of the island, or null if map knowledge is off for it.</summary>
    public MapKnowledge? Knowledge(Actor actor) => _knowledge.GetValueOrDefault(actor.Id);

    /// <summary>After an actor's action: what it sees from where it stands now, if map knowledge is on for it.</summary>
    private void UpdateKnowledge(Actor actor)
    {
        if (_knowledge.Count == 0 || _mapWorld is null || _mapFeatures is null || actor.IsGone
            || !_knowledge.TryGetValue(actor.Id, out MapKnowledge? k))
            return;
        var (ix, iy) = ToIsland(actor.Position);
        double x = ix + 0.5, y = iy + 0.5;
        double range = MapSight.Range(_mapWorld, x, y, AmbientLightPercent >= DaylightPercent, Map.IsIndoor(actor.Position));
        if (_lastSight.TryGetValue(actor.Id, out var last) && last == (ix, iy, range))
            return;
        _lastSight[actor.Id] = (ix, iy, range);
        var (found, sites) = MapSight.See(k, _mapWorld, _mapFeatures, x, y, range, CurrentTick);
        foreach (FoundFeature f in found)
            FeatureFound?.Invoke(actor, f);
        foreach (int site in sites)
            SiteFound?.Invoke(actor, site);
    }

    /// <summary>Picking up a park map charts its sheet (step 25b-2).</summary>
    private void ChartParkMap(Actor actor, Item item)
    {
        if (!item.Def.Has(ItemHandlers.ParkMap) || _mapWorld is null || !_knowledge.TryGetValue(actor.Id, out MapKnowledge? k)
            || !ParkMaps.TryParse(item.Tag, _mapWorld.Works, out int area, out int sheet))
            return;
        bool fresh = k.Chart(area, sheet);
        MapCharted?.Invoke(actor, item, new ChartedSheet(area, sheet), fresh);
    }

    /// <summary>
    /// The first read of a park map (step 25b-2, detail 4): one roll, with Tracking's odds, for a wreck,
    /// ruin or cave the reader doesn't know, in the map's sheet or, failing that, elsewhere in its area.
    /// The roll and the pick are addressed by (run seed, map, reader), so they repeat exactly.
    /// </summary>
    private void ReadParkMap(Actor actor, Item item)
    {
        if (!item.Def.Has(ItemHandlers.ParkMap) || _mapWorld is null || !_knowledge.TryGetValue(actor.Id, out MapKnowledge? k)
            || !ParkMaps.TryParse(item.Tag, _mapWorld.Works, out int area, out int sheet) || !k.MarkRead(item.Id))
            return;
        k.Chart(area, sheet); // a map read is a map in hand
        ulong roll = ParkMaps.ReadHash(Rng.Seed, item.Id, actor.Id);
        FoundFeature? found = null;
        if ((int)(roll % 100) < ParkMaps.FindPercent(actor.Skills.Tracking)
            && MarkedOn(_mapWorld, k, area, sheet) is { Count: > 0 } pool)
        {
            ulong pick = new StateHasher().Add(roll).Add("pick").Value;
            (FeatureKind kind, int id) = pool[(int)(pick % (ulong)pool.Count)];
            var f = new FoundFeature(kind, id, FoundHow.FromMap, CurrentTick);
            if (k.Find(f))
            {
                found = f;
                FeatureFound?.Invoke(actor, f);
            }
        }
        ParkMapRead?.Invoke(actor, item, found);
    }

    /// <summary>What a park map can turn up for a reader: unknown features in its sheet, or else elsewhere in its area. Ruins, then wrecks, then caves, by id.</summary>
    private static List<(FeatureKind Kind, int Id)> MarkedOn(IslandWorld world, MapKnowledge k, int area, int sheet)
    {
        FacilityAreas areas = world.Areas();
        IslandWorks w = world.Works;
        var inSheet = new List<(FeatureKind, int)>();
        var inArea = new List<(FeatureKind, int)>();
        void Consider(FeatureKind kind, int id, double x, double y)
        {
            int cell = areas.CellAt(x, y);
            if (areas.AreaOf(cell) != area || k.Knows(kind, id))
                return;
            (areas.SheetOf(cell) == sheet ? inSheet : inArea).Add((kind, id));
        }
        foreach (Ruin r in w.Ruins)
            Consider(FeatureKind.Ruin, r.Id, r.X, r.Y);
        foreach (Wreck r in w.Wrecks)
            Consider(FeatureKind.Wreck, r.Id, r.X, r.Y);
        foreach (Cave c in w.Caves)
        {
            (double x, double y) = c.Mouths.Count > 0 ? c.Mouths[0] : (c.Xs[0], c.Ys[0]);
            Consider(FeatureKind.Cave, c.Id, x, y);
        }
        return inSheet.Count > 0 ? inSheet : inArea;
    }

    /// <summary>A room entered goes on the actor's map, if map knowledge is on for it.</summary>
    private void NoteRoomKnown(Actor actor, IndexedRoom room)
    {
        if (_knowledge.TryGetValue(actor.Id, out MapKnowledge? k))
            k.EnterRoom(room.Id);
    }

    private void AddKnowledgeTo(StateHasher hasher)
    {
        if (_knowledge.Count == 0)
            return; // off: the hash is as it was before map knowledge existed
        hasher.Add(_knowledge.Count);
        foreach ((int id, MapKnowledge k) in _knowledge)
        {
            hasher.Add(id);
            k.AddTo(hasher);
        }
    }
}
