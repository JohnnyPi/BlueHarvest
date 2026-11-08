using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim.Actors;

/// <summary>
/// Parts of a body. The core is solid: it occupies tiles, blocks others and must fit the terrain.
/// Head and tail are soft: they pass over obstacles, flatten light vegetation and strike what they sweep.
/// </summary>
public enum BodyPart : byte
{
    Core,
    Head,
    Tail,
}

/// <summary>
/// A stretch of body along the spine. Spine distances are in tiles from the anchor, positive forward;
/// HalfWidth 1 makes it 3 tiles wide.
/// </summary>
public sealed record BodySegment(BodyPart Part, int SpineFrom, int SpineTo, int HalfWidth);

public readonly record struct BodyTile(TilePos Position, BodyPart Part);

/// <summary>Sixteen compass directions as unit vectors ×1000, clockwise from north. Y grows downward.</summary>
public static class Compass16
{
    private static readonly (int X, int Y)[] Units =
    {
        (0, -1000), (383, -924), (707, -707), (924, -383),
        (1000, 0), (924, 383), (707, 707), (383, 924),
        (0, 1000), (-383, 924), (-707, 707), (-924, 383),
        (-1000, 0), (-924, -383), (-707, -707), (-383, -924),
    };

    public static int Of(Direction direction) => (int)direction * 2;

    public static (int X, int Y) Unit(int dir16) => Units[((dir16 % 16) + 16) % 16];
}

/// <summary>A body's shape. Footprints are computed from it at any position and facing.</summary>
public sealed class BodyPlan
{
    public BodyPlan(params BodySegment[] segments)
    {
        if (segments.Length == 0 || !segments.Any(s => s.Part == BodyPart.Core))
            throw new ArgumentException("A body needs at least one core segment.", nameof(segments));
        Segments = segments;
        IsSingleTile = segments.Length == 1
            && segments[0] is { Part: BodyPart.Core, SpineFrom: 0, SpineTo: 0, HalfWidth: 0 };
        HeadReach = segments.Where(s => s.Part == BodyPart.Head).Select(s => s.SpineTo).DefaultIfEmpty(0).Max();
    }

    public IReadOnlyList<BodySegment> Segments { get; }

    /// <summary>One tile, turning freely: humans and (for now) raptors.</summary>
    public bool IsSingleTile { get; }

    /// <summary>How far ahead of the anchor the head reaches, in tiles. The eyes and jaws are there.</summary>
    public int HeadReach { get; }

    public static readonly BodyPlan SingleTile = new(new BodySegment(BodyPart.Core, 0, 0, 0));

    /// <summary>
    /// About 12 m: head and neck 2-4 tiles ahead of the anchor, a 3-wide core from 2 behind to 1 ahead,
    /// and a tail 3-7 tiles behind.
    /// </summary>
    public static readonly BodyPlan Rex = new(
        new BodySegment(BodyPart.Core, -2, 1, 1),
        new BodySegment(BodyPart.Head, 2, 4, 0),
        new BodySegment(BodyPart.Tail, -7, -3, 0));

    /// <summary>
    /// The tiles the body covers with its anchor at <paramref name="anchor"/>, facing one of 16 directions.
    /// Sampled every half tile so diagonal bodies have no gaps. Where parts overlap, core wins, then head.
    /// Tiles may fall outside the map; callers check bounds.
    /// </summary>
    public IReadOnlyList<BodyTile> Footprint(TilePos anchor, int dir16)
    {
        var (ux, uy) = Compass16.Unit(dir16);
        var (rx, ry) = Compass16.Unit(dir16 + 4); // to the body's right
        var tiles = new List<BodyTile>();
        var seen = new HashSet<TilePos>();

        foreach (var part in new[] { BodyPart.Core, BodyPart.Head, BodyPart.Tail })
        {
            foreach (var segment in Segments)
            {
                if (segment.Part != part)
                    continue;
                for (int k2 = segment.SpineFrom * 2; k2 <= segment.SpineTo * 2; k2++)
                {
                    for (int l2 = -segment.HalfWidth * 2; l2 <= segment.HalfWidth * 2; l2++)
                    {
                        var p = new TilePos(
                            anchor.X + RoundDiv((long)k2 * ux + (long)l2 * rx, 2000),
                            anchor.Y + RoundDiv((long)k2 * uy + (long)l2 * ry, 2000));
                        if (seen.Add(p))
                            tiles.Add(new BodyTile(p, part));
                    }
                }
            }
        }
        return tiles;
    }

    /// <summary>The head tip: anchor plus HeadReach along the facing.</summary>
    public TilePos HeadTip(TilePos anchor, int dir16)
    {
        var (ux, uy) = Compass16.Unit(dir16);
        return new TilePos(anchor.X + RoundDiv((long)HeadReach * ux, 1000), anchor.Y + RoundDiv((long)HeadReach * uy, 1000));
    }

    /// <summary>Integer division rounding halves away from zero.</summary>
    private static int RoundDiv(long numerator, long denominator) =>
        (int)(numerator >= 0
            ? (numerator + denominator / 2) / denominator
            : -((-numerator + denominator / 2) / denominator));
}
