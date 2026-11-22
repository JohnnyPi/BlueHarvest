#nullable enable
namespace BlueHarvest.Content;

/// <summary>
/// A mechanic implemented (or to be implemented) in C#, named in content by its <see cref="Key"/>. Content can
/// only name handlers; it can never define one. A contract that isn't <see cref="Implemented"/> yet makes the
/// definitions that name it authoring-valid but not runtime-ready.
/// </summary>
/// <param name="Key">The handler key, dotted lower case: <c>process.transform</c>.</param>
/// <param name="Kind">The one definition kind it applies to (<see cref="ContentKinds"/>).</param>
/// <param name="Implemented">Whether a C# executor exists and is registered.</param>
/// <param name="Summary">One line for people reading diagnostics.</param>
/// <param name="Check">The handler's data contract: returns a problem with the definition, or null.</param>
public sealed record HandlerContract(
    string Key, string Kind, bool Implemented, string Summary, Func<ContentDefinition, string?>? Check = null);
