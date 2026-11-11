using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim;

/// <summary>
/// A controller (or anything else hanging off the simulation) that remembers tile positions and
/// must move them when a streamed map scrolls.
/// </summary>
public interface IWindowShiftListener
{
    /// <summary>The map scrolled: whatever was at (x, y) is now at (x − dx, y − dy).</summary>
    void OnWindowShifted(int dx, int dy, WorldMap map);
}

public sealed partial class Simulation
{
    /// <summary>
    /// Raised after <see cref="ShiftWindow"/>, with how far the map scrolled in tiles, so the front end
    /// can move its camera and whatever it remembers about tiles (fog of war) by the same amount.
    /// </summary>
    public event Action<int, int>? WindowShifted;

    /// <summary>How many times the window has moved. Part of nothing; handy for tests and the HUD.</summary>
    public int WindowShifts { get; private set; }

    /// <summary>
    /// Tells the simulation that its map has just been scrolled by (dx, dy) tiles (see WorldMap.Scroll and
    /// Generation.IslandWindow.Recenter): whatever was at (x, y) is now at (x − dx, y − dy). Everything
    /// the simulation holds by position moves with it. What ends up outside the map is dropped: scent,
    /// blood, noises, ropes and exits are forgotten, lights are switched off, and actors (other than
    /// the dead, who just stop being drawn) are marked <see cref="Actor.LeftBehind"/>.
    /// Call it between turns, not while an action is being resolved.
    /// </summary>
    public void ShiftWindow(int dx, int dy)
    {
        if (dx == 0 && dy == 0) return;
        WorldMap map = Map;
        TilePos Moved(TilePos p) => new(p.X - dx, p.Y - dy);
        TilePos Clamped(TilePos p) => new(Math.Clamp(p.X, 0, map.Width - 1), Math.Clamp(p.Y, 0, map.Height - 1));

        // ---- actors: who stays on the map? decided, and the leavers lifted off the grid, before it scrolls ----
        var leaving = new List<Actor>();
        foreach (Actor actor in _actors)
        {
            if (actor.IsGone) continue;
            bool stays = CoreTiles(actor.Body).All(t => map.InBounds(Moved(t)));
            if (stays) continue;
            Vacate(actor);
            leaving.Add(actor);
        }
        GridScroll.Scroll(_occupant, map.Width, map.Height, dx, dy);
        GridScroll.Scroll(_deckOccupant, map.Width, map.Height, dx, dy);
        foreach (Actor actor in _actors)
        {
            TilePos to = Moved(actor.Position);
            if (leaving.Contains(actor))
            {
                actor.LeftBehind = true;
                if (actor.CarriedLight is { } carried && _lights[carried].On) SetLightOn(carried, false);
            }
            // the gone keep a position inside the map so nothing that looks at it can fall off the edge
            actor.Position = actor.IsGone ? Clamped(to) : to;
            actor.Body = actor.Species.Body.Footprint(actor.Position, Compass16.Of(actor.Facing));
        }

        // ---- vehicles and platforms ----
        foreach (Vehicle vehicle in _vehicles)
        {
            if (vehicle.LeftBehind) continue;
            if (vehicle.Tiles.All(t => map.InBounds(Moved(t))))
            {
                vehicle.MoveBy(-dx, -dy, vehicle.Under.ToDictionary(kv => Moved(kv.Key), kv => kv.Value));
                continue;
            }
            // cut by the edge: the part still on the map goes back to the ground that was under it
            foreach (TilePos t in vehicle.Tiles)
            {
                TilePos to = Moved(t);
                if (map.InBounds(to) && map.Cover[to] == CoverType.Vehicle)
                    map.Cover[to] = vehicle.Under.TryGetValue(t, out CoverType under) ? under : CoverType.Grass;
                _vehicleAt[t.Y * map.Width + t.X] = 0;
            }
            vehicle.LeftBehind = true;
            vehicle.MoveBy(-vehicle.Anchor.X, -vehicle.Anchor.Y, new Dictionary<TilePos, CoverType>()); // parked at the corner, on no tile
        }
        GridScroll.Scroll(_vehicleAt, map.Width, map.Height, dx, dy);
        GridScroll.Scroll(_platformAt, map.Width, map.Height, dx, dy);
        foreach (Platform platform in _platforms) platform.Shift(dx, dy, map);

        // ---- lights: indexes are kept (actors, vehicles and sparks refer to them); the ones outside go dark ----
        for (int i = 0; i < _lights.Count; i++)
        {
            TilePos to = Moved(_lights[i].Position);
            _lights[i] = map.InBounds(to) ? _lights[i] with { Position = to } : _lights[i] with { Position = Clamped(to), On = false };
        }
        foreach (SparkingFence sparks in _sparks)
        {
            TilePos to = Moved(sparks.Position);
            if (!map.InBounds(to) && sparks.Live)
            {
                sparks.Live = false;
                sparks.DiedTick = CurrentTick;
            }
            sparks.Position = Clamped(to);
        }

        // ---- traces and events ----
        for (int i = 0; i < _noises.Count; i++) _noises[i] = _noises[i] with { Position = Moved(_noises[i].Position) };
        _noises.RemoveAll(e => !map.InBounds(e.Position));
        for (int i = 0; i < _vibrations.Count; i++) _vibrations[i] = _vibrations[i] with { Position = Moved(_vibrations[i].Position) };
        _vibrations.RemoveAll(e => !map.InBounds(e.Position));
        for (int i = 0; i < _blood.Count; i++) _blood[i] = _blood[i] with { Position = Moved(_blood[i].Position) };
        _blood.RemoveAll(b => !map.InBounds(b.Position));
        for (int i = 0; i < _scent.Count; i++) _scent[i] = _scent[i] with { Position = Moved(_scent[i].Position) };
        _scent.RemoveAll(m => !map.InBounds(m.Position));
        for (int i = 0; i < _ropes.Count; i++) _ropes[i] = new RopeSpot(Moved(_ropes[i].Top), Moved(_ropes[i].Bottom));
        _ropes.RemoveAll(r => !map.InBounds(r.Top) || !map.InBounds(r.Bottom));
        for (int i = 0; i < _exits.Count; i++) _exits[i] = _exits[i] with { Position = Moved(_exits[i].Position) };
        _exits.RemoveAll(e => !map.InBounds(e.Position));

        _lightGrid = null;

        // ---- minds ----
        foreach (Actor actor in _actors)
        {
            if (actor.Controller is IWindowShiftListener listener) listener.OnWindowShifted(dx, dy, map);
        }

        WindowShifts++;
        WindowShifted?.Invoke(dx, dy);
    }
}
