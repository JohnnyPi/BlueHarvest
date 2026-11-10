namespace BlueHarvest.Sim.World.Generation;

/// <summary>A polyline vertex: position in metres, plus discharge (or grade) and height.</summary>
public readonly record struct PolyPoint(double X, double Y, double Q, double H);

/// <summary>
/// Helpers for the generator's row-major grids (index = y * nx + x). Masks are byte arrays with 1 for set.
/// </summary>
public static class GenGrid
{
    /// <summary>The eight neighbour offsets, east first and clockwise in screen space (y down).</summary>
    public static readonly int[] DX = [1, 1, 0, -1, -1, -1, 0, 1];
    public static readonly int[] DY = [0, 1, 1, 1, 0, -1, -1, -1];
    /// <summary>Step length of each neighbour in cells.</summary>
    public static readonly double[] DL = [1, GenMath.Sqrt2, 1, GenMath.Sqrt2, 1, GenMath.Sqrt2, 1, GenMath.Sqrt2];

    /// <summary>
    /// Chamfer distance in metres from every cell with src = 1 to the nearest cell with src = 0
    /// (two passes, weights cell and cell·√2). Stored as floats, like the reference.
    /// </summary>
    public static float[] Chamfer(byte[] src, int nx, int ny, double cell)
    {
        int n = nx * ny;
        var d = new float[n];
        double a = cell, b = cell * GenMath.Sqrt2;
        for (int i = 0; i < n; i++) d[i] = src[i] != 0 ? 1e9f : 0f;
        for (int y = 0; y < ny; y++)
        {
            for (int x = 0; x < nx; x++)
            {
                int i = y * nx + x;
                double v = d[i];
                if (v == 0) continue;
                if (x > 0) v = Math.Min(v, d[i - 1] + a);
                if (y > 0)
                {
                    v = Math.Min(v, d[i - nx] + a);
                    if (x > 0) v = Math.Min(v, d[i - nx - 1] + b);
                    if (x < nx - 1) v = Math.Min(v, d[i - nx + 1] + b);
                }
                d[i] = (float)v;
            }
        }
        for (int y = ny - 1; y >= 0; y--)
        {
            for (int x = nx - 1; x >= 0; x--)
            {
                int i = y * nx + x;
                double v = d[i];
                if (v == 0) continue;
                if (x < nx - 1) v = Math.Min(v, d[i + 1] + a);
                if (y < ny - 1)
                {
                    v = Math.Min(v, d[i + nx] + a);
                    if (x < nx - 1) v = Math.Min(v, d[i + nx + 1] + b);
                    if (x > 0) v = Math.Min(v, d[i + nx - 1] + b);
                }
                d[i] = (float)v;
            }
        }
        return d;
    }

    /// <summary>Separable box blur of radius r in place, repeated; edges average only the cells inside.</summary>
    public static void BoxBlur(double[] f, int nx, int ny, int r, int passes)
    {
        var tmp = new double[f.Length];
        for (int p = 0; p < passes; p++)
        {
            for (int y = 0; y < ny; y++)
            {
                int row = y * nx;
                double acc = 0;
                int cnt = 0;
                for (int x = -r; x < nx; x++)
                {
                    int add = x + r, rem = x - r - 1;
                    if (add < nx) { acc += f[row + add]; cnt++; }
                    if (rem >= 0) { acc -= f[row + rem]; cnt--; }
                    if (x >= 0) tmp[row + x] = acc / cnt;
                }
            }
            for (int x = 0; x < nx; x++)
            {
                double acc = 0;
                int cnt = 0;
                for (int y = -r; y < ny; y++)
                {
                    int add = y + r, rem = y - r - 1;
                    if (add < ny) { acc += tmp[add * nx + x]; cnt++; }
                    if (rem >= 0) { acc -= tmp[rem * nx + x]; cnt--; }
                    if (y >= 0) f[y * nx + x] = acc / cnt;
                }
            }
        }
    }

    /// <summary>
    /// Labels the 8-connected components of the mask (−1 where unset), numbered in scan order,
    /// and returns each component's size.
    /// </summary>
    public static List<int> Components(byte[] mask, int nx, int ny, int[] label)
    {
        int n = nx * ny;
        Array.Fill(label, -1);
        var sizes = new List<int>();
        var q = new int[n];
        for (int s0 = 0; s0 < n; s0++)
        {
            if (mask[s0] == 0 || label[s0] >= 0) continue;
            int id = sizes.Count, qh = 0, qt = 0, size = 0;
            q[qt++] = s0; label[s0] = id;
            while (qh < qt)
            {
                int c = q[qh++]; size++;
                int x = c % nx, y = c / nx;
                for (int k = 0; k < 8; k++)
                {
                    int xx = x + DX[k], yy = y + DY[k];
                    if (xx < 0 || yy < 0 || xx >= nx || yy >= ny) continue;
                    int j = yy * nx + xx;
                    if (mask[j] != 0 && label[j] < 0) { label[j] = id; q[qt++] = j; }
                }
            }
            sizes.Add(size);
        }
        return sizes;
    }

    /// <summary>Chaikin corner cutting; the end points stay where they are. Lines under 3 points are returned as given.</summary>
    public static List<PolyPoint> Chaikin(List<PolyPoint> pts, int iterations)
    {
        for (int it = 0; it < iterations; it++)
        {
            if (pts.Count < 3) return pts;
            var output = new List<PolyPoint>(pts.Count * 2) { pts[0] };
            for (int k = 0; k + 1 < pts.Count; k++)
            {
                PolyPoint a = pts[k], b = pts[k + 1];
                if (k > 0) output.Add(Lerp(a, b, 0.75, 0.25));
                if (k + 1 < pts.Count - 1) output.Add(Lerp(a, b, 0.25, 0.75));
            }
            output.Add(pts[^1]);
            pts = output;
        }
        return pts;
    }

    private static PolyPoint Lerp(PolyPoint a, PolyPoint b, double wa, double wb) =>
        new(a.X * wa + b.X * wb, a.Y * wa + b.Y * wb, a.Q * wa + b.Q * wb, a.H * wa + b.H * wb);
}
