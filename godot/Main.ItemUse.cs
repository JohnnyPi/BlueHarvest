#nullable enable
using System.Collections.Generic;
using System.Linq;
using BlueHarvest.Game.Ui;
using BlueHarvest.Sim;
using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.World;

namespace BlueHarvest.Game;

/// <summary>Using items in the log and the menu: eating, drinking, combining, unloading, keys and crowbars.</summary>
public partial class Main
{
    private void SubscribeToItemUseEvents()
    {
        _sim.ItemsCombined += OnItemsCombined;
        _sim.ItemTakenOut += OnItemTakenOut;
        _sim.ItemUsed += OnItemUsed;
        _sim.ContainerUnlocked += OnContainerUnlocked;
        _sim.LockForced += OnLockForced;
        _sim.LightDied += OnLightDied;
        _sim.KeycardUsed += OnKeycardUsed;
        _sim.FireDoused += OnFireDoused;
        _sim.FuelPoured += OnFuelPoured;
    }

    private void OnKeycardUsed(Actor actor, Item card, TilePos door)
    {
        if (actor == _playerActor)
            Say($"You touch the {card.Def.Name} to the reader. The lock clicks and the door swings open.");
    }

    private void OnItemsCombined(Actor actor, Combination combination, int moved, Item? made)
    {
        if (actor != _playerActor)
            return;
        var into = combination.Into;
        var from = combination.From;
        Say(combination.Kind switch
        {
            CombineKind.Load => $"You load {moved} {(moved == 1 ? from.Def.Name : from.Def.Plural)} into the {into.Def.Name}.",
            CombineKind.Fit => $"You fit the {from.Def.Name} into the {into.Def.Name}.",
            _ => made is null ? "You make nothing of it." : $"You make {made.Describe()}.",
        });
    }

    private void OnItemTakenOut(Actor actor, Item item, Item from)
    {
        if (actor == _playerActor)
            Say($"You take {item.Describe()} out of the {from.Def.Name}.");
    }

    private void OnItemUsed(Actor actor, Item item, ItemUse use, int amount)
    {
        if (actor != _playerActor)
        {
            if (Shown(actor) && use is ItemUse.StopBleeding)
                Say($"{actor.Name} binds a bleeding wound.");
            return;
        }
        var def = item.Def;
        Say(use switch
        {
            ItemUse.Eat => $"You eat {def.WithArticle}.",
            ItemUse.Drink => $"You drink {amount} ml from the {def.Name}.",
            ItemUse.StopBleeding => $"You bind the wound with the {def.Name}: the bleeding stops.",
            ItemUse.TakeMedicine => $"You swallow {amount} {def.Unit ?? "doses"} from the {def.Name}.",
            ItemUse.DressWounds => $"You use the {def.Name} on your wounds.",
            ItemUse.Read => $"The {def.Name}: {def.Note ?? "nothing of use."}",
            ItemUse.TakeBearing => $"The needle settles: you're facing {_playerActor.Facing.Compass()}.",
            ItemUse.Listen => "Static. Under it, for a moment, a voice, then nothing.",
            ItemUse.TreatWater => "You drop a tablet into your water.",
            ItemUse.Inject => "You jab the auto-injector into your thigh. Your heart hammers; the pain drops away.",
            _ => $"You use the {def.Name}.",
        });
    }

    private void OnContainerUnlocked(Actor actor, Item container, Item key)
    {
        if (actor == _playerActor)
            Say($"Your {key.Def.Name} opens the {container.Def.Name}.");
    }

    private void OnLockForced(Actor actor, Item container)
    {
        if (actor == _playerActor)
            Say($"You wrench the {container.Def.Name} open with the crowbar. That was loud.");
        else if (Shown(actor))
            Say($"{actor.Name} forces a {container.Def.Name} open.");
    }

    private void OnLightDied(Actor actor)
    {
        if (actor == _playerActor)
            Say("Your flashlight flickers and dies: the batteries are flat.");
    }

    /// <summary>The menu's words for using a carried item.</summary>
    private static string UseLabel(Item item, ItemUse use)
    {
        var def = item.Def;
        return use switch
        {
            ItemUse.Eat => $"Eat {def.WithArticle}",
            ItemUse.Drink => $"Drink from the {def.Name}",
            ItemUse.StopBleeding => $"Bind a bleeding wound with the {def.Name}",
            ItemUse.TakeMedicine => $"Take {def.Dose} {def.Unit ?? "doses"} of the {def.Name}",
            ItemUse.DressWounds => $"Use the {def.Name} on your wounds",
            ItemUse.Read => $"Read the {def.Name}",
            ItemUse.TakeBearing => "Check the compass",
            ItemUse.Listen => "Listen to the radio",
            ItemUse.TreatWater => "Drop a water tablet into your water",
            ItemUse.Inject => $"Inject the {def.Name}",
            _ => $"Use the {def.Name}",
        };
    }

    /// <summary>What a use would do for you, for the menu: food's kcal, water's ml, painkillers' relief.</summary>
    private static string? UseHint(Item item, ItemUse use)
    {
        var def = item.Def;
        switch (use)
        {
            case ItemUse.Eat:
                var (kcal, water, _) = Nutrition.Food(def.Kind);
                if (kcal <= 0)
                    return null;
                return water > 0 ? $"{kcal} kcal, {water} ml" : $"{kcal} kcal";
            case ItemUse.Drink:
                return $"{System.Math.Min(def.Dose, item.Amount)} ml of {item.Amount}";
            case ItemUse.TakeMedicine when def.Kind == ItemKind.Painkillers:
                return "dulls pain for 4 h";
            case ItemUse.Inject:
                return "30 s: no pain, full breath";
            default:
                return null;
        }
    }

    /// <summary>The menu's words for two carried items going together.</summary>
    private static string CombineLabel(Combination c) => c.Kind switch
    {
        CombineKind.Load => $"Load {c.From.Def.Plural} into the {c.Into.Def.Name}",
        CombineKind.Fit => $"Fit {c.From.Describe()} into the {c.Into.Def.Name}",
        _ => $"Make {ItemCatalog.Of(c.Recipe!.Makes).WithArticle} ({c.Into.Def.Name} + {c.From.Def.Name})",
    };

    /// <summary>
    /// What you can do with what you carry, for the menu on your own tile: use things, put pairs
    /// together, take batteries and magazines out, unload.
    /// </summary>
    private void AddCarriedChoices(List<MenuChoice> items)
    {
        var inventory = _playerActor.Inventory;
        var carried = inventory.All.ToList();

        foreach (var item in carried)
        {
            if (Recipes.UseOf(item.Def) is { } use)
            {
                bool empty = !item.Def.Stacks && item.Def.Unit is not null && item.Amount == 0;
                items.Add(new MenuChoice(UseLabel(item, use), () => Submit(new UseItemAction(item.Id)), empty ? "empty" : UseHint(item, use), !empty));
            }
        }

        var seen = new HashSet<(ItemKind, ItemKind, int)>();
        for (int i = 0; i < carried.Count; i++)
        {
            for (int j = i + 1; j < carried.Count; j++)
            {
                if (Recipes.Of(carried[i], carried[j]) is not { } c || !seen.Add((c.From.Kind, c.Into.Kind, c.Into.Id)))
                    continue;
                // nothing already fitted, and nothing into a slot or a gun that's full
                if (inventory.HolderOf(c.From) is { Def.Holds.Only: not null })
                    continue;
                string? why = c.Kind switch
                {
                    CombineKind.Load => c.Into.Amount >= c.Into.Def.MaxAmount ? "full" : null,
                    CombineKind.Fit => c.Into.RefusesToHold(c.From),
                    _ => null,
                };
                var (a, b) = (carried[i], carried[j]);
                items.Add(new MenuChoice(CombineLabel(c), () => Submit(new CombineAction(a.Id, b.Id)), why, why is null));
            }
        }

        foreach (var item in carried)
        {
            if (item.Def.Fills is not null && item.Amount > 0)
                items.Add(new MenuChoice($"Unload the {item.Def.Name}", () => Submit(new UnloadAction(item.Id)), $"{item.Amount} {item.Def.Unit}"));
            else if (inventory.HolderOf(item) is { Def.Holds.Only: not null } slot)
                items.Add(new MenuChoice($"Take the {item.Def.Name} out of the {slot.Def.Name}", () => Submit(new TakeOutAction(item.Id))));
        }
    }

    /// <summary>For a locked container within reach: whether your key opens it, or a crowbar can force it.</summary>
    private void AddLockChoices(List<MenuChoice> items, Item container)
    {
        if (!container.Locked)
            return;
        if (_playerActor.Inventory.Has(ItemKind.Crowbar))
            items.Add(new MenuChoice($"Force the {container.Def.Name} open with the crowbar", () => Submit(new PryAction(container.Id)), "loud"));
    }

    /// <summary>A hint on a locked container's "Open": what your key or card does, or that it's locked.</summary>
    private string LockHint(Item container) =>
        Simulation.KeyFor(_playerActor, container) is { } key ? $"your {key.Def.Name} opens it"
        : container.Level > 0 ? $"locked: a level {container.Level} card"
        : "locked";
}
