namespace BlueHarvest.Sim.World.Generation;

/// <summary>
/// Orographic rain from the trade winds: moisture is swept downwind across the grid and wrung out
/// wherever the ground rises; the sea recharges it. A weaker sweep from the opposite quarter stands
/// in for winter Kona storms, which wet the leeward slopes.
/// </summary>
public static class RainModel
{
    // Upwind offsets toward N, NE, E, ... (y down).
    private static readonly int[] Ux = [0, 1, 1, 1, 0, -1, -1, -1];
    private static readonly int[] Uy = [-1, -1, 0, 1, 1, 1, 0, -1];

    /// <summary>Mean annual rain in mm for every cell.</summary>
    public static double[] Compute(double[] h, byte[] land, int nx, int ny, double cell, double windDeg, double mult)
    {
        int n = nx * ny;
        var output = new double[n];
        var m = new double[n];

        void Sweep(int k, double weight)
        {
            if (weight <= 0) return;
            int ox = Ux[k], oy = Uy[k];
            double step = cell * (ox != 0 && oy != 0 ? GenMath.Sqrt2 : 1);
            double recharge = 1 - GenMath.Exp(-step / 15000);
            int ys = oy > 0 ? ny - 1 : 0, ye = oy > 0 ? -1 : ny, yd = oy > 0 ? -1 : 1;
            int xs = ox > 0 ? nx - 1 : 0, xe = ox > 0 ? -1 : nx, xd = ox > 0 ? -1 : 1;
            for (int y = ys; y != ye; y += yd)
            {
                for (int x = xs; x != xe; x += xd)
                {
                    int i = y * nx + x, ux = x + ox, uy = y + oy;
                    double mu = 1, hu = 0;
                    if (ux >= 0 && uy >= 0 && ux < nx && uy < ny)
                    {
                        int u = uy * nx + ux;
                        mu = m[u];
                        hu = land[u] != 0 ? Math.Max(0, h[u]) : 0;
                    }
                    double r;
                    if (land[i] == 0)
                    {
                        m[i] = mu + (1 - mu) * recharge;
                        r = 250 + 450 * mu;
                    }
                    else
                    {
                        double hi = Math.Max(0, h[i]), lift = hi - hu;
                        double ro = lift > 0 ? mu * Math.Min(0.6, lift / 1700) : 0;
                        m[i] = Math.Max(0.02, mu - ro);
                        r = 220 + 650 * mu + (ro / step) * 4.2e7;
                    }
                    output[i] += weight * r;
                }
            }
        }

        void From(double deg, double weight)
        {
            double a = ((deg % 360) + 360) % 360;
            int k0 = (int)Math.Floor(a / 45) % 8, k1 = (k0 + 1) % 8;
            double w1 = (a - k0 * 45) / 45;
            Sweep(k0, weight * (1 - w1));
            Sweep(k1, weight * w1);
        }

        From(windDeg, 1);
        From(windDeg + 150, 0.2);
        GenGrid.BoxBlur(output, nx, ny, Math.Max(1, (int)GenMath.Round(700 / cell)), 2);
        for (int i = 0; i < n; i++) output[i] *= mult;
        return output;
    }
}
