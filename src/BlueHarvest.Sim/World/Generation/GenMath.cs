namespace BlueHarvest.Sim.World.Generation;

/// <summary>
/// Portable maths for world generation. Math.Exp, Math.Sin and friends may differ in the last bit
/// between runtimes and CPUs, so the generator never calls them. Everything here uses only
/// + − × ÷, Math.Sqrt and Math.Floor, which IEEE 754 makes identical everywhere, in exactly the
/// same order as the island lab's reference generator (gen.js, "D"). Do not "simplify" the
/// expressions: reordering an addition changes the last bit, and the golden-hash tests will fail.
/// </summary>
public static class GenMath
{
    public const double Pi = 3.141592653589793;
    public const double Sqrt2 = 1.4142135623730951;

    private const double Ln2Hi = 0.6931471803691238;
    private const double Ln2Lo = 1.9082149292705877e-10;
    private const double Ln2 = 0.6931471805599453;
    private const double TwoPi = 6.283185307179586;
    private const double HalfPi = 1.5707963267948966;

    /// <summary>JavaScript's Math.round: halves round up (−2.5 → −2).</summary>
    public static double Round(double x) => Math.Floor(x + 0.5);

    public static double Hyp(double x, double y) => Math.Sqrt(x * x + y * y);

    private static double Scale2(double v, int k)
    {
        if (k > 0)
        {
            while (k > 0) { v *= 2; k--; }
        }
        else
        {
            while (k < 0) { v *= 0.5; k++; }
        }
        return v;
    }

    public static double Exp(double x)
    {
        if (x > 700) return double.PositiveInfinity;
        if (x < -700) return 0;
        double k = Math.Floor(x / Ln2 + 0.5);
        double r = (x - k * Ln2Hi) - k * Ln2Lo;
        double t = 1;
        for (int i = 13; i >= 1; i--) t = 1 + r * t / i;
        return Scale2(t, (int)k);
    }

    public static double Log(double x)
    {
        if (!(x > 0)) return x == 0 ? double.NegativeInfinity : double.NaN;
        int e = 0;
        while (x >= 2) { x *= 0.5; e++; }
        while (x < 1) { x *= 2; e--; }
        if (x > 1.4142135623730951) { x *= 0.5; e++; }
        double s = (x - 1) / (x + 1), s2 = s * s;
        double sum = 0;
        for (int i = 41; i >= 1; i -= 2) sum = 1.0 / i + s2 * sum;
        return 2 * s * sum + e * Ln2;
    }

    public static double Pow(double a, double b)
    {
        if (a <= 0) return a == 0 ? (b == 0 ? 1 : 0) : double.NaN;
        if (b == 0.5) return Math.Sqrt(a);
        if (b == 1.5) return a * Math.Sqrt(a);
        return Exp(b * Log(a));
    }

    public static double Tanh(double x)
    {
        if (x > 20) return 1;
        if (x < -20) return -1;
        double e2 = Exp(2 * x);
        return (e2 - 1) / (e2 + 1);
    }

    public static double Sin(double x)
    {
        x = x - TwoPi * Math.Floor(x / TwoPi + 0.5);
        if (x > HalfPi) x = Pi - x;
        else if (x < -HalfPi) x = -Pi - x;
        double x2 = x * x;
        double t = 1;
        for (int i = 23; i >= 3; i -= 2) t = 1 - x2 * t / (double)(i * (i - 1));
        return x * t;
    }

    public static double Cos(double x) => Sin(x + HalfPi);

    public static double Atan(double x)
    {
        if (x < 0) return -Atan(-x);
        bool off = false;
        if (x > 1) { x = 1 / x; off = true; }
        double h1 = x / (1 + Math.Sqrt(1 + x * x));
        double h2 = h1 / (1 + Math.Sqrt(1 + h1 * h1));
        double z2 = h2 * h2;
        double sum = 0;
        for (int i = 31; i >= 1; i -= 2) sum = 1.0 / i - z2 * sum;
        double a = 4 * h2 * sum;
        return off ? HalfPi - a : a;
    }

    public static double Atan2(double y, double x)
    {
        if (x > 0) return Atan(y / x);
        if (x < 0) return y >= 0 ? Atan(y / x) + Pi : Atan(y / x) - Pi;
        return y > 0 ? HalfPi : y < 0 ? -HalfPi : 0;
    }

    public static double Clamp(double v, double a, double b) => v < a ? a : v > b ? b : v;

    /// <summary>Smooth maximum: max(a, b) with the corner rounded over a width of k.</summary>
    public static double SMax(double a, double b, double k)
    {
        double h = Math.Max(k - Math.Abs(a - b), 0) / k;
        return Math.Max(a, b) + h * h * k * 0.25;
    }

    public static double SmoothStep(double a, double b, double v)
    {
        double t = Clamp((v - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }
}
