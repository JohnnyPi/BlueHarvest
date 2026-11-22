#nullable enable
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace BlueHarvest.Content;

/// <summary>
/// Feeds typed values into a SHA-256, each with a type byte and, for text, a length, so no two different
/// sequences of values can hash the same bytes. Definitions write their meaning through this, never their YAML,
/// so whitespace, quoting, key order, flow or block lists and file layout don't change a fingerprint.
/// </summary>
internal sealed class CanonicalWriter : IDisposable
{
    const byte NullTag = 0, TextTag = 1, NumberTag = 2, FlagTag = 3, CountTag = 4;

    readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    public void Text(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> head = stackalloc byte[5];
        head[0] = TextTag;
        BinaryPrimitives.WriteInt32LittleEndian(head[1..], bytes.Length);
        hash.AppendData(head);
        hash.AppendData(bytes);
    }

    public void Number(long value)
    {
        Span<byte> buffer = stackalloc byte[9];
        buffer[0] = NumberTag;
        BinaryPrimitives.WriteInt64LittleEndian(buffer[1..], value);
        hash.AppendData(buffer);
    }

    public void Flag(bool value)
    {
        Span<byte> buffer = stackalloc byte[2];
        buffer[0] = FlagTag;
        buffer[1] = value ? (byte)1 : (byte)0;
        hash.AppendData(buffer);
    }

    public void Count(int count)
    {
        Span<byte> buffer = stackalloc byte[5];
        buffer[0] = CountTag;
        BinaryPrimitives.WriteInt32LittleEndian(buffer[1..], count);
        hash.AppendData(buffer);
    }

    public void Nothing()
    {
        Span<byte> buffer = stackalloc byte[1];
        buffer[0] = NullTag;
        hash.AppendData(buffer);
    }

    /// <summary>Text that may be absent: absent and empty hash differently.</summary>
    public void OptionalText(string? value)
    {
        if (value is null)
            Nothing();
        else
            Text(value);
    }

    public void Field(string name, string value)
    {
        Text(name);
        Text(value);
    }

    public void Field(string name, long value)
    {
        Text(name);
        Number(value);
    }

    public void Field(string name, bool value)
    {
        Text(name);
        Flag(value);
    }

    public void Field(string name, ContentId? value)
    {
        Text(name);
        if (value is { } id)
            Text(id.Value);
        else
            Nothing();
    }

    /// <summary>A list whose order means something (dialogue nodes, recipe inputs as shown).</summary>
    public void Field(string name, IReadOnlyList<string> values)
    {
        Text(name);
        Count(values.Count);
        foreach (string value in values)
            Text(value);
    }

    /// <summary>A list that's really a set (tags, handler keys): sorted first, so its order doesn't count.</summary>
    public void SetField(string name, IReadOnlyList<string> values)
    {
        Field(name, values.OrderBy(v => v, StringComparer.Ordinal).ToList());
    }

    public string Finish() => "sha256:" + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();

    public void Dispose() => hash.Dispose();
}
