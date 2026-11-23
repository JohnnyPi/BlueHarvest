using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Events;

namespace BlueHarvest.Sim;

public sealed partial class Simulation
{
    /// <summary>The session this simulation runs in: its content, items, clock revision and event ledger.</summary>
    public WorldSession Session { get; }

    /// <summary>The session's item catalog: every kind of item this simulation can make.</summary>
    public ItemCatalog Catalog => Session.Catalog;

    /// <summary>
    /// Holds an event until the request it belongs to commits (the turn being resolved, or the world moving on),
    /// when it gets its ID and revision, goes into the ledger and reaches the observers. Raised outside any
    /// request, it commits at once as a request of its own.
    /// </summary>
    private void Emit(SimEventPayload payload) => Session.Emit(payload, CurrentTick);

    /// <summary>Raises the legacy C# event for a committed event: the presentation stage.</summary>
    internal void Present(SimEventPayload payload)
    {
        switch (payload)
        {
            case BitPayload e: Bit?.Invoke(e.Biter, e.Result); break;
            case ActorBittenPayload e: ActorBitten?.Invoke(e.Biter, e.Victim); break;
            case ActorBlackedOutPayload e: ActorBlackedOut?.Invoke(e.Actor); break;
            case ActorDiedPayload e: ActorDied?.Invoke(e.Actor, e.Cause); break;
            case ActorEscapedPayload e: ActorEscaped?.Invoke(e.Actor, e.Exit); break;
            case ActorFellPayload e: ActorFell?.Invoke(e.Actor, e.HeightDm); break;
            case ActorInjuredPayload e: ActorInjured?.Invoke(e.Actor, e.Section, e.Severity, e.Cause); break;
            case MoveBlockedPayload e: MoveBlocked?.Invoke(e.Actor, e.Reason); break;
            case OrderGivenPayload e: OrderGiven?.Invoke(e.From, e.To, e.Order); break;
            case RappelledPayload e: Rappelled?.Invoke(e.Actor, e.Slipped); break;
            case ActorScreamedPayload e: ActorScreamed?.Invoke(e.Actor); break;
            case ActorStruckPayload e: ActorStruck?.Invoke(e.Striker, e.Victim); break;
            case ActorTreatedPayload e: ActorTreated?.Invoke(e.Medic, e.Patient, e.Section, e.Worked); break;
            case BurnedPayload e: Burned?.Invoke(e.Actor, e.Severity); break;
            case FireDousedPayload e: FireDoused?.Invoke(e.Actor, e.TilesPutOut); break;
            case FiresOutPayload e: FiresOut?.Invoke(); break;
            case FuelPouredPayload e: FuelPoured?.Invoke(e.Actor, e.Tiles); break;
            case KindledPayload e: Kindled?.Invoke(e.Actor, e.At, e.Caught); break;
            case LightningHitGroundPayload e: LightningHitGround?.Invoke(e.At); break;
            case FireStartedPayload e: FireStarted?.Invoke(e.At, e.Source); break;
            case TanksBurstPayload e: TanksBurst?.Invoke(e.At); break;
            case ContainerOpenedPayload e: ContainerOpened?.Invoke(e.Actor, e.Container, e.Opened); break;
            case ContainerUnlockedPayload e: ContainerUnlocked?.Invoke(e.Actor, e.Container, e.Key); break;
            case ItemDroppedPayload e: ItemDropped?.Invoke(e.Actor, e.Item); break;
            case ItemsCombinedPayload e: ItemsCombined?.Invoke(e.Actor, e.Combination, e.Moved, e.Made); break;
            case LightDiedPayload e: LightDied?.Invoke(e.Actor); break;
            case LockForcedPayload e: LockForced?.Invoke(e.Actor, e.Container); break;
            case ItemPickedUpPayload e: ItemPickedUp?.Invoke(e.Actor, e.Item); break;
            case ItemStowedPayload e: ItemStowed?.Invoke(e.Actor, e.Item, e.Container); break;
            case ItemTakenOutPayload e: ItemTakenOut?.Invoke(e.Actor, e.Item, e.From); break;
            case ItemThrownPayload e: ItemThrown?.Invoke(e.Actor, e.Kind, e.LandedAt); break;
            case ItemUsedPayload e: ItemUsed?.Invoke(e.Actor, e.Item, e.Use, e.UsedUp); break;
            case FeatureFoundPayload e: FeatureFound?.Invoke(e.Actor, e.Feature); break;
            case MapChartedPayload e: MapCharted?.Invoke(e.Actor, e.Map, e.Sheet, e.IsNew); break;
            case ParkMapReadPayload e: ParkMapRead?.Invoke(e.Actor, e.Map, e.Feature); break;
            case SiteFoundPayload e: SiteFound?.Invoke(e.Actor, e.Site); break;
            case ItemLightDiedPayload e: ItemLightDied?.Invoke(e.Holder, e.Light); break;
            case ItemLightSwitchedPayload e: ItemLightSwitched?.Invoke(e.Actor, e.Light, e.Lit); break;
            case AteKillPayload e: AteKill?.Invoke(e.Actor, e.Kill); break;
            case NeedBlackedOutPayload e: NeedBlackedOut?.Invoke(e.Actor, e.Need); break;
            case NeedChangedPayload e: NeedChanged?.Invoke(e.Actor, e.Need, e.Stage, e.WasStage); break;
            case FellAsleepPayload e: FellAsleep?.Invoke(e.Actor); break;
            case WokeUpPayload e: WokeUp?.Invoke(e.Actor, e.Reason); break;
            case DoorForcedPayload e: DoorForced?.Invoke(e.Actor, e.At); break;
            case KeycardUsedPayload e: KeycardUsed?.Invoke(e.Actor, e.Card, e.At); break;
            case PowerChangedPayload e: PowerChanged?.Invoke(e.Change); break;
            case PowerOperatedPayload e: PowerOperated?.Invoke(e.Actor, e.Device, e.Result); break;
            case RideEndedPayload e: RideEnded?.Invoke(e.Actor); break;
            case ShockedPayload e: Shocked?.Invoke(e.Actor, e.At); break;
            case SparksDiedPayload e: SparksDied?.Invoke(e.Fence); break;
            case ActorEnteredRoomPayload e: ActorEnteredRoom?.Invoke(e.Actor, e.Room); break;
            case ActorLeftRoomPayload e: ActorLeftRoom?.Invoke(e.Actor, e.Room); break;
            case CoverBrokenPayload e: CoverBroken?.Invoke(e.At, e.Cover, e.By); break;
            case PlatformCollapsedPayload e: PlatformCollapsed?.Invoke(e.Platform, e.By, e.Riders); break;
            case VehicleDamagedPayload e: VehicleDamaged?.Invoke(e.Vehicle, e.By, e.Damage); break;
            case VehicleShovedPayload e: VehicleShoved?.Invoke(e.Vehicle, e.By); break;
            case WaterChangedPayload e: WaterChanged?.Invoke(e.Change); break;
            case DrankWaterPayload e: DrankWater?.Invoke(e.Actor, e.At); break;
            case WaterFilledPayload e: WaterFilled?.Invoke(e.Actor, e.Container); break;
            case WaterMovedPayload e: WaterMoved?.Invoke(); break;
            case SweptPayload e: Swept?.Invoke(e.Actor, e.To); break;
            case WeatherChangedPayload e: WeatherChanged?.Invoke(e.Settings); break;
            case LightningStruckPayload e: LightningStruck?.Invoke(e.Strike); break;
            case WindowShiftedPayload e: WindowShifted?.Invoke(e.Dx, e.Dy); break;
            case ActorActedPayload:
                break; // a turn taken: in the ledger, with no C# event of its own
            default:
                throw new InvalidOperationException($"No C# event for {payload.Type}.");
        }
    }
}
