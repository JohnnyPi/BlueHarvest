'use strict';
// Seed validation in the lab: a port of BlueHarvest.Sim.World.Generation.IslandStats.Measure,
// IslandWorks.Reachability and IslandChecks (steps 24a–24d), so `island validate` can be run where there
// is no .NET SDK. Reads a world made with generate({ ..., keepInternals: true }) and never changes it.
//   node tools/island-lab/checks.js            self-test: the four test islands against the rows the C# tests pin
// The pinned rows below are copied from IslandStatsTests.cs and IslandReachTests.cs; if they change there,
// copy them here too.
const G = require('./gen.js'), I = G._internal, D = I.D;
const DX = [1, 1, 0, -1, -1, -1, 0, 1], DY = [0, 1, 1, 1, 0, -1, -1, -1], SQ2 = Math.SQRT2, DL = [1, SQ2, 1, SQ2, 1, SQ2, 1, SQ2];

const LOWLAND_BELOW_M = 150, MIN_ISLET_KM2 = 0.1, SLOPE_EDGES = [5, 10, 20, 30, 38];
const WADE_DEPTH_DM = 12; // WaterLevels.WadeDepthDm
const deepAtBaseFlow = q => Math.max(0.3, 0.3 * D.pow(q, 0.4)) * 10 > WADE_DEPTH_DM;
const slopeBin = deg => { let k = 0; while (k < SLOPE_EDGES.length && deg >= SLOPE_EDGES[k]) k++; return k; };
const ANCHOR_KINDS = G.ANCHOR_KINDS2; // docks, airstrip, comms, the Court (in place of the control centre), watch posts

function grid(w) {
  if (!w._grid) throw new Error('generate the world with keepInternals: true');
  return w._grid;
}
function besideSea(land, nx, ny, i) {
  const x = i % nx, y = (i - x) / nx;
  for (let k = 0; k < 8; k++) { const xx = x + DX[k], yy = y + DY[k]; if (xx < 0 || yy < 0 || xx >= nx || yy >= ny || !land[yy * nx + xx]) return true; }
  return false;
}
function drainsToSea(T, land, nx, ny, start) {
  let i = start;
  for (let steps = 0; steps < T.n; steps++) {
    const r = T.rec[i];
    if (r === -1) return T.mouth[i] >= 0 || besideSea(land, nx, ny, i);
    if (r < 0) return false;
    i = r;
  }
  return false;
}
// the facility table the stats count against: the program's (program 2 brings its own)
function facilityTable(w) { return w.works.facilityTable || G.FAC; }
function paddocksWanted(w) { return w.params.paddockSize > 0 ? (w.works.paddocksWanted || G.PADDOCKS.length) : 0; }

/** IslandStats.Measure */
function measure(w) {
  const g = grid(w), nx = w.nx, ny = w.ny, n = nx * ny, cell = w.cell, cellKm2 = cell * cell / 1e6, land = w.land, W = w.works, WG = W.walkGrid;
  const label = new Int32Array(n), pieces = I.components(land, nx, ny, label);
  let main = -1; for (let k = 0; k < pieces.length; k++) if (main < 0 || pieces[k] > pieces[main]) main = k;
  const others = []; for (let k = 0; k < pieces.length; k++) if (k !== main) others.push(pieces[k] * cellKm2);
  others.sort((a, b) => b - a);
  const wn = WG.cnx * WG.cny, walkMask = new Uint8Array(wn); for (let k = 0; k < wn; k++) walkMask[k] = WG.pass[k] ? 1 : 0;
  const walkLabel = new Int32Array(wn), patches = I.components(walkMask, WG.cnx, WG.cny, walkLabel);
  let walkMain = -1; for (let k = 0; k < patches.length; k++) if (walkMain < 0 || patches[k] > patches[walkMain]) walkMain = k;
  const walkCellAt = (x, y) => I.clamp(Math.floor(y / WG.cs), 0, WG.cny - 1) * WG.cnx + I.clamp(Math.floor(x / WG.cs), 0, WG.cnx - 1);
  let landCells = 0, dryCells = 0, lowland = 0, walkable = 0, walkConnected = 0, coast = 0, beach = 0, rocky = 0, cliff = 0, mainCoast = 0, mainBeach = 0;
  let pits = 0, coastalPits = 0, deep = 0, highest = 0, largestQ = 0;
  const slopes = new Array(SLOPE_EDGES.length + 1).fill(0), T = g.T;
  for (let i = 0; i < n; i++) {
    if (!land[i]) continue;
    landCells++;
    const onMain = label[i] === main, cc = g.coast[i];
    if (cc !== 0) { coast++; if (onMain) mainCoast++; if (cc === 1) { beach++; if (onMain) mainBeach++; } else if (cc === 2) rocky++; else if (cc === 3) cliff++; }
    if (T.rec[i] === -1 && T.mouth[i] < 0) { pits++; if (besideSea(land, nx, ny, i)) coastalPits++; }
    if (g.isRiver[i]) { const q = g.Q[i]; if (q > largestQ) largestQ = q; if (deepAtBaseFlow(q)) deep++; }
    if (!isNaN(g.lake[i])) continue;
    dryCells++;
    const h = g.H[i]; if (h > highest) highest = h; if (h < LOWLAND_BELOW_M) lowland++;
    slopes[slopeBin(g.slopeD[i])]++;
    const wc = walkCellAt((i % nx + 0.5) * cell, (Math.floor(i / nx) + 0.5) * cell);
    if (WG.pass[wc]) { walkable++; if (walkLabel[wc] === walkMain) walkConnected++; }
  }
  let toSea = 0; for (const r of w.rivers) if (drainsToSea(T, land, nx, ny, r.head)) toSea++;
  const facilities = facilityTable(w).map(F => ({ key: F.key, name: F.name, major: !!F.major, wanted: F.count, placed: W.sites.filter(t => t.kind === F.key).length }));
  facilities.forEach(f => { f.short = Math.max(0, f.wanted - f.placed); });
  const S = {
    seed: w.params.seed, scale: w.params.scale, cells: n, cellKm2, landCells, dryCells, mainKm2: main < 0 ? 0 : pieces[main] * cellKm2, otherLandKm2: others,
    rivers: w.rivers.length, riversToSea: toSea, largestRiverQ: largestQ, deepRiverCells: deep, lakes: w.lakes.length, lakeKm2: w.lakes.reduce((a, l) => a + l.area, 0),
    pitCells: pits, coastalPitCells: coastalPits, slopeCells: slopes, highestM: highest, lowlandCells: lowland, walkableCells: walkable, walkableConnectedCells: walkConnected,
    coastCells: coast, beachCells: beach, rockyCells: rocky, cliffCells: cliff, mainCoastCells: mainCoast, mainBeachCells: mainBeach,
    facilities, paddocksWanted: paddocksWanted(w), paddocksPlaced: W.paddocks.length, roadKm: W.roadKm, mainRoadKm: W.mainKm, bridges: W.bridges, tunnels: W.tunnels
  };
  S.landFraction = S.landCells / S.cells; S.landKm2 = S.landCells * cellKm2; S.islets = others.filter(a => a >= MIN_ISLET_KM2).length; S.largestIsletKm2 = others.length ? others[0] : 0;
  S.riversToSeaShare = S.rivers ? S.riversToSea / S.rivers : 0; S.inlandPitCells = S.pitCells - S.coastalPitCells;
  S.lowlandShare = dryCells ? lowland / dryCells : 0; S.walkableShare = dryCells ? walkable / dryCells : 0; S.walkableConnectedShare = dryCells ? walkConnected / dryCells : 0;
  S.steepShare = dryCells ? (slopes[slopes.length - 1] + slopes[slopes.length - 2]) / dryCells : 0;
  S.beachShare = mainCoast ? mainBeach / mainCoast : 0; S.beachShareAll = coast ? beach / coast : 0;
  S.facilitiesShort = facilities.reduce((a, f) => a + f.short, 0); S.paddocksShort = Math.max(0, S.paddocksWanted - S.paddocksPlaced);
  return S;
}

/**
 * IslandWorks.TravelCore: minutes between the listed walking-grid cells (walk[a][b] from tcells[a] to tcells[b]) with some
 * cells shut. sources limits which rows are walked (all of them by default); the others stay infinite.
 */
function travelCore(w, tcells, blocked, sources) {
  const WG = w.works.walkGrid, { cnx, cny, cs, ch, pass, spd, wade, onRoad } = WG, cn = cnx * cny, targets = tcells, sites = tcells.map((_, k) => k);
  const tob = sl => 6 * D.exp(-3.5 * Math.abs(sl + 0.05)) / 3.6 / (6 * D.exp(-3.5 * 0.05) / 3.6) * 1.4;
  const cuts = (x, y, xx, yy) => { const a = y * cnx + xx, b = yy * cnx + x, sA = blocked[a] !== 0, sB = blocked[b] !== 0; return (sA || sB) && (sA || !pass[a]) && (sB || !pass[b]); };
  const ns = sites.length, walk = sites.map(() => new Array(ns).fill(Infinity));
  const wd = new Float64Array(cn), wdone = new Uint8Array(cn), wh = new I.Heap(cn * 8);
  for (let a = 0; a < ns; a++) {
    if (sources && !sources.includes(a)) continue;
    wd.fill(Infinity); wdone.fill(0); wh.n = 0;
    const s0 = targets[sites[a]];
    if (!pass[s0] || (blocked && blocked[s0])) continue;
    wd[s0] = 0; wh.push(0, s0); let left = ns;
    while (wh.n > 0 && left > 0) {
      const c = wh.pop(); if (wdone[c]) continue; wdone[c] = 1;
      for (let b = 0; b < ns; b++) if (targets[sites[b]] === c && walk[a][b] === Infinity) { walk[a][b] = wd[c] / 60; left--; }
      const x = c % cnx, y = (c - x) / cnx;
      for (let k = 0; k < 8; k++) {
        const xx = x + DX[k], yy = y + DY[k]; if (xx < 0 || yy < 0 || xx >= cnx || yy >= cny) continue;
        const j = yy * cnx + xx; if (!pass[j] || wdone[j]) continue;
        if (blocked && (blocked[j] || (k % 2 === 1 && cuts(x, y, xx, yy)))) continue;
        const d = DL[k] * cs, sl = (ch[j] - ch[c]) / d, roads = onRoad[c] && onRoad[j];
        if (Math.abs(sl) > 0.8 && !roads) continue;
        const v = tob(roads ? sl * 0.6 : sl) * (spd[c] + spd[j]) / 2, nd = wd[c] + d / Math.max(0.05, v) + wade[j];
        if (nd < wd[j]) { wd[j] = nd; wh.push(nd, j); }
      }
    }
  }
  return walk;
}

/**
 * IslandWorks.Reachability: walking routes between every pair of sites of these kinds (or the listed site
 * indices), each also timed with road tunnels shut, deep rivers shut, and every river shut.
 */
// what shuts each walking-grid cell: road tunnels, rivers too deep to wade at base flow, and every river over 1 m³/s
function shutMasks(w) {
  const W = w.works, WG = W.walkGrid, g = grid(w), nx = w.nx, ny = w.ny, cell = w.cell, cn = WG.cnx * WG.cny;
  const f = Math.max(1, D.round(256 / cell)), tunnel = new Uint8Array(cn), deep = new Uint8Array(cn), river = new Uint8Array(cn);
  const dry = i => w.land[i] && isNaN(g.lake[i]);
  for (let c = 0; c < cn; c++) {
    if (!WG.pass[c]) continue;
    const cx = c % WG.cnx, cy = (c - cx) / WG.cnx; let lc = 0, cliffy = 0, tot = 0, rq = 0, surfaceRoad = false;
    for (let y = cy * f; y < Math.min(ny, cy * f + f); y++) for (let x = cx * f; x < Math.min(nx, cx * f + f); x++) {
      const i = y * nx + x; tot++;
      if (W.roadCell[i] && !W.tunnelCell[i]) surfaceRoad = true;
      if (!dry(i)) continue;
      lc++; if (g.isRiver[i] && g.Q[i] > 1) rq = Math.max(rq, g.Q[i]); if (g.slopeD[i] > 38) cliffy++;
    }
    if (!surfaceRoad && (lc * 2 < tot || cliffy * 2 > lc)) tunnel[c] = 1;
    if (!surfaceRoad && rq > 0) { river[c] = 1; if (deepAtBaseFlow(rq)) deep[c] = 1; }
  }
  return { tunnel, deep, river };
}
// the five timings Reachability compares, between walking-grid cells (a listed cell is never shut)
function timings(w, tcells, sources) {
  const WG = w.works.walkGrid, cn = WG.cnx * WG.cny, { tunnel, deep, river } = shutMasks(w);
  const shut = (...masks) => { const m = new Uint8Array(cn); for (let c = 0; c < cn; c++) for (const k of masks) if (k[c]) m[c] = 1; for (const t of tcells) m[t] = 0; return m; };
  return { any: travelCore(w, tcells, null, sources), noTunnel: travelCore(w, tcells, shut(tunnel), sources), noDeep: travelCore(w, tcells, shut(deep), sources),
    neither: travelCore(w, tcells, shut(tunnel, deep), sources), dry: travelCore(w, tcells, shut(tunnel, river), sources) };
}
function routeStatus(WG, T, a, b, ta, tb) {
  if (T.any[a][b] === Infinity) return !WG.pass[ta] || !WG.pass[tb] ? 'NoGround' : 'NoRoute';
  if (T.neither[a][b] !== Infinity) return 'Ok';
  if (T.noTunnel[a][b] !== Infinity) return 'NeedsDeepWater';
  if (T.noDeep[a][b] !== Infinity) return 'NeedsTunnel';
  return 'NeedsBoth';
}
/** The walking-grid cell a point is walked from: its own, or the nearest passable one within 5 cells (as the lab's site targets). */
function targetCell(w, x, y) {
  const WG = w.works.walkGrid, cnx = WG.cnx, cny = WG.cny;
  const c0 = I.clamp(Math.floor(y / WG.cs), 0, cny - 1) * cnx + I.clamp(Math.floor(x / WG.cs), 0, cnx - 1); if (WG.pass[c0]) return c0;
  const x0 = c0 % cnx, y0 = (c0 - x0) / cnx;
  for (let rr = 1; rr < 6; rr++) for (let yy = y0 - rr; yy <= y0 + rr; yy++) for (let xx = x0 - rr; xx <= x0 + rr; xx++)
    if (xx >= 0 && yy >= 0 && xx < cnx && yy < cny && WG.pass[yy * cnx + xx]) return yy * cnx + xx;
  return c0;
}
/** Routes from the first point to each of the others: [{ status, minutes, minutesAnyWay, mustFord }], one per other point. */
function reachFrom(w, points) {
  const WG = w.works.walkGrid, tc = points.map(([x, y]) => targetCell(w, x, y)), T = timings(w, tc, [0]);
  return tc.slice(1).map((t, k) => { const b = k + 1, status = routeStatus(WG, T, 0, b, tc[0], t);
    return { status, minutes: status === 'Ok' ? T.neither[0][b] : Infinity, minutesAnyWay: T.any[0][b], mustFord: status === 'Ok' && T.dry[0][b] === Infinity }; });
}
function reach(w, kindsOrSites) {
  const W = w.works, WG = W.walkGrid;
  const sites = typeof kindsOrSites[0] === 'number' ? kindsOrSites.slice() : W.sites.map((t, a) => kindsOrSites.includes(t.kind) ? a : -1).filter(a => a >= 0);
  const tc = sites.map(s => WG.targets[s]), Tm = timings(w, tc);
  const any = Tm.any, noTunnel = Tm.noTunnel, noDeep = Tm.noDeep, neither = Tm.neither, dryW = Tm.dry;
  const routes = [];
  for (let a = 0; a < sites.length; a++) for (let b = a + 1; b < sites.length; b++) {
    let status;
    if (any[a][b] === Infinity) status = !WG.pass[WG.targets[sites[a]]] || !WG.pass[WG.targets[sites[b]]] ? 'NoGround' : 'NoRoute';
    else if (neither[a][b] !== Infinity) status = 'Ok';
    else if (noTunnel[a][b] !== Infinity) status = 'NeedsDeepWater';
    else if (noDeep[a][b] !== Infinity) status = 'NeedsTunnel';
    else status = 'NeedsBoth';
    const ok = status === 'Ok', mustFord = ok && dryW[a][b] === Infinity;
    routes.push({ fromSite: sites[a], toSite: sites[b], from: W.sites[sites[a]].name, to: W.sites[sites[b]].name, status,
      minutes: ok ? neither[a][b] : Infinity, minutesAnyWay: any[a][b], mustFord, dryMinutes: ok && !mustFord ? dryW[a][b] : Infinity });
  }
  const R = { anchorSites: sites, anchors: sites.map(s => W.sites[s].name), routes };
  R.gaps = routes.filter(r => r.status !== 'Ok').length;
  R.tunnelGaps = routes.filter(r => r.status === 'NeedsTunnel' || r.status === 'NeedsBoth').length;
  R.deepGaps = routes.filter(r => r.status === 'NeedsDeepWater' || r.status === 'NeedsBoth').length;
  R.groundGaps = routes.filter(r => r.status === 'NoGround' || r.status === 'NoRoute').length;
  R.mustFord = routes.filter(r => r.mustFord).length; R.fullyConnected = R.gaps === 0;
  R.slowestMinutes = routes.filter(r => r.status === 'Ok').reduce((m, r) => Math.max(m, r.minutes), 0);
  const det = routes.filter(r => r.status === 'Ok' && !r.mustFord && r.minutes > 0).map(r => r.dryMinutes / r.minutes);
  R.worstDryDetour = det.length ? Math.max(...det) : 1;
  return R;
}

// ---------- IslandChecks ----------
const T24 = { landMin: 0.22, landMax: 0.42, lowlandMin: 0.30, beachMin: 0.15, walkConnMin: 0.93, isletsMin: 3, isletsMax: 8 };
const P = v => fmt(100 * v, 1) + '%';
function missing(S, major) {
  const shortOf = S.facilities.filter(f => f.short > 0 && (major ? f.major || f.placed === 0 : !f.major && f.placed > 0));
  return shortOf.length ? shortOf.map(f => `${f.name} ${f.placed} of ${f.wanted}`).join(', ') + ' placed' : null;
}
function routesReason(R) {
  if (R.fullyConnected) return null;
  const parts = [];
  if (R.tunnelGaps) parts.push(`${R.tunnelGaps} need a tunnel`); if (R.deepGaps) parts.push(`${R.deepGaps} cross deep water`); if (R.groundGaps) parts.push(`${R.groundGaps} have no ground or no route`);
  return `${R.gaps} of ${R.routes.length} key-site pairs can't be walked (${parts.join(', ')})`;
}
const CHECKS = [
  ['land', 'Fail', c => c.S.landFraction < T24.landMin || c.S.landFraction > T24.landMax ? `land ${P(c.S.landFraction)} of the frame, need ${P(T24.landMin)}–${P(T24.landMax)}` : null],
  ['lowland', 'Fail', c => c.S.lowlandShare < T24.lowlandMin ? `lowland ${P(c.S.lowlandShare)} of dry land, need ${P(T24.lowlandMin)}` : null],
  ['beach', 'Fail', c => c.S.beachShare < T24.beachMin ? `beach ${P(c.S.beachShare)} of the coast, need ${P(T24.beachMin)}` : null],
  ['walkable', 'Fail', c => c.S.walkableConnectedShare < T24.walkConnMin ? `walkable ground ${P(c.S.walkableConnectedShare)} of dry land, need ${P(T24.walkConnMin)}` : null],
  ['facilities', 'Fail', c => missing(c.S, true)],
  ['facilities-minor', 'Warn', c => missing(c.S, false)],
  ['paddocks', 'Fail', c => c.S.paddocksShort > 0 ? `${c.S.paddocksPlaced} of ${c.S.paddocksWanted} paddocks placed` : null],
  ['anchors', 'Fail', c => routesReason(c.R)],
  ['rivers', 'Warn', c => c.S.riversToSea < c.S.rivers || c.S.inlandPitCells > 0 ? `${c.S.rivers - c.S.riversToSea} rivers don't reach the sea, ${c.S.inlandPitCells} inland pit cells` : null],
  ['islets', 'Warn', c => c.S.islets < T24.isletsMin || c.S.islets > T24.isletsMax ? `${c.S.islets} islets, the design asks for ${T24.isletsMin}–${T24.isletsMax}` : null]
];
/** IslandChecks.Run, plus any extra checks ([key, 'Fail' | 'Warn', ctx => reason | null]). */
function validate(ctx, extra) {
  const failures = [], warnings = [];
  for (const [key, sev, test] of CHECKS.concat(extra || [])) { const reason = test(ctx); if (reason == null) continue; (sev === 'Fail' ? failures : warnings).push({ key, reason }); }
  return { failures, warnings, passed: failures.length === 0 };
}

// ---------- rows, formatted as IslandReport.StatsRow / ReachRow ----------
// .NET formats a double for a custom format string from 15 significant digits, then rounds half away from zero.
function fmt(v, decimals) {
  if (v === Infinity) return '∞'; if (v === -Infinity) return '-∞'; if (Number.isNaN(v)) return 'NaN';
  if (v === 0) return '0';
  const neg = v < 0, e = Number(Math.abs(v).toPrecision(15)).toString();
  let [ip, fp = ''] = (e.includes('e') ? Math.abs(Number(e)).toFixed(20) : e).split('.');
  if (fp.length > decimals) {
    const up = fp.charCodeAt(decimals) >= 53; fp = fp.slice(0, decimals);
    if (up) { let digits = (ip + fp).split('').map(Number), k = digits.length - 1; while (k >= 0) { if (digits[k] < 9) { digits[k]++; break; } digits[k] = 0; k--; } if (k < 0) digits.unshift(1); const s = digits.join(''); ip = s.slice(0, s.length - fp.length); fp = s.slice(s.length - fp.length); }
  }
  fp = fp.replace(/0+$/, '');
  const out = fp ? ip + '.' + fp : ip;
  return neg && out !== '0' ? '-' + out : out;
}
const f6 = v => fmt(v, 6);
const STATS_COLS = [
  ['land_pct', s => 100 * s.landFraction], ['land_km2', s => s.landKm2], ['main_km2', s => s.mainKm2], ['islets', s => s.islets], ['largest_islet_km2', s => s.largestIsletKm2],
  ['rivers', s => s.rivers], ['rivers_to_sea_pct', s => 100 * s.riversToSeaShare], ['largest_river_q', s => s.largestRiverQ], ['deep_river_cells', s => s.deepRiverCells],
  ['lakes', s => s.lakes], ['lake_km2', s => s.lakeKm2], ['pit_cells', s => s.pitCells], ['highest_m', s => s.highestM], ['lowland_pct', s => 100 * s.lowlandShare],
  ['steep_pct', s => 100 * s.steepShare], ['walkable_pct', s => 100 * s.walkableShare], ['walkable_connected_pct', s => 100 * s.walkableConnectedShare],
  ['beach_pct', s => 100 * s.beachShare], ['beach_all_pct', s => 100 * s.beachShareAll], ['facilities_short', s => s.facilitiesShort], ['paddocks_placed', s => s.paddocksPlaced],
  ['road_km', s => s.roadKm], ['bridges', s => s.bridges], ['tunnels', s => s.tunnels], ['coastal_pit_cells', s => s.coastalPitCells], ['inland_pit_cells', s => s.inlandPitCells]
];
function statsRow(S) {
  const keys = STATS_COLS.map(c => c[0]), vals = STATS_COLS.map(c => f6(c[1](S)));
  S.slopeCells.forEach((v, b) => { keys.push(`slope_bin${b}_cells`); vals.push(String(v)); });
  S.facilities.forEach(f => { keys.push(`placed_${f.key}`); vals.push(String(f.placed)); });
  return { keys, vals, get(k) { return vals[keys.indexOf(k)]; } };
}
const REACH_KEYS = ['anchors', 'anchor_pairs', 'anchor_gaps', 'tunnel_gaps', 'deep_gaps', 'no_ground_gaps', 'must_ford_pairs', 'slowest_anchor_minutes', 'worst_dry_detour'];
const reachRow = R => [R.anchors.length, R.routes.length, R.gaps, R.tunnelGaps, R.deepGaps, R.groundGaps, R.mustFord].map(String).concat([f6(R.slowestMinutes), f6(R.worstDryDetour)]);

module.exports = { measure, reach, reachFrom, targetCell, validate, travelCore, statsRow, reachRow, REACH_KEYS, ANCHOR_KINDS, CHECKS, T24, fmt, deepAtBaseFlow };

// ---------- self-test against the C# pins ----------
if (require.main === module) {
  const CASES = { Default: {}, Seed12: { seed: 12, scale: 75 }, SmallNoCliffs: { seed: 44, scale: 50, wind: 100, cliffs: 0, iterations: 5, pali: 0.5 }, CoolWet: { seed: 9, scale: 60, seaTemp: 21, cloudBase: 500, wetlands: 1.5, rain: 1.3 } };
  const STATS_PINNED_COLS = 'land_pct,land_km2,main_km2,islets,largest_islet_km2,rivers,rivers_to_sea_pct,largest_river_q,deep_river_cells,lakes,lake_km2,pit_cells,highest_m,lowland_pct,steep_pct,walkable_pct,walkable_connected_pct,beach_pct,beach_all_pct,facilities_short,paddocks_placed,road_km,bridges,tunnels,slope_bin0_cells,slope_bin1_cells,slope_bin2_cells,slope_bin3_cells,slope_bin4_cells,slope_bin5_cells,placed_court,placed_docks,placed_airstrip,placed_comms,placed_genetics,placed_raptors,placed_orgone,placed_orchard,placed_weather,placed_choir,placed_drowned,placed_geothermal,placed_depot,placed_watch,placed_yard,placed_survey,placed_outstation,coastal_pit_cells,inland_pit_cells'.split(',');
  const STATS_PINNED = { // IslandStatsTests.Pinned
    Default: '31.52579,1679.966208,1671.839744,3,5.111808,150,100,6.06677,0,1,0.294912,0,1162.126102,42.403847,2.467835,98.387616,97.924287,34.134078,33.907147,0,5,189.765291,2,1,56315,29163,13298,1213,913,1617,1,2,1,1,1,1,1,1,1,1,1,1,2,3,1,4,2,0,0',
    Seed12: '28.518409,854.8352,852.819968,2,1.622016,102,100,2.017199,0,1,0.57344,0,1191.598906,37.491369,3.716916,97.543153,97.332183,24.569241,25.077208,0,5,157.355163,4,5,17099,20318,11461,1324,683,1255,1,2,1,1,1,1,1,1,1,1,1,1,2,3,1,4,5,0,0',
    SmallNoCliffs: '30.999115,412.975104,409.321472,3,1.916928,44,100,1.882016,0,2,0.57344,1,1153.002823,36.800286,1.557348,99.555044,98.72075,17.216643,15.714286,0,5,99.936575,6,2,5672,6695,10028,2384,341,51,1,2,1,1,1,1,1,1,1,1,1,1,2,3,1,4,5,1,0',
    CoolWet: '33.125272,636.469248,630.767616,9,1.343488,105,100,2.955048,0,1,0.589824,0,1178.215784,39.792842,4.243642,97.191518,96.47265,22.502031,21.901408,0,5,123.011504,8,2,12430,12668,10511,1555,602,1045,1,2,1,1,1,1,1,1,1,1,1,1,2,3,1,4,3,0,0'
  };
  const REACH_PINNED = { // IslandReachTests.Pinned
    Default: '8,28,0,0,0,0,0,922.335711,1.147902',
    Seed12: '8,28,0,0,0,0,0,772.858298,1.062727',
    SmallNoCliffs: '8,28,0,0,0,0,0,395.148648,1.01727',
    CoolWet: '8,28,0,0,0,0,0,560.053636,1'
  };
  if (process.argv.includes('--print')) { // new pins, for IslandStatsTests, IslandReachTests and this file
    for (const [name, p] of Object.entries(CASES)) {
      const w = G.generate(Object.assign({ keepInternals: true }, p)), row = statsRow(measure(w)), R = reach(w, ANCHOR_KINDS);
      if (name === 'Default') console.log('columns: ' + row.keys.join(','));
      console.log(`stats ${name}: ${row.vals.join(',')}`);
      console.log(`reach ${name}: ${reachRow(R).join(',')}`);
    }
    process.exit(0);
  }
  let bad = 0;
  for (const [name, p] of Object.entries(CASES)) {
    const w = G.generate(Object.assign({ keepInternals: true }, p)), S = measure(w), row = statsRow(S), R = reach(w, ANCHOR_KINDS);
    const want = STATS_PINNED[name].split(','), wrong = [];
    STATS_PINNED_COLS.forEach((k, j) => { if (row.get(k) !== want[j]) wrong.push(`${k}: pinned ${want[j]}, lab ${row.get(k)}`); });
    const rr = reachRow(R).join(','); if (rr !== REACH_PINNED[name]) wrong.push(`reach: pinned ${REACH_PINNED[name]}, lab ${rr}`);
    // with nothing shut the routes must equal the generator's own walk table (IslandReachTests)
    for (const r of R.routes) if (r.minutesAnyWay !== w.works.walk[r.fromSite][r.toSite]) wrong.push(`${r.from}–${r.to}: walk table differs`);
    const V = validate({ S, R });
    console.log(`${name}: ${wrong.length ? 'DIFFERS' : 'matches'} the C# pins (${STATS_PINNED_COLS.length} stats columns, ${REACH_KEYS.length} reach columns); checks ${V.passed ? 'pass' : 'FAIL ' + V.failures.map(f => f.key).join(',')}${V.warnings.length ? '; warnings ' + V.warnings.map(f => f.key).join(',') : ''}`);
    wrong.forEach(x => console.log('   ' + x)); bad += wrong.length;
  }
  console.log(bad ? `${bad} differences` : 'All four test islands match.');
  process.exit(bad ? 1 : 0);
}
