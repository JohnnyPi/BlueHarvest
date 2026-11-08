namespace BlueHarvest.Sim.World;

/// <summary>A tile coordinate. One tile is 1 m.</summary>
public readonly record struct TilePos(int X, int Y)
{
    public TilePos Offset(Direction direction)
    {
        var (dx, dy) = direction.Delta();
        return new TilePos(X + dx, Y + dy);
    }

    public override string ToString() => $"({X},{Y})";
}
