#nullable enable
using System.Collections.Generic;
using BlueHarvest.Game.Ui;
using BlueHarvest.Sim;
using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.World;
using Direction = BlueHarvest.Sim.World.Direction;
using BlueHarvest.Sim.ContentIntegration;

namespace BlueHarvest.Game;

/// <summary>Hunger, thirst, sleep, pain, breath, warmth and sickness in the log, the sidebar and the menu; drinking from streams.</summary>
public partial class Main
{
    private static readonly NeedKind[] ShownNeeds =
        [NeedKind.Thirst, NeedKind.Hunger, NeedKind.Sleep, NeedKind.Warmth, NeedKind.Sickness, NeedKind.Pain, NeedKind.Stamina];

    private void SubscribeToNeedEvents()
    {
        _sim.NeedChanged += OnNeedChanged;
        _sim.FellAsleep += OnFellAsleep;
        _sim.WokeUp += OnWokeUp;
        _sim.NeedBlackedOut += OnNeedBlackedOut;
        _sim.DrankWater += OnDrankWater;
        _sim.WaterFilled += OnWaterFilled;
    }

    private void OnDrankWater(Actor actor, TilePos at)
    {
        if (actor == _playerActor)
            Say("You cup your hands and drink. It tastes of earth; there's no telling what's in it.");
    }

    private void OnWaterFilled(Actor actor, Item item)
    {
        if (actor == _playerActor)
            Say($"You fill the {item.Def.Name}. Untreated: a water tablet would make it safe.");
    }

    private void OnNeedChanged(Actor actor, NeedKind kind, int stage, int was)
    {
        if (actor != _playerActor)
            return;
        string? line = stage > was ? Worse(kind, stage) : Better(kind, stage, was);
        if (line is not null)
            Say(line);
    }

    private static string? Worse(NeedKind kind, int stage) => (kind, stage) switch
    {
        (NeedKind.Hunger, 1) => "Your stomach growls. You're hungry.",
        (NeedKind.Hunger, 2) => "Hunger gnaws at you: you're getting weak.",
        (NeedKind.Hunger, _) => "You're starving. Every step is an effort.",
        (NeedKind.Thirst, 1) => "Your mouth is dry. You're thirsty.",
        (NeedKind.Thirst, 2) => "Your throat is parched: you need water.",
        (NeedKind.Thirst, 3) => "Your head pounds: you're dehydrated. Find water.",
        (NeedKind.Thirst, _) => "You're dying of thirst.",
        (NeedKind.Sleep, 1) => "You're getting tired.",
        (NeedKind.Sleep, 2) => "You're exhausted: you need to sleep.",
        (NeedKind.Sleep, 3) => "You're dead on your feet. Your eyes keep closing.",
        (NeedKind.Sleep, _) => "You can barely stand. Sleep soon, or you'll drop.",
        (NeedKind.Pain, 3) => "The pain is overwhelming.",
        (NeedKind.Warmth, 1) => "You're getting chilly.",
        (NeedKind.Warmth, 2) => "You're cold. Get dry, get out of the wind, find a fire.",
        (NeedKind.Warmth, 3) => "You're shivering hard: your hands are clumsy.",
        (NeedKind.Warmth, _) => "You're hypothermic. Get warm, now.",
        (NeedKind.Sickness, 1) => "Your stomach turns. Something isn't right.",
        (NeedKind.Sickness, 2) => "You feel sick.",
        (NeedKind.Sickness, 3) => "You're burning with fever.",
        (NeedKind.Sickness, _) => "You're gravely ill. You need antibiotics.",
        (NeedKind.Stamina, 2) => "You're out of breath: no more sprinting until you get it back.",
        _ => null, // aching and hurting come with the wound's own line; winded is in the sidebar
    };

    private string? Better(NeedKind kind, int stage, int was) => (kind, stage) switch
    {
        (NeedKind.Hunger, 0) => "You've eaten enough.",
        (NeedKind.Thirst, 0) => "Your thirst is gone.",
        (NeedKind.Sleep, 0) => null, // waking rested says so
        (NeedKind.Warmth, 0) => "You're warm again.",
        (NeedKind.Sickness, 0) => "You feel well again.",
        (NeedKind.Pain, _) when _playerActor.Needs is { } needs && needs.Relief(_sim.CurrentTick) > 0 && was >= 2 =>
            stage == 0 ? "The pain fades to nothing." : "The pain dulls.",
        (NeedKind.Pain, 0) => "The pain fades.",
        (NeedKind.Stamina, 0) when was == 2 => "You've got your breath back.",
        (NeedKind.Stamina, _) => null,
        (NeedKind.Pain, _) => null,
        (_, _) => $"You feel a little better: {NeedRules.Word(kind, stage).ToLowerInvariant()} now.",
    };

    private void OnFellAsleep(Actor actor)
    {
        if (actor == _playerActor)
            Say("You drift off.");
    }

    private void OnWokeUp(Actor actor, string why)
    {
        if (actor != _playerActor)
            return;
        string line = why switch
        {
            "rested" => "You wake, rested.",
            "pain" => "Pain jolts you awake!",
            "thirst" => "You wake with a raging thirst.",
            "hunger" => "Hunger wakes you.",
            "cold" => "The cold wakes you, shivering.",
            _ when why.StartsWith("a noise: ") => $"You wake with a start: {why["a noise: ".Length..]}.",
            _ => "You wake.",
        };
        if (_rest is not null)
            StopRest(line);
        else
            Say(line);
    }

    private void OnNeedBlackedOut(Actor actor, NeedKind kind)
    {
        if (actor != _playerActor)
            return;
        Say(kind switch
        {
            NeedKind.Sleep => "Your eyes close by themselves... you jerk awake moments later.",
            NeedKind.Thirst => "Your vision greys and you sway: the thirst is getting to you.",
            NeedKind.Warmth => "The cold takes you for a moment: you come to, numb.",
            _ => "A wave of fever: the world swims and you lose a few seconds.",
        });
    }

    /// <summary>The sidebar's Condition lines: asleep, then each need that has something to say, then breath.</summary>
    private string ConditionText()
    {
        if (_playerActor.Needs is not { } needs)
            return "";
        long now = _sim.CurrentTick;
        var lines = new List<string>();
        if (needs.Asleep)
            lines.Add($"[color={HudTheme.Hex(HudTheme.Muted)}]Asleep[/color]");
        foreach (var kind in ShownNeeds)
        {
            int stage = _sim.NeedStage(_playerActor, kind);
            if (stage == 0)
                continue;
            int top = NeedRules.TopStage(kind);
            var color = stage >= top || kind != NeedKind.Stamina && stage >= 3 ? HudTheme.Danger : HudTheme.Amber;
            string extra = kind == NeedKind.Pain && needs.OnAdrenaline(now) ? " (adrenaline)"
                : kind == NeedKind.Pain && needs.OnPainkillers(now) ? " (dulled)"
                : "";
            lines.Add($"[color={HudTheme.Hex(color)}]{NeedRules.Word(kind, stage)}{extra}[/color]");
        }
        if (needs.OnPainkillers(now) && _sim.NeedStage(_playerActor, NeedKind.Pain) == 0 && Needs.WoundPain(_playerActor.Health) > 0)
            lines.Add($"[color={HudTheme.Hex(HudTheme.Muted)}]Painkillers working[/color]");
        if (lines.Count == 0)
            lines.Add($"[color={HudTheme.Hex(HudTheme.Safe)}]Fed, watered and rested[/color]");
        if (_sim.DirtyWounds(_playerActor) > 0)
            lines.Add($"[color={HudTheme.Hex(HudTheme.Danger)}]A wound is going bad: clean it[/color]");
        else if (_sim.UncleanedWounds(_playerActor) > 0)
            lines.Add($"[color={HudTheme.Hex(HudTheme.Amber)}]A wound wants cleaning[/color]");
        string muted = HudTheme.Hex(HudTheme.Muted);
        if (needs.Stamina < NeedRules.MaxStamina)
            lines.Add($"[color={muted}]Breath {needs.Stamina / 10}%[/color]");
        string wet = needs.Wet >= 80 ? "Soaked" : needs.Wet >= 50 ? "Wet" : needs.Wet >= 20 ? "Damp" : "Dry";
        lines.Add($"[color={muted}]{wet} · feels like {_sim.FeelsLike(_playerActor) / 10} °C[/color]");
        return string.Join("\n", lines);
    }

    /// <summary>Drinking from water on your tile or the next, and filling bottles there.</summary>
    private void AddWaterChoices(List<MenuChoice> choices, TilePos tile, int reach)
    {
        if (reach > 1 || _playerActor.Needs is null)
            return;
        var source = _sim.WaterSourceAt(tile);
        if (source == WaterSource.None)
            return;
        Direction? way = reach == 0 ? null : Directions.FromDelta(tile.X - _playerActor.Position.X, tile.Y - _playerActor.Position.Y);
        bool salt = source == WaterSource.Salt;
        choices.Add(new MenuChoice("Drink the water", () => Submit(new DrinkFromAction(way)),
            salt ? "seawater: it would only make you thirstier" : "untreated: it may make you ill", !salt));
        foreach (var item in _playerActor.Inventory.All)
        {
            if (!item.Def.Has(ItemHandlers.Refill))
                continue;
            int id = item.Id;
            choices.Add(new MenuChoice($"Fill the {item.Def.Name} here", () => Submit(new FillAction(id, way)),
                salt ? "seawater" : item.Amount >= item.Def.MaxAmount ? "already full" : $"{item.Def.MaxAmount} ml, untreated",
                !salt && item.Amount < item.Def.MaxAmount));
        }
    }

    /// <summary>The menu entry for sleeping, on your own tile.</summary>
    private void AddNeedChoices(List<MenuChoice> choices)
    {
        if (_playerActor.Needs is not { } needs)
            return;
        string? why = _sim.SleepBlock(_playerActor);
        int hours = (needs.Fatigue / NeedRules.SleepRecoveryPerMinute + 59) / 60;
        choices.Add(new MenuChoice("Sleep until rested", StartSleep, why ?? $"about {hours} h", why is null));
    }
}
