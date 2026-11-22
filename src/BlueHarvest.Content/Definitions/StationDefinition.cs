#nullable enable
namespace BlueHarvest.Content;

/// <summary><c>kind: station</c>. A kind of place a recipe is worked at (<c>STN-HAND</c> is your own hands).</summary>
public sealed record StationDefinition : ContentDefinition
{
    public override string Kind => ContentKinds.Station;

    internal override void WriteFields(CanonicalWriter w) { }
}
