#nullable enable
using System;
using BlueHarvest.Sim.World.Generation;

namespace BlueHarvest.Game.Rendering;

/// <summary>
/// The height tint's scale on a generated island. The tint shows how high a tile stands among the
/// ground round it. It used to span the loaded map's own lowest to highest ground, so the same tile
/// changed shade whenever the streamed map moved. This takes "the ground round it" from the island's
/// height grid instead: for each grid cell, the lowest and highest ground over it and its eight
/// neighbours (about 384 m across, a little wider than the 192 m map), blended smoothly between cell
/// centres. A tile's tint then depends only on where it is on the island. Looks only.
/// </summary>
public sealed class ReliefScale
{
    /// <summary>
    /// The least rise the tint is spread over, in decimetres, so that nearly flat ground (a beach, a
    /// plateau) isn't striped light and dark by its half-metre height steps.
    /// </summary>
    public const float MinSpanDm = 20f;

    private readonly int _nx, _ny;
    private readonly double _cell;

    /// <summary>Per grid cell, in decimetres (never below sea level, as the game's tiles aren't): the lowest and highest ground over the cell and its neighbours.</summary>
    private readonly float[] _low, _high;

    public ReliefScale(IslandShape shape)
    {
        _nx = shape.Nx;
        _ny = shape.Ny;
        _cell = shape.Cell;
        int n = _nx * _ny;
        var dm = new float[n];
        for (int i = 0; i < n; i++)
            dm[i] = (float)Math.Max(0.0, shape.Height[i] * 10.0);

        _low = new float[n];
        _high = new float[n];
        for (int y = 0; y < _ny; y++)
        {
            for (int x = 0; x < _nx; x++)
            {
                float low = float.MaxValue, high = float.MinValue;
                for (int dy = -1; dy <= 1; dy++)
                {
                    int row = Math.Clamp(y + dy, 0, _ny - 1) * _nx;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        float v = dm[row + Math.Clamp(x + dx, 0, _nx - 1)];
                        low = Math.Min(low, v);
                        high = Math.Max(high, v);
                    }
                }
                _low[y * _nx + x] = low;
                _high[y * _nx + x] = high;
            }
        }
    }

    /// <summary>
    /// The tint for a tile: 0 at the lowest ground round it, 1 at the highest.
    /// </summary>
    /// <param name="x">The tile's centre on the island, in metres east.</param>
    /// <param name="y">The same, south.</param>
    /// <param name="heightDm">The tile's own ground height.</param>
    public float At(double x, double y, int heightDm)
    {
        // grid coordinates with (0, 0) at the first cell's centre, as GenGrid.Bilinear takes them
        double gx = x / _cell - 0.5, gy = y / _cell - 0.5;
        float low = (float)GenGrid.Bilinear(_low, _nx, _ny, gx, gy);
        float high = (float)GenGrid.Bilinear(_high, _nx, _ny, gx, gy);
        float span = Math.Max(MinSpanDm, high - low);
        return Math.Clamp((heightDm - low) / span, 0f, 1f);
    }
}
