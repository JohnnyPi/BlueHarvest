using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Lighting;
using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim;

/// <summary>
/// Lights that are items (step 23): the headlamp and the lantern (batteries, switched on and off),
/// glow sticks (cracked, eight hours) and the torch (lit with a flame, half an hour). A lit one
/// shines where it is: held or worn, on its holder (the headlamp where they face); set down or
/// thrown, where it lies. In a bag it's hidden, and in a pocket too, except a glow stick. The flashlight keeps its own older
/// path (ToggleLightAction, Actor.CarriedLight).
/// </summary>
public partial class Simulation
{
    /// <summary>A glow stick glows 8 game hours; a torch burns 30 game minutes.</summary>
    public const int GlowStickTicks = 8 * 60 * WorldClock.TicksPerGameMinute, TorchTicks = 30 * WorldClock.TicksPerGameMinute;

    /// <summary>Cracking a glow stick, lighting a torch.</summary>
    public const int CrackTicks = 100, LightTorchTicks = 200;

    /// <summary>The lit items, and which of the lights each one is.</summary>
    private readonly List<Item> _litItems = new();
    private readonly Dictionary<int, int> _itemLights = new();

    /// <summary>A lit item went out by itself: batteries flat, glow faded, torch burnt down (the actor holding it, if anyone).</summary>
    public event Action<Actor?, Item>? ItemLightDied;

    /// <summary>Someone switched a light on or off, cracked a glow stick or lit a torch (true: it's lit now).</summary>
    public event Action<Actor, Item, bool>? ItemLightSwitched;

    /// <summary>The lit items now.</summary>
    public IReadOnlyList<Item> LitItems => _litItems;

    /// <summary>Switches a carried headlamp or lantern on (with charged batteries) or off.</summary>
    private int SwitchLight(Actor actor, Item item)
    {
        if (item.Lit)
        {
            item.Lit = false;
            SyncItemLights();
            ItemLightSwitched?.Invoke(actor, item, false);
            return GearRules.FlashlightToggleTicks;
        }
        if (!item.Charged)
            return Refuse(actor, item.Contents.Count == 0 ? $"the {item.Def.Name} has no batteries" : $"the {item.Def.Name}'s batteries are flat");
        item.Lit = true;
        item.DrainedTick = CurrentTick;
        TrackLight(item);
        ItemLightSwitched?.Invoke(actor, item, true);
        return GearRules.FlashlightToggleTicks;
    }

    /// <summary>Cracks a glow stick from a carried stack (or the last one): it glows in your hand.</summary>
    private int CrackGlowStick(Actor actor, Item item)
    {
        if (item.Lit)
            return Refuse(actor, "it's already glowing");
        if (item.Spent)
            return Refuse(actor, "it's spent");
        var stick = TakeOneGlowStick(actor, item);
        LightUp(stick, GlowStickTicks);
        if (!GiveItem(actor, stick))
            PlaceItem(stick, actor.Position, actor.OnDeck); // nowhere to hold it: it lies at your feet
        TrackLight(stick);
        ItemLightSwitched?.Invoke(actor, stick, true);
        return CrackTicks;
    }

    /// <summary>One glow stick off a carried stack (the item itself if it's the last), out of the inventory.</summary>
    private Item TakeOneGlowStick(Actor actor, Item stack)
    {
        if (stack.Amount > 1)
        {
            stack.Amount--;
            return _items.Create(ItemKind.GlowStick, 1);
        }
        actor.Inventory.Remove(stack);
        return stack;
    }

    /// <summary>Lights a carried torch with a lighter, a match or another burning torch.</summary>
    private int LightTorch(Actor actor, Item torch)
    {
        if (torch.Lit)
            return Refuse(actor, "it's already burning");
        if (torch.Spent)
            return Refuse(actor, "it's burnt out");
        var inventory = actor.Inventory;
        var flame = inventory.All.FirstOrDefault(i => i.Kind is ItemKind.Lighter or ItemKind.Matches && i.Amount > 0);
        bool fromTorch = inventory.All.Any(i => i.Kind == ItemKind.Torch && i.Lit);
        if (flame is null && !fromTorch)
            return Refuse(actor, "you have nothing to light it with");
        if (!fromTorch)
            flame!.Amount--;
        LightUp(torch, TorchTicks);
        TrackLight(torch);
        EmitNoise(actor.Position, 4, NoiseKinds.Fire, actor.Id);
        ItemLightSwitched?.Invoke(actor, torch, true);
        return LightTorchTicks * NeedEffectsOf(actor).HandsPercent / 100;
    }

    private void LightUp(Item item, int ticks)
    {
        item.Lit = true;
        item.LitUntilTick = CurrentTick + ticks;
    }

    /// <summary>A just-lit item joins the lights.</summary>
    private void TrackLight(Item item)
    {
        if (!_litItems.Contains(item))
            _litItems.Add(item);
        SyncItemLights();
    }

    /// <summary>
    /// Brings the lit items' lights up to now: timed ones go out at their time, battery ones run down
    /// (1% per <see cref="ItemRules.LightTicksPerPercent"/> a battery) and go out when flat; each shines
    /// where it is, or is hidden in a pocket, a bag, or off the map.
    /// </summary>
    private void SyncItemLights()
    {
        if (_litItems.Count == 0)
            return;
        foreach (var item in _litItems.ToList())
        {
            Actor? holder = CarrierOf(item);
            bool died = false;
            if (item.Lit && item.LitUntilTick > 0 && CurrentTick >= item.LitUntilTick)
            {
                item.Lit = false;
                died = true;
            }
            else if (item.Lit && item.Def.Holds is { Only: not null })
            {
                long percent = (CurrentTick - item.DrainedTick) / ItemRules.LightTicksPerPercent;
                if (percent > 0)
                {
                    item.DrainedTick += percent * ItemRules.LightTicksPerPercent;
                    foreach (var cell in item.Contents)
                        cell.Amount = (int)Math.Max(0, cell.Amount - percent);
                }
                if (!item.Charged)
                {
                    item.Lit = false;
                    died = true;
                }
            }

            _itemLights.TryGetValue(item.Id, out int index);
            bool known = _itemLights.ContainsKey(item.Id);
            if (!item.Lit)
            {
                if (known && _lights[index].On)
                    SetLightOn(index, false);
                _litItems.Remove(item);
                if (died)
                    ItemLightDied?.Invoke(holder, item);
                continue;
            }

            LightSource? shine = ShineOf(item, holder);
            if (shine is null)
            {
                if (known && _lights[index].On)
                    SetLightOn(index, false);
                continue;
            }
            if (!known)
            {
                _lights.Add(shine);
                _itemLights[item.Id] = _lights.Count - 1;
                _lightGrid = null;
            }
            else if (_lights[index] != shine)
            {
                _lights[index] = shine;
                _lightGrid = null;
            }
        }
    }

    /// <summary>Who carries an item (anywhere on them), or null.</summary>
    private Actor? CarrierOf(Item item)
    {
        foreach (var actor in _actors)
            if (actor.Inventory.Find(item.Id) is not null)
                return actor;
        return null;
    }

    /// <summary>The light a lit item gives where it is now, or null if it's hidden (pocket, bag, container, off the map).</summary>
    private LightSource? ShineOf(Item item, Actor? holder)
    {
        TilePos at;
        bool carried;
        Direction facing = Direction.North;
        if (holder is not null)
        {
            // held or worn shines; a glow stick shines from a pocket too (clipped on), nothing does from a bag
            var slot = holder.Inventory.SlotOf(item);
            if (holder.LeftBehind || holder.HasEscaped || slot is null || slot == CarrySlot.Pocket && item.Kind != ItemKind.GlowStick)
                return null;
            at = holder.Position;
            facing = holder.Facing;
            carried = true;
        }
        else if (_items.Find(item.Id) is { } lying)
        {
            at = FromIsland(lying.X, lying.Y);
            if (!Map.InBounds(at))
                return null;
            carried = false;
        }
        else
        {
            return null; // inside something
        }
        return item.Kind switch
        {
            ItemKind.Headlamp => carried ? LightSource.Headlamp(at, facing) : LightSource.Lantern(at, false) with { RadiusTiles = 2, IntensityPercent = 30 },
            ItemKind.Lantern => LightSource.Lantern(at, carried),
            ItemKind.GlowStick => LightSource.GlowStick(at, carried),
            _ => LightSource.Torch(at, carried),
        };
    }

    /// <summary>A glow stick to throw: a glowing one carried, or one cracked from a stack (out of the inventory either way).</summary>
    private Item? GlowStickToThrow(Actor actor)
    {
        var inventory = actor.Inventory;
        if (inventory.All.FirstOrDefault(i => i.Kind == ItemKind.GlowStick && i.Lit) is { } glowing)
        {
            inventory.Remove(glowing);
            return glowing;
        }
        if (inventory.All.FirstOrDefault(i => i.Kind == ItemKind.GlowStick && !i.Spent && !i.Lit && i.Amount > 0) is not { } stack)
            return null;
        var stick = TakeOneGlowStick(actor, stack);
        LightUp(stick, GlowStickTicks);
        return stick;
    }
}
