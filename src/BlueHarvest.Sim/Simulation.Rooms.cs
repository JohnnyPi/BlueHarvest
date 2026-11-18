using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.World;
using BlueHarvest.Sim.World.Generation;

namespace BlueHarvest.Sim;

/// <summary>
/// Rooms (step 25a-1): on an island run the simulation knows the island's rooms, so it can say which
/// room a tile is in and tell the front end (and later the quests) when someone leaves a room or
/// walks into one. A corridor is no room: stepping from a room into its corridor leaves the room.
/// Which room an actor is in follows from where it stands, so none of this is in the state hash.
/// </summary>
public sealed partial class Simulation
{
    private RoomIndex? _rooms;
    /// <summary>The room id each actor stood in after its last move, −1 outside every room.</summary>
    private readonly Dictionary<int, int> _roomOf = new();

    /// <summary>The island's rooms, if <see cref="UseRooms"/> was called.</summary>
    public RoomIndex? Rooms => _rooms;

    /// <summary>
    /// Raised after an actor's move takes it out of a room, with the room it left: into a corridor,
    /// outdoors, or (just before <see cref="ActorEnteredRoom"/>) straight into the next room.
    /// </summary>
    public event Action<Actor, IndexedRoom>? ActorLeftRoom;

    /// <summary>Raised after an actor's move takes it into a room, with the room. Not raised for an actor's first placing.</summary>
    public event Action<Actor, IndexedRoom>? ActorEnteredRoom;

    /// <summary>
    /// Takes the island's room index. Call it once the map's place on the island is known (after
    /// <see cref="UsePower"/> or <see cref="FollowWeather"/>), since rooms are found by island metres.
    /// </summary>
    public void UseRooms(RoomIndex index)
    {
        _rooms = index;
        _roomOf.Clear();
        foreach (Actor actor in _actors)
            _roomOf[actor.Id] = RoomAt(actor.Position)?.Id ?? -1;
    }

    /// <summary>The room a map tile is in, or null: outside every room, off the island, or no rooms in use.</summary>
    public IndexedRoom? RoomAt(TilePos tile)
    {
        if (_rooms is null)
            return null;
        var (x, y) = ToIsland(tile);
        return _rooms.At(x, y);
    }

    /// <summary>The rooms of one facility, an index into the island's sites. Empty without rooms in use.</summary>
    public IReadOnlyList<IndexedRoom> RoomsOf(int site) => _rooms?.OfSite(site) ?? Array.Empty<IndexedRoom>();

    /// <summary>After an actor moves: raises <see cref="ActorLeftRoom"/> and <see cref="ActorEnteredRoom"/> if it changed rooms.</summary>
    private void NoteRoom(Actor actor)
    {
        if (_rooms is null || actor.IsGone)
            return;
        IndexedRoom? now = RoomAt(actor.Position);
        int id = now?.Id ?? -1;
        if (!_roomOf.TryGetValue(actor.Id, out int was))
        {
            _roomOf[actor.Id] = id; // first placing: nothing was entered
            return;
        }
        if (was == id)
            return;
        _roomOf[actor.Id] = id;
        if (was >= 0)
            ActorLeftRoom?.Invoke(actor, _rooms.Rooms[was]);
        if (now is not null)
        {
            NoteRoomKnown(actor, now);
            ActorEnteredRoom?.Invoke(actor, now);
        }
    }

    /// <summary>When an actor is added: where it starts, so its first move can tell whether it changed rooms.</summary>
    private void StartRoom(Actor actor)
    {
        if (_rooms is not null)
            _roomOf[actor.Id] = RoomAt(actor.Position)?.Id ?? -1;
    }
}
