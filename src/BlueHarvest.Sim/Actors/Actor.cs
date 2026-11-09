using BlueHarvest.Sim.Injuries;
using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim.Actors;

/// <summary>Anything that takes turns: the player, NPCs, dinosaurs.</summary>
public sealed class Actor
{
    /// <summary>LastMovedTick for an actor that has never moved.</summary>
    public const long NeverMoved = long.MinValue / 4;

    internal Actor(int id, string name, char glyph, TilePos position, Species species, Direction facing, IActorController controller)
    {
        Id = id;
        Name = name;
        Glyph = glyph;
        Position = position;
        Species = species;
        Facing = facing;
        Controller = controller;
        Body = species.Body.Footprint(position, Compass16.Of(facing));
    }

    public int Id { get; }
    public string Name { get; }
    public char Glyph { get; }

    /// <summary>The body's anchor: the tile itself for small actors, the middle of the core for large ones.</summary>
    public TilePos Position { get; internal set; }

    public Species Species { get; }
    public MovementProfile Movement => Species.Movement;
    public IActorController Controller { get; internal set; }

    /// <summary>More than one tile: turns in 45° steps, occupies its whole core, smashes through cover.</summary>
    public bool IsLarge => !Species.Body.IsSingleTile;

    /// <summary>Tiles the body covers now. May include head or tail tiles outside the map.</summary>
    public IReadOnlyList<BodyTile> Body { get; internal set; }

    /// <summary>Which way the actor faces. Small actors turn as they move; large ones turn in 45° steps.</summary>
    public Direction Facing { get; internal set; }

    /// <summary>When it last left scent on the ground (each step, and every 20 s while still).</summary>
    public long LastScentTick { get; internal set; } = NeverMoved;

    /// <summary>The tick of its last successful step or turn.</summary>
    public long LastMovedTick { get; internal set; } = NeverMoved;

    /// <summary>The tick at which this actor next gets a turn.</summary>
    public long NextActTick { get; internal set; }

    /// <summary>Its six body sections, blood lost, and whether it's dead.</summary>
    public Health Health { get; } = new();

    public bool IsDead => Health.Dead;

    /// <summary>Whether its last step was at a charge (large bodies).</summary>
    public bool Charging { get; internal set; }

    /// <summary>The exit it left the map by, or null while it's still here.</summary>
    public ExitPoint? EscapedVia { get; internal set; }

    /// <summary>When it escaped, or null.</summary>
    public long? EscapedTick { get; internal set; }

    public bool HasEscaped => EscapedVia is not null;

    /// <summary>Dead or escaped: off the map as far as everyone else is concerned (no turns, not perceived).</summary>
    public bool IsGone => IsDead || HasEscaped;

    public Skills Skills { get; internal set; } = Skills.None;

    public Inventory Inventory { get; } = new();

    /// <summary>The last order it was given, if any (people only).</summary>
    public StandingOrder? Order { get; internal set; }

    /// <summary>Index in Simulation.Lights of the flashlight it carries (on or off), once it has switched one on.</summary>
    public int? CarriedLight { get; internal set; }

    /// <summary>Standing on the deck over its tile rather than the ground. Always false for large bodies.</summary>
    public bool OnDeck { get; internal set; }

    /// <summary>Its most recent bite, or null if it has never bitten.</summary>
    public BiteResult? LastBite { get; internal set; }

    /// <summary>Whether it stepped within the last <paramref name="windowTicks"/> ticks.</summary>
    public bool MovedRecently(long now, long windowTicks) => now - LastMovedTick <= windowTicks;
}
