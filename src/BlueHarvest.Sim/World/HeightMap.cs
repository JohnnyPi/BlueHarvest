namespace BlueHarvest.Sim.World;

/// <summary>Terrain height per tile, in decimeters.</summary>
public sealed class HeightMap
{
    /// <summary>One height level is 0.5 m.</summary>
    public const short DecimetersPerLevel = 5;

    /// <summary>Highest level a map file can store: 'z' = 35 levels = 17.5 m.</summary>
    public const int MaxLevel = 35;

    private readonly short[] _heights;

    public HeightMap(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Must be positive.");
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), "Must be positive.");
        Width = width;
        Height = height;
        _heights = new short[width * height];
    }

    public int Width { get; }
    public int Height { get; }

    public bool InBounds(TilePos p) => p.X >= 0 && p.Y >= 0 && p.X < Width && p.Y < Height;

    /// <summary>Height in decimeters.</summary>
    public short this[TilePos p]
    {
        get => _heights[Index(p)];
        set => _heights[Index(p)] = value;
    }

    /// <summary>Height in whole levels of 0.5 m.</summary>
    public int LevelAt(TilePos p) => this[p] / DecimetersPerLevel;

    public void SetLevel(TilePos p, int level)
    {
        if (level < 0 || level > MaxLevel)
            throw new ArgumentOutOfRangeException(nameof(level), $"Level must be 0-{MaxLevel}.");
        this[p] = (short)(level * DecimetersPerLevel);
    }

    /// <summary>Height change moving from one tile to another, in decimeters. Positive is uphill.</summary>
    public int StepDm(TilePos from, TilePos to) => this[to] - this[from];

    /// <summary>Character for a level: '0'-'9' then 'a'-'z'.</summary>
    public static char LevelChar(int level)
    {
        if (level < 0 || level > MaxLevel)
            throw new ArgumentOutOfRangeException(nameof(level), $"Level must be 0-{MaxLevel}.");
        return level < 10 ? (char)('0' + level) : (char)('a' + level - 10);
    }

    /// <summary>Level for a character, or -1 if it isn't '0'-'9' or 'a'-'z'.</summary>
    public static int ParseLevel(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'z' => c - 'a' + 10,
        _ => -1,
    };

    /// <summary>
    /// Builds a map from text rows, one character per tile: '0'-'9' then 'a'-'z', each a 0.5 m level.
    /// So "0" is 0 m, "4" is 2 m and "g" is 8 m.
    /// </summary>
    public static HeightMap FromRows(params string[] rows)
    {
        if (rows.Length == 0 || rows[0].Length == 0)
            throw new FormatException("A height map needs at least one non-empty row.");

        int width = rows[0].Length;
        var map = new HeightMap(width, rows.Length);
        for (int y = 0; y < rows.Length; y++)
        {
            if (rows[y].Length != width)
                throw new FormatException($"Height row {y} has {rows[y].Length} characters; expected {width}.");

            for (int x = 0; x < width; x++)
            {
                int level = ParseLevel(rows[y][x]);
                if (level < 0)
                    throw new FormatException($"Height row {y}, column {x}: '{rows[y][x]}' is not 0-9 or a-z.");
                map.SetLevel(new TilePos(x, y), level);
            }
        }
        return map;
    }

    private int Index(TilePos p)
    {
        if (!InBounds(p))
            throw new ArgumentOutOfRangeException(nameof(p), $"{p} is outside the {Width}x{Height} map.");
        return p.Y * Width + p.X;
    }
}
