namespace BlueHarvest.Sim.World.Generation;

/// <summary>
/// Addressed gradient noise. A value depends only on (seed, x, y), never on what was sampled before,
/// so the coarse island grid and every streamed chunk read the same field.
/// </summary>
public static class GenNoise
{
    private static readonly double[] Gx = new double[16];
    private static readonly double[] Gy = new double[16];

    static GenNoise()
    {
        for (int i = 0; i < 16; i++)
        {
            double a = (i + 0.5) * GenMath.Pi / 8;
            Gx[i] = GenMath.Cos(a);
            Gy[i] = GenMath.Sin(a);
        }
    }

    /// <summary>Gradient noise with a quintic fade, roughly in [−1, 1].</summary>
    public static double Noise(uint s, double x, double y)
    {
        double fix = Math.Floor(x), fiy = Math.Floor(y);
        int ix = (int)fix, iy = (int)fiy;
        double fx = x - fix, fy = y - fiy;
        double u = fx * fx * fx * (fx * (fx * 6 - 15) + 10), v = fy * fy * fy * (fy * (fy * 6 - 15) + 10);
        uint h = GenHash.Hash2(s, ix, iy) & 15; double a = Gx[h] * fx + Gy[h] * fy;
        h = GenHash.Hash2(s, ix + 1, iy) & 15; double b = Gx[h] * (fx - 1) + Gy[h] * fy;
        h = GenHash.Hash2(s, ix, iy + 1) & 15; double c = Gx[h] * fx + Gy[h] * (fy - 1);
        h = GenHash.Hash2(s, ix + 1, iy + 1) & 15; double d = Gx[h] * (fx - 1) + Gy[h] * (fy - 1);
        double ab = a + u * (b - a), cd = c + u * (d - c);
        return (ab + v * (cd - ab)) * 1.41;
    }

    /// <summary>Fractal sum of octaves; each octave has its own seed and a shifted, ×2.03 frequency.</summary>
    public static double Fbm(uint s, double x, double y, int octaves)
    {
        double sum = 0, amp = 1, norm = 0;
        for (int o = 0; o < octaves; o++)
        {
            sum += amp * Noise(unchecked(s + (uint)(o + 1) * 0x9E3779B1u), x, y);
            norm += amp; amp *= 0.5; x = x * 2.03 + 17.1; y = y * 2.03 - 9.7;
        }
        return sum / norm;
    }

    /// <summary>Ridged multifractal in [0, 1]: sharp crests where the noise crosses zero.</summary>
    public static double Ridged(uint s, double x, double y, int octaves)
    {
        double sum = 0, amp = 1, norm = 0, w = 1;
        for (int o = 0; o < octaves; o++)
        {
            double n = 1 - Math.Abs(Noise(unchecked(s + (uint)(o + 1) * 0x85EBCA6Bu), x, y));
            n *= n; n *= w; w = Math.Min(1, n * 2);
            sum += n * amp; norm += amp; amp *= 0.5; x = x * 2.03 + 3.3; y = y * 2.03 + 7.9;
        }
        return sum / norm;
    }
}
