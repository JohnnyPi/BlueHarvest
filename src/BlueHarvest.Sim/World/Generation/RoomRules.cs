using System.Collections.Frozen;

namespace BlueHarvest.Sim.World.Generation;

/// <summary>
/// What a room's name says about it, in one table that the power plan, the room index and later the
/// quests all read: which rooms sit behind security doors and at what keycard level, which have
/// motorised doors, and which are only ways through. Names match the facility programmes
/// (<see cref="FacilityPrograms"/>) and the station rooms of the transit lines.
/// </summary>
public static class RoomRules
{
    /// <summary>
    /// Rooms behind security doors: maglocks that hold while the facility has power. None of them
    /// holds power equipment, so the switch panel that releases the locks can always be reached.
    /// </summary>
    public static readonly FrozenSet<string> SecureRooms = new[]
    {
        "Server hall", "Data centre", "Cryo vault", "Armoury", "Security office", "Security", "Sequencing lab", "Embryology lab",
        "Fertilisation lab", "Clean room", "Genetics lab", "Specimen store", "Records", "Health physics",
        "Fuel pool", "Cask store", "Accumulator chamber", "Holding cell 1", "Holding cell 2", "Holding cell 3", "Holding cell 4", "Animal holding",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Secure rooms whose doors want a level 3 keycard: vaults, the armoury, records and the reactor's fuel stores. The rest want level 2.</summary>
    public static readonly FrozenSet<string> Level3Rooms =
        new[] { "Cryo vault", "Armoury", "Records", "Fuel pool", "Cask store", "Accumulator chamber" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Rooms whose doors are motorised: elevators, airlocks, and the big doors of vehicle bays and cargo halls.</summary>
    public static readonly FrozenSet<string> MotorRooms =
        new[] { "Elevator", "Airlock", "Vehicle bays", "Hangar floor", "Boat bays", "Cargo hall" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Rooms that are only ways through, which the log doesn't name as you pass. Corridors aren't rooms
    /// at all (a building's corridor is the space between its two rows of rooms), so this is short.
    /// </summary>
    public static readonly FrozenSet<string> PassageRooms = new[] { "Airlock" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The keycard a room's security door wants: 3 or 2 for a secure room, 0 for any other.</summary>
    public static int KeycardLevel(string room) => Level3Rooms.Contains(room) ? 3 : SecureRooms.Contains(room) ? 2 : 0;

    public static bool IsMotorised(string room) => MotorRooms.Contains(room);

    public static bool IsPassage(string room) => PassageRooms.Contains(room);
}
