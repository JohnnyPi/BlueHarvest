#nullable enable
namespace BlueHarvest.Content;

public interface IContentLoader
{
    ContentLoadResult Load(IContentSource source, ContentLoadOptions options);
}
