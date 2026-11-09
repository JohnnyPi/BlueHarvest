namespace BlueHarvest.Sim.World;

/// <summary>Eight-way movement. Y grows downward, matching screen coordinates.</summary>
public enum Direction : byte
{
    North,
    NorthEast,
    East,
    SouthEast,
    South,
    SouthWest,
    West,
    NorthWest,
}

public static class Directions
{
    public static readonly IReadOnlyList<Direction> All = new[]
    {
        Direction.North, Direction.NorthEast, Direction.East, Direction.SouthEast,
        Direction.South, Direction.SouthWest, Direction.West, Direction.NorthWest,
    };

    public static readonly IReadOnlyList<Direction> Cardinal = new[]
    {
        Direction.North, Direction.East, Direction.South, Direction.West,
    };

    public static (int Dx, int Dy) Delta(this Direction direction) => direction switch
    {
        Direction.North => (0, -1),
        Direction.NorthEast => (1, -1),
        Direction.East => (1, 0),
        Direction.SouthEast => (1, 1),
        Direction.South => (0, 1),
        Direction.SouthWest => (-1, 1),
        Direction.West => (-1, 0),
        Direction.NorthWest => (-1, -1),
        _ => throw new ArgumentOutOfRangeException(nameof(direction)),
    };

    public static bool IsDiagonal(this Direction direction) => ((int)direction & 1) == 1;

    public static Direction Opposite(this Direction direction) => (Direction)(((int)direction + 4) % 8);

    /// <summary>Nearest of the eight directions to an offset, without trigonometry. (0,0) gives East.</summary>
    public static Direction FromDelta(int dx, int dy)
    {
        int ax = Math.Abs(dx), ay = Math.Abs(dy);
        // tan(22.5°) ≈ 0.414; 5/12 ≈ 0.417 is close enough and stays in integers.
        if (ay * 12 <= ax * 5)
            return dx >= 0 ? Direction.East : Direction.West;
        if (ax * 12 <= ay * 5)
            return dy >= 0 ? Direction.South : Direction.North;
        return (dx > 0, dy > 0) switch
        {
            (true, true) => Direction.SouthEast,
            (true, false) => Direction.NorthEast,
            (false, true) => Direction.SouthWest,
            (false, false) => Direction.NorthWest,
        };
    }

    /// <summary>Reads a short compass name (N, NE, E, ...; any case), as written by <see cref="Compass"/>.</summary>
    public static bool TryParseCompass(string text, out Direction direction)
    {
        foreach (var d in All)
        {
            if (string.Equals(d.Compass(), text, StringComparison.OrdinalIgnoreCase))
            {
                direction = d;
                return true;
            }
        }
        direction = Direction.North;
        return false;
    }

    /// <summary>Short compass name: N, NE, E, ...</summary>
    public static string Compass(this Direction direction) => direction switch
    {
        Direction.North => "N",
        Direction.NorthEast => "NE",
        Direction.East => "E",
        Direction.SouthEast => "SE",
        Direction.South => "S",
        Direction.SouthWest => "SW",
        Direction.West => "W",
        Direction.NorthWest => "NW",
        _ => "?",
    };
}
