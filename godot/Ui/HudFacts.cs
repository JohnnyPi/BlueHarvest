#nullable enable
using System.Collections.Generic;
using BlueHarvest.Sim.Injuries;

namespace BlueHarvest.Game.Ui;

/// <summary>How worried the Rex's sight score should make you: well under, close to, or at its threshold.</summary>
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
    int SightScore,
    int SightThreshold,
    string SightVerdict,
    Alarm SightAlarm,
    string SightDetail,
    string See,
    string Hear,
    string Feel,
    Health Health,
    string BodyNotes,
    IReadOnlyList<ExitFact> Exits,
    string Gear,
    string People);
