namespace BlueHarvest.Sim.World;

/// <summary>
/// A per-tile layer of small numbers (0–255). The map uses it for how damp the ground is by climate
/// (<see cref="WorldMap.Damp"/>) and how thick the smoke is (<see cref="WorldMap.Smoke"/>).
/// </summary>
public sealed class ByteMap
{
    private readonly byte[] _values;

    public ByteMap(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Must be positive.");
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), "Must be positive.");
        Width = width;
        Height = height;
        _values = new byte[width * height];
    }

    public int Width { get; }
    public int Height { get; }

    public int this[TilePos p]
    {
        get => _values[Index(p)];
        set => _values[Index(p)] = (byte)Math.Clamp(value, 0, 255);
    }

    /// <summary>Sets every tile to 0.</summary>
    public void Clear() => Array.Clear(_values);

    /// <summary>Moves every tile by (−dx, −dy); tiles scrolled in from outside are 0. See WorldMap.Scroll.</summary>
    internal void Scroll(int dx, int dy) => GridScroll.Scroll(_values, Width, Height, dx, dy);

    private int Index(TilePos p)
    {
        if (p.X < 0 || p.Y < 0 || p.X >= Width || p.Y >= Height)
            throw new ArgumentOutOfRangeException(nameof(p), $"{p} is outside the {Width}x{Height} map.");
        return p.Y * Width + p.X;
    }
}
