#nullable enable
using System.Collections.Generic;
using BlueHarvest.Sim.Injuries;

namespace BlueHarvest.Game.Ui;

/// <summary>How worried your exposure should make you: nothing, a feeling of being watched, or found.</summary>
public enum Alarm
{
    Calm,
    Close,
    Spotted,
}

/// <summary>One exit as the sidebar lists it.</summary>
public readonly record struct ExitFact(string Name, int Distance, string Compass, string Arrow);

/// <summary>
/// Everything the sidebar shows, worked out by Main after each turn. Text fields may hold BBCode
/// unless they say otherwise; the sidebar only lays them out.
/// </summary>
public sealed record HudFacts(
    string Clock,
    string WeatherBadge,
    string Weather,
    string ExposureVerdict,
    Alarm ExposureAlarm,
    string Exposure,
    string ExposureDetail,
    string Sight,
    string Sound,
    string Smell,
    string Feel,
    Health Health,
    string BodyNotes,
    IReadOnlyList<ExitFact> Exits,
    string Gear,
    string People);
