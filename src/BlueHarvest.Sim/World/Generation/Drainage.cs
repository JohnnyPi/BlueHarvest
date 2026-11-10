namespace BlueHarvest.Sim.World.Generation;

/// <summary>
/// The drainage tree of a land grid: each land cell's steepest-descent receiver, and a stack that
/// lists every cell after the cell it drains into (base to top, Braun &amp; Willett 2013), so flow
/// can be accumulated in one reverse pass. Buffers are reused between erosion iterations.
/// </summary>
public sealed class DrainageTree
{
    /// <summary>Receiver marking a cell that drains straight to the sea (or a dead end).</summary>
    public const int Outlet = -1;
    /// <summary>Receiver marking a sea cell.</summary>
    public const int NotLand = -2;

    public DrainageTree(int nx, int ny, double cell)
    {
        Nx = nx; Ny = ny; N = nx * ny; Cell = cell;
        Receiver = new int[N];
        ReceiverDistance = new float[N];
        Mouth = new int[N];
        DonorStart = new int[N + 1];
        Donors = new int[N];
        Stack = new int[N];
        _fill = new int[N + 1];
        _dfs = new int[N];
    }

    public int Nx { get; }
    public int Ny { get; }
    public int N { get; }
    public double Cell { get; }

    /// <summary>The cell each cell drains into, or <see cref="Outlet"/> / <see cref="NotLand"/>.</summary>
    public int[] Receiver { get; }
    /// <summary>Metres to the receiver (or to the sea cell, for outlets).</summary>
    public float[] ReceiverDistance { get; }
    /// <summary>For outlets: the sea cell the water enters, or −1. Stale elsewhere.</summary>
    public int[] Mouth { get; }
    /// <summary>Donors of cell c are Donors[DonorStart[c] .. DonorStart[c + 1]).</summary>
    public int[] DonorStart { get; }
    public int[] Donors { get; }
    /// <summary>Land cells, each after its receiver. The first <see cref="StackCount"/> entries are valid.</summary>
    public int[] Stack { get; }
    public int StackCount { get; private set; }

    private readonly int[] _fill;
    private readonly int[] _dfs;

    /// <summary>Recomputes receivers, donors and the stack from a depression-free surface.</summary>
    public void Build(double[] filled, byte[] land)
    {
        int nx = Nx, ny = Ny, n = N;
        double cell = Cell;
        int[] cnt = DonorStart;
        Array.Clear(cnt);
        for (int i = 0; i < n; i++)
        {
            Receiver[i] = NotLand;
            if (land[i] == 0) continue;
            int x = i % nx, y = i / nx;
            int best = -1, bo = -1;
            double bs = 0, bd = cell, bos = -1e30;
            for (int k = 0; k < 8; k++)
            {
                int xx = x + GenGrid.DX[k], yy = y + GenGrid.DY[k];
                if (xx < 0 || yy < 0 || xx >= nx || yy >= ny) continue;
                int j = yy * nx + xx;
                double dl = GenGrid.DL[k] * cell;
                if (land[j] == 0)
                {
                    double sea = filled[i] / dl;
                    if (sea > bos) { bos = sea; bo = j; }
                    continue;
                }
                double sl = (filled[i] - filled[j]) / dl;
                if (sl > bs) { bs = sl; best = j; bd = dl; }
            }
            if (bo >= 0 && bos >= bs)
            {
                Receiver[i] = Outlet; Mouth[i] = bo;
                ReceiverDistance[i] = (float)(GenMath.Hyp(bo % nx - x, bo / nx - y) * cell);
            }
            else if (best >= 0)
            {
                Receiver[i] = best; ReceiverDistance[i] = (float)bd; cnt[best + 1]++;
            }
            else
            {
                Receiver[i] = Outlet; Mouth[i] = -1; ReceiverDistance[i] = (float)cell; // not after flooding
            }
        }
        for (int i = 0; i < n; i++) cnt[i + 1] += cnt[i];
        Array.Copy(cnt, _fill, n + 1);
        for (int i = 0; i < n; i++)
        {
            int r = Receiver[i];
            if (r >= 0) Donors[_fill[r]++] = i;
        }
        int ns = 0;
        for (int i = 0; i < n; i++)
        {
            if (Receiver[i] != Outlet) continue;
            int top = 0;
            _dfs[top++] = i;
            while (top > 0)
            {
                int c = _dfs[--top];
                Stack[ns++] = c;
                for (int q = cnt[c]; q < cnt[c + 1]; q++) _dfs[top++] = Donors[q];
            }
        }
        StackCount = ns;
    }
}

/// <summary>Priority-flood with epsilon (Barnes et al. 2014): fills depressions so every land cell drains to the sea.</summary>
public static class Flood
{
    public const double Epsilon = 1e-4;

    /// <summary>
    /// Writes the filled surface to <paramref name="filled"/> (land cells only). Cells on the coast or
    /// the grid edge are the outlets. closed, heap (capacity ≥ land cells) and pit are scratch buffers.
    /// </summary>
    public static void Fill(double[] h, byte[] land, int nx, int ny, double[] filled, byte[] closed, GenHeap heap, int[] pit)
    {
        int n = nx * ny;
        Array.Clear(closed);
        heap.Clear();
        for (int i = 0; i < n; i++)
        {
            if (land[i] == 0) continue;
            int x = i % nx, y = i / nx;
            bool coast = false;
            for (int k = 0; k < 8; k++)
            {
                int xx = x + GenGrid.DX[k], yy = y + GenGrid.DY[k];
                if (xx < 0 || yy < 0 || xx >= nx || yy >= ny || land[yy * nx + xx] == 0) { coast = true; break; }
            }
            if (coast) { filled[i] = h[i]; closed[i] = 1; heap.Push(h[i], i); }
        }
        int ph = 0, pt = 0;
        while (heap.Count > 0 || ph < pt)
        {
            int c;
            if (ph < pt) c = pit[ph++];
            else { ph = pt = 0; c = heap.Pop(); }
            int x = c % nx, y = c / nx;
            double fc = filled[c] + Epsilon;
            for (int k = 0; k < 8; k++)
            {
                int xx = x + GenGrid.DX[k], yy = y + GenGrid.DY[k];
                if (xx < 0 || yy < 0 || xx >= nx || yy >= ny) continue;
                int j = yy * nx + xx;
                if (land[j] == 0 || closed[j] != 0) continue;
                closed[j] = 1;
                if (h[j] <= fc) { filled[j] = fc; pit[pt++] = j; }
                else { filled[j] = h[j]; heap.Push(h[j], j); }
            }
        }
    }
}
