namespace BlueHarvest.Sim.Core;

/// <summary>
/// Stable 64-bit FNV-1a hash for fingerprinting simulation state.
/// Unlike string.GetHashCode, the result is the same on every run and every machine.
/// </summary>
public sealed class StateHasher
{
    private const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    private ulong _hash = OffsetBasis;

    public ulong Value => _hash;

    public StateHasher Add(ulong value)
    {
        for (int i = 0; i < 8; i++)
        {
            _hash ^= (byte)(value >> (i * 8));
            _hash *= Prime;
        }
        return this;
    }

    public StateHasher Add(long value) => Add(unchecked((ulong)value));

    public StateHasher Add(int value) => Add((long)value);

    public StateHasher Add(string value)
    {
        Add(value.Length);
        foreach (char c in value)
            Add((ulong)c);
        return this;
    }

    public static ulong HashString(string value) => new StateHasher().Add(value).Value;
}
