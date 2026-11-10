namespace BlueHarvest.Sim.World.Generation;

/// <summary>
/// Binary min-heap of (key, cell) with a fixed capacity. The order in which equal keys come out
/// is part of the generator's output (flood fill, paths), so the tie rules here must match gen.js.
/// </summary>
public sealed class GenHeap
{
    private readonly double[] _keys;
    private readonly int[] _values;

    public GenHeap(int capacity)
    {
        _keys = new double[capacity + 1];
        _values = new int[capacity + 1];
    }

    public int Count { get; private set; }

    public void Clear() => Count = 0;

    public void Push(double key, int value)
    {
        int i = ++Count;
        while (i > 1)
        {
            int p = i >> 1;
            if (_keys[p] <= key) break;
            _keys[i] = _keys[p]; _values[i] = _values[p]; i = p;
        }
        _keys[i] = key; _values[i] = value;
    }

    /// <summary>Removes and returns the value with the smallest key. The heap must not be empty.</summary>
    public int Pop()
    {
        int top = _values[1];
        double lk = _keys[Count];
        int lv = _values[Count];
        int n = --Count;
        int i = 1;
        while (true)
        {
            int c = i << 1;
            if (c > n) break;
            if (c < n && _keys[c + 1] < _keys[c]) c++;
            if (_keys[c] >= lk) break;
            _keys[i] = _keys[c]; _values[i] = _values[c]; i = c;
        }
        _keys[i] = lk; _values[i] = lv;
        return top;
    }
}
