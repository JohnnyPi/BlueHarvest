#nullable enable
using System.Text;

namespace BlueHarvest.Content;

/// <summary>
/// One file from a content source. <see cref="Path"/> is relative to the source root with forward slashes
/// (<c>core/items/vine-fiber.yaml</c>). The bytes are read only when the loader needs them, after it has checked
/// <see cref="Length"/> against its size limit.
/// </summary>
public sealed class ContentFile
{
    readonly Func<byte[]> read;

    public ContentFile(string path, long length, Func<byte[]> read)
    {
        Path = path;
        Length = length;
        this.read = read;
    }

    public string Path { get; }
    public long Length { get; }

    public byte[] ReadAllBytes() => read();

    public static ContentFile FromBytes(string path, byte[] bytes) => new(path, bytes.Length, () => bytes);

    public static ContentFile FromText(string path, string text) => FromBytes(path, Encoding.UTF8.GetBytes(text));
}
