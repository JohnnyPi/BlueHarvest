using System.Text.Json;
using System.Text.Json.Nodes;
using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Ai;
using BlueHarvest.Sim.Injuries;
using BlueHarvest.Sim.Lighting;
using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.Power;
using BlueHarvest.Sim.World;
using BlueHarvest.Sim.World.Generation;
using static BlueHarvest.Sim.Snapshots.SnapshotJson;

namespace BlueHarvest.Sim;

/// <summary>
/// The simulation's own state for a snapshot (P3b-2): every actor with body, health, needs, kit and mind; the
/// turn order and the random streams; lights, fire, power, water, vehicles and platforms; and every trace left on
/// the ground. Written so that putting it back and playing on is the same, tick for tick, as never having stopped.
/// Places are island metres (contracts §7); dictionaries and sets are written sorted, so equal states write the same.
/// </summary>
public sealed partial class Simulation
{
    private static readonly Species[] SnapshotSpecies = [Species.Human, Species.Raptor, Species.Rex];

    // ================= capture =================

    internal JsonObject CaptureState()
    {
        if (_rooms is null && _roomOf.Count > 0)
            throw new InvalidOperationException("Room tracking without a room index.");
        var (queue, nextSequence) = _scheduler.Capture();
        return new JsonObject
        {
            ["counters"] = new JsonObject
            {
                ["actionsResolved"] = ActionsResolved,
                ["coverChanges"] = CoverChanges,
                ["structureChanges"] = StructureChanges,
                ["waterChanges"] = WaterChanges,
                ["tilesBurnt"] = TilesBurnt,
                ["fenceBreach"] = FenceBreachTick,
                ["playerDied"] = _playerDied,
                ["playerEscaped"] = _playerEscaped,
                ["weatherTick"] = _weatherTick,
                ["sparkTick"] = _sparkTick,
                ["ambient"] = _ambientLight,
                ["baseTemperature"] = _baseTemperature,
            },
            ["rng"] = new JsonObject { ["injuries"] = RngState(_injuryRng), ["gear"] = RngState(_gearRng) },
            ["actors"] = new JsonArray(_actors.Select(a => (JsonNode?)SaveActor(a)).ToArray()),
            ["scheduler"] = new JsonObject
            {
                ["next"] = nextSequence,
                ["queue"] = new JsonArray(queue.Select(e => (JsonNode?)new JsonArray(e.ActorId, e.Tick, e.Sequence)).ToArray()),
            },
            ["grids"] = new JsonObject
            {
                ["occupant"] = SaveGrid(_occupant),
                ["deck"] = SaveGrid(_deckOccupant),
                ["vehicle"] = SaveGrid(_vehicleAt),
                ["platform"] = SaveGrid(_platformAt),
                ["damp"] = SaveBytes(Map.Damp),
                ["smoke"] = SaveBytes(Map.Smoke),
            },
            ["lights"] = new JsonObject
            {
                ["all"] = new JsonArray(_lights.Select(l => (JsonNode?)SaveLight(l)).ToArray()),
                ["expiry"] = new JsonArray(_lightExpiry.Select(e => (JsonNode?)new JsonArray(e.Light, e.OffTick)).ToArray()),
                ["litItems"] = new JsonArray(_litItems.Select(i => (JsonNode?)InstanceId(i.Id)).ToArray()),
                ["itemLights"] = SortedPairs(_itemLights),
                ["lampLights"] = SortedPairs(_lampLights),
                ["fireLights"] = new JsonArray(_fireLights.Select(i => (JsonNode?)i).ToArray()),
                ["grid"] = new JsonArray(_lightGridAmbient, _lightGridTick),
            },
            ["traces"] = new JsonObject
            {
                ["noises"] = new JsonArray(_noises.Select(n => (JsonNode?)new JsonObject
                {
                    ["tick"] = n.Tick, ["at"] = Tile(this, n.Position), ["loudness"] = n.Loudness, ["kind"] = n.Kind, ["actor"] = n.ActorId,
                }).ToArray()),
                ["vibrations"] = new JsonArray(_vibrations.Select(v => (JsonNode?)new JsonObject
                {
                    ["tick"] = v.Tick, ["at"] = Tile(this, v.Position), ["strength"] = v.Strength, ["actor"] = v.ActorId,
                }).ToArray()),
                ["blood"] = new JsonArray(_blood.Select(b => (JsonNode?)new JsonObject { ["at"] = Tile(this, b.Position), ["tick"] = b.Tick }).ToArray()),
                ["scent"] = new JsonArray(_scent.Select(m => (JsonNode?)new JsonObject
                {
                    ["at"] = Tile(this, m.Position), ["tick"] = m.Tick, ["actor"] = m.ActorId, ["strength"] = m.Strength,
                    ["blood"] = m.Blood, ["wash"] = m.WashStamp,
                }).ToArray()),
                ["ropes"] = new JsonArray(_ropes.Select(r => (JsonNode?)new JsonArray(Tile(this, r.Top), Tile(this, r.Bottom))).ToArray()),
                ["exits"] = new JsonArray(_exits.Select(e => (JsonNode?)SaveExit(e)).ToArray()),
                ["sparks"] = new JsonArray(_sparks.Select(s => (JsonNode?)new JsonObject
                {
                    ["at"] = Tile(this, s.Position), ["light"] = s.LightIndex, ["live"] = s.Live, ["died"] = s.DiedTick,
                }).ToArray()),
            },
            ["vehicles"] = new JsonArray(_vehicles.Select(v => (JsonNode?)new JsonObject
            {
                ["id"] = v.Id,
                ["name"] = v.Name,
                ["tiles"] = Tiles(this, v.Tiles),
                ["under"] = new JsonArray(v.Under.OrderBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.X)
                    .Select(kv => (JsonNode?)new JsonArray(Tile(this, kv.Key), Name(kv.Value))).ToArray()),
                ["integrity"] = v.Integrity,
                ["lights"] = new JsonArray(v.LightIndices.Select(i => (JsonNode?)i).ToArray()),
                ["leftBehind"] = v.LeftBehind,
            }).ToArray()),
            ["platforms"] = new JsonArray(_platforms.Select(p => (JsonNode?)new JsonObject
            {
                ["id"] = p.Id, ["tiles"] = Tiles(this, p.Tiles), ["supports"] = p.InitialSupports, ["collapsed"] = p.Collapsed,
            }).ToArray()),
            ["fire"] = SaveFire(),
            ["power"] = SavePower(),
            ["water"] = SaveWater(),
            ["rooms"] = SortedPairs(_roomOf),
        };
    }

    private static JsonArray SortedPairs(IEnumerable<KeyValuePair<int, int>> pairs) =>
        new(pairs.OrderBy(kv => kv.Key).Select(kv => (JsonNode?)new JsonArray(kv.Key, kv.Value)).ToArray());

    private JsonArray SaveGrid(int[] grid)
    {
        var cells = new JsonArray();
        for (int i = 0; i < grid.Length; i++)
        {
            if (grid[i] != 0)
                cells.Add(new JsonArray(Tile(this, new TilePos(i % Map.Width, i / Map.Width)), grid[i]));
        }
        return cells;
    }

    private JsonArray SaveBytes(ByteMap map)
    {
        var cells = new JsonArray();
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                if (map[new TilePos(x, y)] is int v and not 0)
                    cells.Add(new JsonArray(Tile(this, new TilePos(x, y)), v));
        return cells;
    }

    private JsonObject SaveLight(LightSource l) => new()
    {
        ["at"] = Tile(this, l.Position),
        ["height"] = l.HeightDm,
        ["radius"] = l.RadiusTiles,
        ["intensity"] = l.IntensityPercent,
        ["facing"] = l.Facing is { } f ? Name(f) : null,
        ["cone"] = l.ConeCosPermille,
        ["on"] = l.On,
        ["red"] = l.Red,
    };

    private JsonObject SaveExit(ExitPoint e) => new() { ["at"] = Tile(this, e.Position), ["name"] = e.Name };

    private JsonObject SaveActor(Actor a)
    {
        var o = new JsonObject
        {
            ["id"] = a.Id,
            ["name"] = a.Name,
            ["glyph"] = a.Glyph.ToString(),
            ["species"] = a.Species.Name,
            ["controller"] = SaveController(a),
            ["space"] = "surface", // every actor is on the surface until interiors arrive (P10)
            ["at"] = Tile(this, a.Position),
            ["body"] = new JsonArray(a.Body.Select(b => (JsonNode?)new JsonArray(Tile(this, b.Position), Name(b.Part))).ToArray()),
            ["facing"] = Name(a.Facing),
            ["deck"] = a.OnDeck,
            ["lastScent"] = a.LastScentTick,
            ["lastMoved"] = a.LastMovedTick,
            ["nextAct"] = a.NextActTick,
            ["scheduled"] = a.Scheduled,
            ["charging"] = a.Charging,
            ["health"] = SaveHealth(a.Health),
            ["escapedVia"] = a.EscapedVia is { } exit ? SaveExit(exit) : null,
            ["escapedTick"] = a.EscapedTick,
            ["leftBehind"] = a.LeftBehind,
            ["parkedAt"] = Pair(_islandOriginX + a.ParkedAt.X, _islandOriginY + a.ParkedAt.Y),
            ["parkedLightOn"] = a.ParkedLightOn,
            ["pendingRide"] = a.PendingRide is { } ride ? Pair(ride.X, ride.Y) : null,
            ["skills"] = JsonSerializer.SerializeToNode(a.Skills),
            ["carried"] = new JsonArray(a.Inventory.Carried
                .Select(c => (JsonNode?)new JsonObject { ["slot"] = Name(c.Slot), ["item"] = WriteItem(c.Item) }).ToArray()),
            ["order"] = a.Order is { } order
                ? new JsonObject { ["order"] = Name(order.Order), ["from"] = order.FromActorId, ["tick"] = order.Tick }
                : null,
            ["carriedLight"] = a.CarriedLight,
            ["lightDrain"] = a.LightDrainTick,
            ["needs"] = a.Needs is { } needs ? SaveNeeds(needs) : null,
            ["lastBite"] = a.LastBite is { } bite ? SaveBite(bite) : null,
        };
        if (Knowledge(a) is { } known)
        {
            o["knowledge"] = WriteKnowledge(known);
            o["lastSight"] = LastSight(a) is { } last ? new JsonArray(last.X, last.Y, last.Range) : null;
        }
        return o;
    }

    private JsonObject SaveController(Actor a) => a.Controller switch
    {
        PlayerController => new JsonObject { ["type"] = "player" },
        HumanBrain human => human.Save(this),
        CreatureBrain creature => creature.Save(this),
        RandomWalker walker => new JsonObject { ["type"] = "walker", ["rng"] = RngState(walker.Stream) },
        HoldStillController => new JsonObject { ["type"] = "still" },
        _ => throw new NotSupportedException($"{a.Name}'s controller ({a.Controller.GetType().Name}) can't be saved."),
    };

    private static JsonObject SaveHealth(Health h) => new()
    {
        ["sections"] = new JsonArray(Enum.GetValues<BodySection>()
            .Select(s => (JsonNode?)new JsonArray(Name(h[s].Severity), h[s].Bleeding, h[s].SinceTick)).ToArray()),
        ["bloodLoss"] = h.BloodLossTicks,
        ["updated"] = h.LastUpdatedTick,
        ["death"] = h.DeathCause,
        ["died"] = h.DiedTick,
    };

    private static JsonObject SaveNeeds(Needs n) => new()
    {
        ["profile"] = JsonSerializer.SerializeToNode(n.Profile),
        ["fed"] = n.FedTick,
        ["hunger"] = n.Hunger,
        ["thirst"] = n.Thirst,
        ["fatigue"] = n.Fatigue,
        ["stamina"] = n.Stamina,
        ["asleep"] = n.Asleep,
        ["painkillers"] = n.PainkillersUntilTick,
        ["adrenaline"] = n.AdrenalineUntilTick,
        ["chill"] = n.Chill,
        ["wet"] = n.Wet,
        ["gutOnset"] = n.GutOnsetTick,
        ["gutEnd"] = n.GutEndTick,
        ["infection"] = n.Infection,
        ["antibiotics"] = n.AntibioticsUntilTick,
        ["cleaned"] = new JsonArray(n.CleanedTick.Select(t => (JsonNode?)t).ToArray()),
        ["minute"] = n.Minute,
        ["staminaTick"] = n.StaminaTick,
        ["staminaRemainder"] = n.StaminaRemainder,
        ["woke"] = n.WokeTick,
        ["wakeCheck"] = n.WakeCheckTick,
        ["reported"] = new JsonArray(n.Reported.Select(r => (JsonNode?)r).ToArray()),
    };

    private JsonObject SaveBite(BiteResult b) => new()
    {
        ["tick"] = b.Tick,
        ["investigative"] = b.Investigative,
        ["zone"] = Tiles(this, b.Zone),
        ["hits"] = new JsonArray(b.Hits.Select(h => (JsonNode?)new JsonArray(h.Victim.Id, Tile(this, h.At))).ToArray()),
        ["cover"] = Tiles(this, b.CoverBitten),
        ["vehicles"] = new JsonArray(b.VehiclesBitten.Select(v => (JsonNode?)v.Id).ToArray()),
    };

    private JsonObject SaveFire() => new()
    {
        ["fires"] = new JsonArray(_fires.Values.OrderBy(f => f.Position.Y).ThenBy(f => f.Position.X).Select(f => (JsonNode?)new JsonObject
        {
            ["at"] = Tile(this, f.Position), ["intensity"] = f.Intensity, ["fuel"] = f.FuelSteps, ["max"] = f.MaxIntensity, ["burnt"] = f.StepsBurnt,
        }).ToArray()),
        ["fuel"] = SortedTiles(_fuel),
        ["quenched"] = SortedTiles(_quenched),
        ["groundFlares"] = new JsonArray(_groundFlares.Select(g => (JsonNode?)new JsonArray(Tile(this, g.Tile), g.OffTick)).ToArray()),
        ["burntVehicles"] = new JsonArray(_burntVehicles.Order().Select(v => (JsonNode?)v).ToArray()),
        ["lastBurn"] = SortedPairs(_lastBurnTick),
        ["tankHeat"] = SortedPairs(_tankHeat),
        ["tick"] = _fireTick,
        ["smoke"] = _smokeOnMap,
        ["rainHour"] = _recentRainHour,
        ["rainPercent"] = _recentRainPercent,
    };

    private static JsonArray SortedPairs(IEnumerable<KeyValuePair<int, long>> pairs) =>
        new(pairs.OrderBy(kv => kv.Key).Select(kv => (JsonNode?)new JsonArray(kv.Key, kv.Value)).ToArray());

    private JsonArray SortedTiles(IEnumerable<KeyValuePair<TilePos, long>> tiles) =>
        new(tiles.OrderBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.X).Select(kv => (JsonNode?)new JsonArray(Tile(this, kv.Key), kv.Value)).ToArray());

    private JsonObject? SavePower()
    {
        if (Power is not { } grid)
            return null;
        return new JsonObject
        {
            ["sites"] = new JsonArray(grid.Sites.Select(s => (JsonNode?)new JsonObject
            {
                ["id"] = s.Id, ["boardOn"] = s.BoardOn, ["autoBackup"] = s.AutoBackup, ["locks"] = s.LocksEngaged,
                ["securityDoors"] = s.HasSecurityDoors, ["energised"] = s.Energised, ["busLive"] = s.BusLive,
                ["powered"] = s.Powered, ["source"] = Name(s.Source),
            }).ToArray()),
            ["devices"] = new JsonArray(grid.Devices.Select(d => (JsonNode?)SaveDevice(d)).ToArray()),
            ["feeders"] = new JsonArray(grid.Feeders.Select(f => (JsonNode?)new JsonObject
            {
                ["id"] = f.Id, ["breaker"] = Name(f.Breaker), ["down"] = f.Down, ["fault"] = f.FaultDevice,
            }).ToArray()),
            ["lamps"] = new JsonArray(grid.Lamps.Select(l => (JsonNode?)new JsonObject
            {
                ["id"] = l.Id, ["standalone"] = l.Standalone, ["broken"] = l.Broken, ["charge"] = l.ChargeWh,
                ["capacity"] = l.CapacityWh, ["panel"] = l.PanelW, ["lit"] = l.Lit,
            }).ToArray()),
            ["liveFence"] = Tiles(this, _liveFence.OrderBy(t => t.Y).ThenBy(t => t.X)),
            ["signature"] = _powerSignature,
            ["lastShock"] = new JsonArray(_lastShock.ActorId, _lastShock.Tick),
            ["machinery"] = _machineryTick,
        };
    }

    private static JsonObject SaveDevice(PowerDevice d)
    {
        var o = new JsonObject { ["id"] = d.Id, ["kind"] = d.GetType().Name };
        switch (d)
        {
            case Generator g:
                o["state"] = Name(g.State);
                o["fuel"] = g.FuelCentilitres;
                o["autoStarted"] = g.AutoStarted;
                break;
            case FuelStore f:
                o["litres"] = f.Litres;
                o["valve"] = f.ValveOpen;
                break;
            case BatteryBank b:
                o["charge"] = b.ChargeWh;
                o["inverter"] = b.InverterOn;
                break;
            case SolarArray s:
                o["condition"] = s.ConditionPercent;
                o["connected"] = s.Connected;
                o["output"] = s.OutputW;
                break;
            case Plant p:
                o["state"] = Name(p.State);
                o["startLeft"] = p.StartMinutesLeft;
                break;
        }
        return o;
    }

    private JsonObject SaveWater()
    {
        var covers = new List<byte>();
        for (int y = 0; y < Map.Height; y++)
            for (int x = 0; x < Map.Width; x++)
                if (Map.Water[new TilePos(x, y)].Kind != WaterKind.None)
                    covers.Add((byte)Cover[new TilePos(x, y)]);
        return new JsonObject
        {
            ["sea"] = _seaLevelDm,
            ["rise"] = SortedPairs(_riverRise),
            ["speed"] = SortedPairs(_riverSpeed),
            ["band"] = SortedPairs(_riverBand),
            ["covers"] = Convert.ToBase64String(covers.ToArray()),
            ["mask"] = _waterMask is { } mask ? Convert.ToBase64String(mask) : null,
            ["maskSignature"] = _waterMaskSignature,
        };
    }

    // ================= restore =================

    /// <summary>
    /// Puts the simulation's own state back over a freshly laid island (<see cref="IslandExpedition.LayOver"/>):
    /// actors and minds, turns, streams, lights, fire, power, water and traces, exactly as captured.
    /// </summary>
    internal void RestoreState(JsonObject o, IActorController player, IslandWorld world, FeatureIndex? features,
        HashSet<int> seen, int made)
    {
        if (_actors.Count > 0)
            throw new InvalidOperationException("The state is put back into a simulation with nobody in it yet.");

        JsonObject counters = Obj(o, "counters");
        ActionsResolved = Val<long>(counters, "actionsResolved");
        CoverChanges = Val<int>(counters, "coverChanges");
        StructureChanges = Val<int>(counters, "structureChanges");
        WaterChanges = Val<int>(counters, "waterChanges");
        TilesBurnt = Val<int>(counters, "tilesBurnt");
        FenceBreachTick = counters["fenceBreach"] is null ? null : Val<long>(counters, "fenceBreach");
        _playerDied = Val<bool>(counters, "playerDied");
        _playerEscaped = Val<bool>(counters, "playerEscaped");
        _weatherTick = Val<long>(counters, "weatherTick");
        _sparkTick = Val<long>(counters, "sparkTick");
        _ambientLight = Val<int>(counters, "ambient");
        _baseTemperature = Val<int>(counters, "baseTemperature");

        JsonObject rng = Obj(o, "rng");
        ReadRng(_injuryRng, rng["injuries"], "the injury stream");
        ReadRng(_gearRng, rng["gear"], "the gear stream");

        // the actors first: everything else may name them
        JsonArray actors = Arr(o, "actors");
        bool playerGiven = false;
        foreach (JsonNode? node in actors)
            _actors.Add(LoadActor(Obj(node, "an actor"), player, ref playerGiven, seen, made));
        JsonObject scheduler = Obj(o, "scheduler");
        _scheduler.Restore(Arr(scheduler, "queue").Select(e =>
        {
            JsonArray entry = Arr(e, "a turn");
            if (entry.Count != 3)
                throw Bad("a turn isn't (actor, tick, place)");
            int id = Val<int>(entry[0], "a turn's actor");
            if (id < 0 || id >= _actors.Count)
                throw Bad($"a turn is for actor {id}, who isn't saved");
            return (id, Val<long>(entry[1], "a turn's tick"), Val<long>(entry[2], "a turn's place"));
        }).ToList(), Val<long>(scheduler, "next"));

        JsonObject grids = Obj(o, "grids");
        LoadGrid(_occupant, Arr(grids, "occupant"));
        LoadGrid(_deckOccupant, Arr(grids, "deck"));
        LoadGrid(_vehicleAt, Arr(grids, "vehicle"));
        LoadGrid(_platformAt, Arr(grids, "platform"));
        LoadBytes(Map.Damp, Arr(grids, "damp"));
        LoadBytes(Map.Smoke, Arr(grids, "smoke"));

        LoadLights(Obj(o, "lights"));
        LoadTraces(Obj(o, "traces"));
        LoadVehicles(Arr(o, "vehicles"));
        _platforms.Clear();
        foreach (JsonNode? node in Arr(o, "platforms"))
        {
            JsonObject p = Obj(node, "a platform");
            int id = Val<int>(p, "id");
            if (id != _platforms.Count)
                throw Bad($"platform {id} is out of order");
            _platforms.Add(new Platform(id, ReadTiles(this, p["tiles"], "a platform's tiles"), Val<int>(p, "supports"))
            {
                Collapsed = Val<bool>(p, "collapsed"),
            });
        }
        // what the actors did to one another and to vehicles, and what they know, now that all of those are back
        for (int i = 0; i < actors.Count; i++)
        {
            JsonObject a = Obj(actors[i], "an actor");
            if (a["lastBite"] is { } bite)
                _actors[i].LastBite = LoadBite(Obj(bite, "a bite"));
            if (a["knowledge"] is { } knowledge)
            {
                _mapWorld = world;
                _mapFeatures ??= features ?? FeatureIndex.Build(world);
                _knowledge[i] = ReadKnowledge(Obj(knowledge, "an island map"), world);
                (long X, long Y, double Range)? last = null;
                if (a["lastSight"] is { } sight)
                {
                    JsonArray place = Arr(sight, "where an actor last looked from");
                    if (place.Count != 3)
                        throw Bad("'lastSight' isn't (x, y, range)");
                    last = (Val<long>(place[0], "lastSight x"), Val<long>(place[1], "lastSight y"), Val<double>(place[2], "lastSight range"));
                }
                RestoreLastSight(_actors[i], last);
            }
        }

        LoadFire(Obj(o, "fire"));
        if (o["power"] is { } power)
            LoadPower(Obj(power, "the power"));
        else if (Power is not null)
            throw Bad("the power isn't saved");
        LoadWater(Obj(o, "water"));

        _roomOf.Clear();
        foreach (JsonNode? node in Arr(o, "rooms"))
        {
            JsonArray pair = Arr(node, "an actor's room");
            _roomOf[Val<int>(pair[0], "an actor")] = Val<int>(pair[1], "a room");
        }
        _lightGrid = null;
    }

    private Actor LoadActor(JsonObject o, IActorController player, ref bool playerGiven, HashSet<int> seen, int made)
    {
        int id = Val<int>(o, "id");
        if (id != _actors.Count)
            throw Bad($"actor {id} is out of order");
        if (OptString(o, "space") != "surface")
            throw Bad($"actor {id} is in space '{OptString(o, "space")}', and this build has only the surface");
        string speciesName = Val<string>(o, "species");
        Species species = SnapshotSpecies.FirstOrDefault(s => s.Name == speciesName) ?? throw Bad($"there's no species '{speciesName}'");
        string glyph = Val<string>(o, "glyph");
        if (glyph.Length != 1)
            throw Bad($"actor {id}'s glyph isn't one character");
        JsonObject controller = Obj(o, "controller");
        IActorController mind;
        switch (Val<string>(controller, "type"))
        {
            case "player":
                if (playerGiven)
                    throw new Snapshots.SnapshotRefusedException("This save has two players, and only one can be given.");
                playerGiven = true;
                mind = player;
                break;
            case "human":
                mind = HumanBrain.Load(controller, this);
                break;
            case "creature":
                mind = CreatureBrain.Load(controller, this);
                break;
            case "walker":
                var walker = new RandomWalker(new Core.Rng(1));
                ReadRng(walker.Stream, controller["rng"], "a walker's stream");
                mind = walker;
                break;
            case "still":
                mind = new HoldStillController();
                break;
            default:
                throw Bad($"actor {id}'s controller is '{Val<string>(controller, "type")}', which this build doesn't know");
        }

        var actor = new Actor(id, Val<string>(o, "name"), glyph[0], ReadTile(this, o["at"], "an actor's place"), species,
            EnumOf<Direction>(o, "facing"), mind)
        {
            Body = Arr(o, "body").Select(b =>
            {
                JsonArray t = Arr(b, "a body tile");
                if (t.Count != 2)
                    throw Bad("a body tile isn't (place, part)");
                return new BodyTile(ReadTile(this, t[0], "a body tile"), EnumOf<BodyPart>(t[1], "a body part"));
            }).ToList(),
            OnDeck = Val<bool>(o, "deck"),
            LastScentTick = Val<long>(o, "lastScent"),
            LastMovedTick = Val<long>(o, "lastMoved"),
            NextActTick = Val<long>(o, "nextAct"),
            Scheduled = Val<bool>(o, "scheduled"),
            Charging = Val<bool>(o, "charging"),
            EscapedVia = o["escapedVia"] is { } via ? LoadExit(Obj(via, "an exit")) : null,
            EscapedTick = o["escapedTick"] is null ? null : Val<long>(o, "escapedTick"),
            LeftBehind = Val<bool>(o, "leftBehind"),
            ParkedLightOn = Val<bool>(o, "parkedLightOn"),
            Skills = Settings<Skills>(o["skills"], $"actor {id}'s skills"),
            CarriedLight = o["carriedLight"] is null ? null : Val<int>(o, "carriedLight"),
            LightDrainTick = Val<long>(o, "lightDrain"),
        };
        (long px, long py) = ReadPair(o, "parkedAt");
        actor.ParkedAt = new TilePos((int)(px - _islandOriginX), (int)(py - _islandOriginY));
        if (o["pendingRide"] is not null)
            actor.PendingRide = ReadPair(o, "pendingRide");
        if (o["order"] is { } orderNode)
        {
            JsonObject order = Obj(orderNode, "a standing order");
            actor.Order = new StandingOrder(EnumOf<NpcOrder>(order, "order"), Val<int>(order, "from"), Val<long>(order, "tick"));
        }
        LoadHealth(actor.Health, Obj(o, "health"));
        if (o["needs"] is { } needs)
            actor.Needs = LoadNeeds(Obj(needs, "an actor's needs"));
        foreach (JsonNode? node in Arr(o, "carried"))
        {
            JsonObject c = Obj(node, "a carried item");
            actor.Inventory.Add(ReadItem(c["item"], Catalog, seen, made), new Placement(EnumOf<CarrySlot>(c, "slot"), null, null));
        }
        return actor;
    }

    private static void LoadHealth(Health h, JsonObject o)
    {
        JsonArray sections = Arr(o, "sections");
        BodySection[] all = Enum.GetValues<BodySection>();
        if (sections.Count != all.Length)
            throw Bad("a body doesn't have its sections");
        for (int i = 0; i < all.Length; i++)
        {
            JsonArray w = Arr(sections[i], "a wound");
            if (w.Count != 3)
                throw Bad("a wound isn't (severity, bleeding, since)");
            h.Set(all[i], new Wound(EnumOf<Severity>(w[0], "a wound's severity"), Val<bool>(w[1], "bleeding"), Val<long>(w[2], "a wound's tick")));
        }
        h.BloodLossTicks = Val<long>(o, "bloodLoss");
        h.LastUpdatedTick = Val<long>(o, "updated");
        h.DeathCause = OptString(o, "death");
        h.DiedTick = Val<long>(o, "died");
    }

    private static Needs LoadNeeds(JsonObject o)
    {
        var n = new Needs(Val<int>(o, "hunger"), Val<int>(o, "thirst"), Val<int>(o, "fatigue"), 0,
            Settings<NeedProfile>(o["profile"], "a need profile"))
        {
            FedTick = Val<long>(o, "fed"),
            Stamina = Val<int>(o, "stamina"),
            Asleep = Val<bool>(o, "asleep"),
            PainkillersUntilTick = Val<long>(o, "painkillers"),
            AdrenalineUntilTick = Val<long>(o, "adrenaline"),
            Chill = Val<int>(o, "chill"),
            Wet = Val<int>(o, "wet"),
            GutOnsetTick = Val<long>(o, "gutOnset"),
            GutEndTick = Val<long>(o, "gutEnd"),
            Infection = Val<int>(o, "infection"),
            AntibioticsUntilTick = Val<long>(o, "antibiotics"),
            Minute = Val<long>(o, "minute"),
            StaminaTick = Val<long>(o, "staminaTick"),
            StaminaRemainder = Val<long>(o, "staminaRemainder"),
            WokeTick = Val<long>(o, "woke"),
            WakeCheckTick = Val<long>(o, "wakeCheck"),
        };
        CopyInto(n.CleanedTick, Arr(o, "cleaned"), "when wounds were cleaned");
        CopyInto(n.Reported, Arr(o, "reported"), "the reported stages");
        return n;
    }

    private static void CopyInto(long[] into, JsonArray from, string what)
    {
        if (from.Count != into.Length)
            throw Bad($"{what} don't have {into.Length} values");
        for (int i = 0; i < into.Length; i++)
            into[i] = Val<long>(from[i], what);
    }

    private static void CopyInto(int[] into, JsonArray from, string what)
    {
        if (from.Count != into.Length)
            throw Bad($"{what} don't have {into.Length} values");
        for (int i = 0; i < into.Length; i++)
            into[i] = Val<int>(from[i], what);
    }

    private BiteResult LoadBite(JsonObject o)
    {
        Actor ActorNumbered(int id) => id >= 0 && id < _actors.Count ? _actors[id] : throw Bad($"a bite names actor {id}, who isn't saved");
        return new BiteResult(Val<long>(o, "tick"), Val<bool>(o, "investigative"), ReadTiles(this, o["zone"], "a bite's zone"),
            Arr(o, "hits").Select(h =>
            {
                JsonArray hit = Arr(h, "a bite's hit");
                return new BiteHit(ActorNumbered(Val<int>(hit[0], "who was bitten")), ReadTile(this, hit[1], "where they were bitten"));
            }).ToList(),
            ReadTiles(this, o["cover"], "the cover bitten"),
            Arr(o, "vehicles").Select(v => VehicleNumbered(Val<int>(v, "a vehicle bitten"))).ToList());
    }

    private Vehicle VehicleNumbered(int id) =>
        id >= 0 && id < _vehicles.Count ? _vehicles[id] : throw Bad($"vehicle {id} isn't saved");

    private ExitPoint LoadExit(JsonObject o) => new(ReadTile(this, o["at"], "an exit"), Val<string>(o, "name"));

    private void LoadGrid(int[] grid, JsonArray cells)
    {
        Array.Clear(grid);
        foreach (JsonNode? node in cells)
        {
            JsonArray c = Arr(node, "a grid cell");
            TilePos p = ReadTile(this, c[0], "a grid cell");
            if (!Map.InBounds(p))
                throw Bad($"a grid cell {p} is off the map");
            grid[p.Y * Map.Width + p.X] = Val<int>(c[1], "a grid cell's value");
        }
    }

    private void LoadBytes(ByteMap map, JsonArray cells)
    {
        map.Clear();
        foreach (JsonNode? node in cells)
        {
            JsonArray c = Arr(node, "a map cell");
            TilePos p = ReadTile(this, c[0], "a map cell");
            if (!Map.InBounds(p))
                throw Bad($"a map cell {p} is off the map");
            map[p] = Val<int>(c[1], "a map cell's value");
        }
    }

    private void LoadLights(JsonObject o)
    {
        _lights.Clear();
        foreach (JsonNode? node in Arr(o, "all"))
        {
            JsonObject l = Obj(node, "a light");
            _lights.Add(new LightSource(ReadTile(this, l["at"], "a light"), Val<int>(l, "height"), Val<int>(l, "radius"),
                Val<int>(l, "intensity"), l["facing"] is null ? null : EnumOf<Direction>(l, "facing"), Val<int>(l, "cone"),
                Val<bool>(l, "on"), Val<bool>(l, "red")));
        }
        int Light(JsonNode? node, string what)
        {
            int i = Val<int>(node, what);
            return i >= 0 && i < _lights.Count ? i : throw Bad($"{what} is light {i}, which isn't saved");
        }
        _lightExpiry.Clear();
        foreach (JsonNode? node in Arr(o, "expiry"))
        {
            JsonArray e = Arr(node, "a light going out");
            _lightExpiry.Add((Light(e[0], "a light going out"), Val<long>(e[1], "when it goes out")));
        }
        _litItems.Clear();
        foreach (JsonNode? node in Arr(o, "litItems"))
        {
            int itemId = ReadInstanceId(node, "a lit item");
            _litItems.Add(FindItemAnywhere(itemId) ?? throw Bad($"lit item inst:{itemId} isn't saved"));
        }
        _itemLights.Clear();
        foreach (JsonNode? node in Arr(o, "itemLights"))
        {
            JsonArray pair = Arr(node, "an item's light");
            _itemLights[Val<int>(pair[0], "an item")] = Light(pair[1], "an item's light");
        }
        _lampLights.Clear();
        foreach (JsonNode? node in Arr(o, "lampLights"))
        {
            JsonArray pair = Arr(node, "a lamp's light");
            _lampLights[Val<int>(pair[0], "a lamp")] = Light(pair[1], "a lamp's light");
        }
        _fireLights.Clear();
        foreach (JsonNode? node in Arr(o, "fireLights"))
            _fireLights.Add(Light(node, "a fire's light"));
        JsonArray gridCache = Arr(o, "grid");
        _lightGridAmbient = Val<int>(gridCache[0], "the light grid's ambient");
        _lightGridTick = Val<long>(gridCache[1], "the light grid's tick");
        foreach (Actor actor in _actors)
        {
            if (actor.CarriedLight is { } carried && (carried < 0 || carried >= _lights.Count))
                throw Bad($"{actor.Name} carries light {carried}, which isn't saved");
        }
    }

    /// <summary>An item lying anywhere or carried by anyone, at any depth.</summary>
    private Item? FindItemAnywhere(int id) =>
        _items.All.SelectMany(w => w.Item.SelfAndNested()).Concat(_actors.SelectMany(a => a.Inventory.All)).FirstOrDefault(i => i.Id == id);

    private void LoadTraces(JsonObject o)
    {
        _noises.Clear();
        foreach (JsonNode? node in Arr(o, "noises"))
        {
            JsonObject n = Obj(node, "a noise");
            _noises.Add(new NoiseEvent(Val<long>(n, "tick"), ReadTile(this, n["at"], "a noise"), Val<int>(n, "loudness"),
                Val<string>(n, "kind"), n["actor"] is null ? null : Val<int>(n, "actor")));
        }
        _vibrations.Clear();
        foreach (JsonNode? node in Arr(o, "vibrations"))
        {
            JsonObject v = Obj(node, "a vibration");
            _vibrations.Add(new VibrationEvent(Val<long>(v, "tick"), ReadTile(this, v["at"], "a vibration"), Val<int>(v, "strength"),
                v["actor"] is null ? null : Val<int>(v, "actor")));
        }
        _blood.Clear();
        foreach (JsonNode? node in Arr(o, "blood"))
        {
            JsonObject b = Obj(node, "blood");
            _blood.Add(new BloodSpot(ReadTile(this, b["at"], "blood"), Val<long>(b, "tick")));
        }
        _scent.Clear();
        foreach (JsonNode? node in Arr(o, "scent"))
        {
            JsonObject m = Obj(node, "a scent mark");
            _scent.Add(new ScentMark(ReadTile(this, m["at"], "a scent mark"), Val<long>(m, "tick"), Val<int>(m, "actor"),
                Val<int>(m, "strength"), Val<bool>(m, "blood"), Val<long>(m, "wash")));
        }
        _ropes.Clear();
        foreach (JsonNode? node in Arr(o, "ropes"))
        {
            JsonArray r = Arr(node, "a rope");
            _ropes.Add(new RopeSpot(ReadTile(this, r[0], "a rope's top"), ReadTile(this, r[1], "a rope's bottom")));
        }
        _exits.Clear();
        foreach (JsonNode? node in Arr(o, "exits"))
            _exits.Add(LoadExit(Obj(node, "an exit")));
        _sparks.Clear();
        foreach (JsonNode? node in Arr(o, "sparks"))
        {
            JsonObject s = Obj(node, "a sparking fence");
            int light = Val<int>(s, "light");
            if (light < 0 || light >= _lights.Count)
                throw Bad($"a sparking fence has light {light}, which isn't saved");
            _sparks.Add(new SparkingFence(ReadTile(this, s["at"], "a sparking fence"), light)
            {
                Live = Val<bool>(s, "live"),
                DiedTick = s["died"] is null ? null : Val<long>(s, "died"),
            });
        }
    }

    private void LoadVehicles(JsonArray vehicles)
    {
        _vehicles.Clear();
        foreach (JsonNode? node in vehicles)
        {
            JsonObject v = Obj(node, "a vehicle");
            int id = Val<int>(v, "id");
            if (id != _vehicles.Count)
                throw Bad($"vehicle {id} is out of order");
            var under = new Dictionary<TilePos, CoverType>();
            foreach (JsonNode? u in Arr(v, "under"))
            {
                JsonArray pair = Arr(u, "what's under a vehicle");
                under[ReadTile(this, pair[0], "a tile under a vehicle")] = EnumOf<CoverType>(pair[1], "the cover under a vehicle");
            }
            var vehicle = new Vehicle(id, Val<string>(v, "name"), ReadTiles(this, v["tiles"], "a vehicle's tiles"), under)
            {
                Integrity = Val<int>(v, "integrity"),
                LeftBehind = Val<bool>(v, "leftBehind"),
            };
            foreach (JsonNode? light in Arr(v, "lights"))
                vehicle.LightIndices.Add(Val<int>(light, "a vehicle's light"));
            _vehicles.Add(vehicle);
        }
    }

    private void LoadFire(JsonObject o)
    {
        _fires.Clear();
        foreach (JsonNode? node in Arr(o, "fires"))
        {
            JsonObject f = Obj(node, "a fire");
            TilePos at = ReadTile(this, f["at"], "a fire");
            _fires[at] = new Fire(at, Val<int>(f, "intensity"), Val<int>(f, "fuel"), Val<int>(f, "max")) { StepsBurnt = Val<int>(f, "burnt") };
        }
        LoadTiles(_fuel, Arr(o, "fuel"), "spilt fuel");
        LoadTiles(_quenched, Arr(o, "quenched"), "a doused tile");
        _groundFlares.Clear();
        foreach (JsonNode? node in Arr(o, "groundFlares"))
        {
            JsonArray g = Arr(node, "a flare on the ground");
            _groundFlares.Add((ReadTile(this, g[0], "a flare on the ground"), Val<long>(g[1], "when a flare goes out")));
        }
        _burntVehicles.Clear();
        foreach (JsonNode? node in Arr(o, "burntVehicles"))
            _burntVehicles.Add(Val<int>(node, "a burnt vehicle"));
        LoadPairs(_lastBurnTick, Arr(o, "lastBurn"), "when someone last burnt");
        _tankHeat.Clear();
        foreach (JsonNode? node in Arr(o, "tankHeat"))
        {
            JsonArray pair = Arr(node, "a tank's heat");
            _tankHeat[Val<int>(pair[0], "a tank")] = Val<int>(pair[1], "its heat");
        }
        _fireTick = Val<long>(o, "tick");
        _smokeOnMap = Val<bool>(o, "smoke");
        _recentRainHour = Val<long>(o, "rainHour");
        _recentRainPercent = Val<int>(o, "rainPercent");
    }

    private void LoadTiles(Dictionary<TilePos, long> into, JsonArray from, string what)
    {
        into.Clear();
        foreach (JsonNode? node in from)
        {
            JsonArray pair = Arr(node, what);
            into[ReadTile(this, pair[0], what)] = Val<long>(pair[1], what);
        }
    }

    private static void LoadPairs(Dictionary<int, long> into, JsonArray from, string what)
    {
        into.Clear();
        foreach (JsonNode? node in from)
        {
            JsonArray pair = Arr(node, what);
            into[Val<int>(pair[0], what)] = Val<long>(pair[1], what);
        }
    }

    private void LoadPower(JsonObject o)
    {
        PowerGrid grid = Power ?? throw Bad("the save has power, and this island run has none");
        JsonArray sites = Arr(o, "sites"), devices = Arr(o, "devices"), feeders = Arr(o, "feeders"), lamps = Arr(o, "lamps");
        if (sites.Count != grid.Sites.Count || devices.Count != grid.Devices.Count || feeders.Count != grid.Feeders.Count
            || lamps.Count != grid.Lamps.Count)
            throw Bad("the saved power network isn't this island's");
        for (int i = 0; i < sites.Count; i++)
        {
            JsonObject s = Obj(sites[i], "a powered site");
            PowerSite site = grid.Sites[i];
            if (Val<int>(s, "id") != site.Id)
                throw Bad("the saved power sites aren't this island's");
            site.BoardOn = Val<bool>(s, "boardOn");
            site.AutoBackup = Val<bool>(s, "autoBackup");
            site.LocksEngaged = Val<bool>(s, "locks");
            site.HasSecurityDoors = Val<bool>(s, "securityDoors");
            site.Energised = Val<bool>(s, "energised");
            site.BusLive = Val<bool>(s, "busLive");
            site.Powered = Val<bool>(s, "powered");
            site.Source = EnumOf<PowerSource>(s, "source");
        }
        for (int i = 0; i < devices.Count; i++)
        {
            JsonObject d = Obj(devices[i], "a power device");
            PowerDevice device = grid.Devices[i];
            if (Val<int>(d, "id") != device.Id || Val<string>(d, "kind") != device.GetType().Name)
                throw Bad("the saved power devices aren't this island's");
            switch (device)
            {
                case Generator g:
                    g.State = EnumOf<GeneratorState>(d, "state");
                    g.FuelCentilitres = Val<int>(d, "fuel");
                    g.AutoStarted = Val<bool>(d, "autoStarted");
                    break;
                case FuelStore f:
                    f.Litres = Val<int>(d, "litres");
                    f.ValveOpen = Val<bool>(d, "valve");
                    break;
                case BatteryBank b:
                    b.ChargeWh = Val<int>(d, "charge");
                    b.InverterOn = Val<bool>(d, "inverter");
                    break;
                case SolarArray s:
                    s.ConditionPercent = Val<int>(d, "condition");
                    s.Connected = Val<bool>(d, "connected");
                    s.OutputW = Val<int>(d, "output");
                    break;
                case Plant p:
                    p.State = EnumOf<PlantState>(d, "state");
                    p.StartMinutesLeft = Val<int>(d, "startLeft");
                    break;
            }
        }
        for (int i = 0; i < feeders.Count; i++)
        {
            JsonObject f = Obj(feeders[i], "a feeder");
            Feeder feeder = grid.Feeders[i];
            if (Val<int>(f, "id") != feeder.Id)
                throw Bad("the saved feeders aren't this island's");
            feeder.Breaker = EnumOf<BreakerState>(f, "breaker");
            feeder.Down = Val<bool>(f, "down");
            feeder.FaultDevice = f["fault"] is null ? null : Val<int>(f, "fault");
        }
        for (int i = 0; i < lamps.Count; i++)
        {
            JsonObject l = Obj(lamps[i], "a lamp");
            PowerLamp lamp = grid.Lamps[i];
            if (Val<int>(l, "id") != lamp.Id)
                throw Bad("the saved lamps aren't this island's");
            lamp.Standalone = Val<bool>(l, "standalone");
            lamp.Broken = Val<bool>(l, "broken");
            lamp.ChargeWh = Val<int>(l, "charge");
            lamp.CapacityWh = Val<int>(l, "capacity");
            lamp.PanelW = Val<int>(l, "panel");
            lamp.Lit = Val<bool>(l, "lit");
        }
        _liveFence.Clear();
        foreach (TilePos t in ReadTiles(this, o["liveFence"], "a live fence"))
            _liveFence.Add(t);
        _powerSignature = Val<ulong>(o, "signature");
        JsonArray shock = Arr(o, "lastShock");
        _lastShock = (Val<int>(shock[0], "who was last shocked"), Val<long>(shock[1], "when"));
        _machineryTick = Val<long>(o, "machinery");
    }

    private void LoadWater(JsonObject o)
    {
        _seaLevelDm = Val<int>(o, "sea");
        LoadInts(_riverRise, Arr(o, "rise"), "a river's rise");
        LoadInts(_riverSpeed, Arr(o, "speed"), "a river's speed");
        LoadInts(_riverBand, Arr(o, "band"), "a river's band");
        byte[] covers;
        try
        {
            covers = Convert.FromBase64String(Val<string>(o, "covers"));
        }
        catch (FormatException)
        {
            throw Bad("the water's tiles aren't base64");
        }
        int k = 0;
        for (int y = 0; y < Map.Height; y++)
        {
            for (int x = 0; x < Map.Width; x++)
            {
                var p = new TilePos(x, y);
                if (Map.Water[p].Kind == WaterKind.None)
                    continue;
                if (k >= covers.Length)
                    throw Bad("there are fewer water tiles saved than this window has");
                byte cover = covers[k++];
                if (!Enum.IsDefined((CoverType)cover))
                    throw Bad($"a water tile's cover is {cover}");
                Cover[p] = (CoverType)cover;
            }
        }
        if (k != covers.Length)
            throw Bad("there are more water tiles saved than this window has");
        if (o["mask"] is null)
        {
            _waterMask = null;
        }
        else
        {
            byte[] mask;
            try
            {
                mask = Convert.FromBase64String(Val<string>(o, "mask"));
            }
            catch (FormatException)
            {
                throw Bad("the water mask isn't base64");
            }
            if (mask.Length != Map.Width * Map.Height)
                throw Bad("the water mask isn't this window's size");
            _waterMask = mask;
        }
        _waterMaskSignature = Val<ulong>(o, "maskSignature");
    }

    private static void LoadInts(Dictionary<int, int> into, JsonArray from, string what)
    {
        into.Clear();
        foreach (JsonNode? node in from)
        {
            JsonArray pair = Arr(node, what);
            into[Val<int>(pair[0], what)] = Val<int>(pair[1], what);
        }
    }
}
