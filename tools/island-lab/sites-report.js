'use strict';
// P4a: the Crosswake Island site program (gen.js program 2) over many seeds. For each seed: how often each terrain role
// is found and how big it is, where every site went and how far apart, what failed and why, the walks between the Court
// and every sector, facility-area sizes and generation time; then the program-2 seed checks (the lab's version of P4e).
//   node tools/island-lab/sites-report.js [first seed] [count] [scale] [--out folder] [--workers n]
// Defaults: seeds 1–200 at 100%. Writes report.md, seeds.csv and summary.json to --out (default reports/sites-<time>).
const path = require('path'), fs = require('fs');
const { Worker, isMainThread, parentPort } = require('worker_threads');

// ---------- thresholds of the lab's program-2 checks (P4e will set the C# ones from this run) ----------
const TRAVEL_WARN_H = 20;      // slowest Court–sector walk on foot, hours
const BEACH_MIN_CELLS = 4;     // a shipwreck beach: a stretch of beach coast at least this many 128 m cells long…
const BEACH_CLEAR_M = 2000;    // …at least this far from any compound, and walkable from the Court
const START_SITES_MIN = 3;
const ROLE_FOUND_MIN = 0.97;   // a role found on fewer seeds than this needs a fallback anchor (the P4 gate's 97%)

if (!isMainThread) {
  const G = require('./gen.js'), C = require('./checks.js'), D = G._internal.D;
  parentPort.on('message', ({ seed, scale }) => {
    const t0 = process.hrtime.bigint();
    try { parentPort.postMessage(one(G, C, D, seed, scale, t0)); }
    catch (e) { parentPort.postMessage({ seed, error: e.stack || String(e) }); }
  });
  return;
}

function one(G, C, D, seed, scale, t0) {
  const w = G.generate({ seed, scale, program: 2, keepInternals: true });
  const genSec = Number(process.hrtime.bigint() - t0) / 1e9;
  const W = w.works, P = W.program2, sites = W.sites, nx = w.nx, ny = w.ny, cell = w.cell, area = cell * cell / 1e6;
  const court = sites.find(t => t.kind === 'court');
  const SECTOR_SITE = { MEN: 'genetics', VIT: 'orgone', ORC: 'orchard', WTH: 'weather', CHO: 'choir', DRW: 'drowned' };
  const secSite = code => sites.find(t => t.kind === SECTOR_SITE[code]);
  const checks = []; // [key, 'Fail'|'Warn', reason]
  const chk = (key, sev, reason) => { if (reason) checks.push({ key, sev, reason }); };

  // ---- the step-24 checks on the new facility table and anchors ----
  const S = C.measure(w), R24 = C.reach(w, W.anchorKinds), V24 = C.validate({ S, R: R24 });
  V24.failures.forEach(f => chk(f.key, 'Fail', f.reason)); V24.warnings.forEach(f => chk(f.key, 'Warn', f.reason));

  // ---- sectors and the Court ----
  const missing = Object.keys(SECTOR_SITE).filter(c => !secSite(c)).concat(court ? [] : ['Court']);
  chk('sectors', 'Fail', missing.length ? 'missing: ' + missing.join(', ') : null);

  // ---- walks from the Court: every sector, every authored ruin's perimeter, every candidate shipwreck beach ----
  const pts = [[court.x, court.y]], what = [];
  for (const c of Object.keys(SECTOR_SITE)) { const t = secSite(c); if (t) { pts.push([t.x, t.y]); what.push({ type: 'sector', code: c, site: t.id }); } }
  for (const q of P.ruins) if (q.placed && q.id !== 'RUIN-07') { pts.push(q.landing || [q.x, q.y]); what.push({ type: 'ruin', id: q.id }); }
  // beaches: stretches of beach coast on the main island
  const land = w.land, coast = w._grid.coast, lbl = new Int32Array(nx * ny), sizes = G._internal.components(land, nx, ny, lbl);
  let main = 0; sizes.forEach((v, k) => { if (v > sizes[main]) main = k; });
  const bm = new Uint8Array(nx * ny); for (let i = 0; i < nx * ny; i++) if (coast[i] === 1 && lbl[i] === main) bm[i] = 1;
  const bl = new Int32Array(nx * ny), bsz = G._internal.components(bm, nx, ny, bl), stretches = [];
  for (let k = 0; k < bsz.length; k++) {
    if (bsz[k] < BEACH_MIN_CELLS) continue;
    let sx = 0, sy = 0; const cells = []; for (let i = 0; i < nx * ny; i++) if (bl[i] === k) { cells.push(i); sx += (i % nx + 0.5) * cell; sy += (Math.floor(i / nx) + 0.5) * cell; }
    const mx = sx / cells.length, my = sy / cells.length; let bi = cells[0], bd = 1e30;
    for (const i of cells) { const d = D.hyp((i % nx + 0.5) * cell - mx, (Math.floor(i / nx) + 0.5) * cell - my); if (d < bd) { bd = d; bi = i; } }
    const x = (bi % nx + 0.5) * cell, y = (Math.floor(bi / nx) + 0.5) * cell, clear = sites.every(t => D.hyp(t.x - x, t.y - y) >= BEACH_CLEAR_M);
    stretches.push({ x, y, km: bsz[k] * cell / 1000, clear });
  }
  for (const b of stretches) if (b.clear) { pts.push([b.x, b.y]); what.push({ type: 'beach', km: b.km }); }
  const routes = C.reachFrom(w, pts);
  const courtWalk = {}, ruinWalk = {}; let beachesOk = 0;
  routes.forEach((r, k) => {
    const it = what[k];
    if (it.type === 'sector') courtWalk[it.code] = { status: r.status, hours: r.minutes / 60, anyHours: r.minutesAnyWay / 60, transitHours: W.walkTransit[court.id][it.site] / 60, mustFord: r.mustFord };
    else if (it.type === 'ruin') ruinWalk[it.id] = { status: r.status, hours: r.minutes / 60 };
    else if (r.status === 'Ok') beachesOk++;
  });
  const badSec = Object.entries(courtWalk).filter(([, v]) => v.status !== 'Ok').map(([c, v]) => `${c} ${v.status}`);
  chk('court-routes', 'Fail', badSec.length ? 'Court to ' + badSec.join(', ') : null);
  const slowest = Math.max(0, ...Object.values(courtWalk).filter(v => v.status === 'Ok').map(v => v.hours));
  chk('travel', 'Warn', slowest > TRAVEL_WARN_H ? `slowest Court–sector walk ${slowest.toFixed(1)} h, over ${TRAVEL_WARN_H} h` : null);
  chk('start-sites', 'Fail', beachesOk < START_SITES_MIN ? `${beachesOk} shipwreck beaches, need ${START_SITES_MIN}` : null);

  // ---- facility areas: sizes, and whether each ruin is in its sector's area or a neighbour of it ----
  const FA = G.facilityAreas(w), areaKm2 = new Float64Array(sites.length), nb = sites.map(() => new Set());
  for (let i = 0; i < nx * ny; i++) {
    const a = FA.area[i]; if (a < 0) continue; areaKm2[a] += area;
    const x = i % nx; if (x + 1 < nx) { const b = FA.area[i + 1]; if (b >= 0 && b !== a) { nb[a].add(b); nb[b].add(a); } }
    if (i + nx < nx * ny) { const b = FA.area[i + nx]; if (b >= 0 && b !== a) { nb[a].add(b); nb[b].add(a); } }
  }
  const ruinOut = P.ruins.map(q => {
    const o = { id: q.id, role: q.role, placed: q.placed, reason: q.reason || null, inBand: q.inBand || 0, km: q.fromSector ? q.fromSector / 1000 : 0 };
    if (q.placed && q.id !== 'RUIN-07') {
      const a = FA.area[q.cell], home = q.sectorSite; o.area = a === home ? 'same' : a >= 0 && nb[home].has(a) ? 'neighbour' : a < 0 ? 'none (offshore)' : 'other';
      if (q.role === 'reef') { const la = FA.area[Math.min(ny - 1, Math.floor(q.landing[1] / cell)) * nx + Math.min(nx - 1, Math.floor(q.landing[0] / cell))]; o.landingArea = la === home ? 'same' : la >= 0 && nb[home].has(la) ? 'neighbour' : 'other'; o.approach = q.approach; o.causewayM = q.causewayM; }
      o.walk = ruinWalk[q.id];
    }
    return o;
  });
  const badRuins = ruinOut.filter(o => !o.placed || (o.walk && o.walk.status !== 'Ok')).map(o => o.placed ? `${o.id} ${o.walk.status}` : `${o.id} (${o.reason})`);
  chk('ruins', 'Fail', badRuins.length ? badRuins.join('; ') : null);
  const farRuins = ruinOut.filter(o => o.area && (o.role === 'reef' ? o.landingArea : o.area) === 'other').map(o => o.id);
  chk('ruin-areas', 'Fail', farRuins.length ? farRuins.join(', ') + ' outside its sector\'s facility area and its neighbours' : null);

  // ---- paddocks, outstations, lines, ADM ----
  const men = secSite('MEN');
  const pads = W.paddocks.map(q => ({ key: q.key, km2: q.km2, fromMen: men ? D.hyp(q.x - men.x, q.y - men.y) / 1000 : 0 }));
  const pen = sites.some(t => t.kind === 'raptors');
  chk('paddocks2', 'Fail', W.paddocks.length < 5 || !pen ? `${W.paddocks.length} of 5 paddocks${pen ? '' : ', no raptor pen'}` : null);
  const perSector = {}; P.outstations.forEach(o => perSector[o.sector] = (perSector[o.sector] || 0) + 1);
  chk('outstations', 'Fail', P.outstations.length !== P.outstationCount || Object.values(perSector).some(v => v > 2) ? `${P.outstations.length} of ${P.outstationCount} placed` : null);
  chk('ley', 'Fail', P.lines.length < 4 ? `${P.lines.length} of 4 lines` : null);
  const surveys = P.lines.filter(l => l.survey != null).length;
  chk('survey', 'Warn', surveys < 4 ? `${surveys} of 4 survey posts` : null);
  chk('adm', 'Fail', !P.adm ? 'no footprint' : P.adm.wetCells || P.adm.under.length ? `footprint under ${P.adm.wetCells} samples of sea or lake, and under ${P.adm.under.join(', ') || 'no other compound'}` : null);
  P.warnings.forEach(x => chk(x.key, 'Warn', x.reason));
  P.failures.filter(f => !f.key.startsWith('ruin:')).forEach(f => chk('placement', 'Fail', `${f.key}: ${f.reason}`));

  // ---- sites: spacing and distance from the Court ----
  const siteOut = sites.map(t => {
    let nd = 1e30; for (const u of sites) if (u !== t) nd = Math.min(nd, D.hyp(u.x - t.x, u.y - t.y));
    return { kind: t.kind, sector: t.sector || null, h: t.h, fromCourt: D.hyp(t.x - court.x, t.y - court.y) / 1000, nearest: nd / 1000, areaKm2: areaKm2[t.id], major: t.major };
  });
  return {
    seed, genSec, seconds: Number(process.hrtime.bigint() - t0) / 1e9, landKm2: S.landKm2,
    s24: { landPct: 100 * S.landFraction, lowlandPct: 100 * S.lowlandShare, beachPct: 100 * S.beachShare, walkPct: 100 * S.walkableConnectedShare, islets: S.islets, slowestKeyH: R24.slowestMinutes / 60 },
    roles: P.roles, ruins: ruinOut, sites: siteOut, courtWalk, pads, pen, outstations: { chosen: P.outstationCount, placed: P.outstations.length, onRole: P.outstations.filter(o => o.onRole).length, perSector },
    beaches: { stretches: stretches.length, clear: stretches.filter(b => b.clear).length, ok: beachesOk },
    lines: P.lines.map(l => ({ key: l.key, through: l.through, survey: l.survey != null })), adm: P.adm ? { b1Depth: court.h - P.adm.b1z, rivers: P.adm.riverCells, adit: P.adm.ports.some(q => q.kind === 'service adit'), lift: P.adm.ports.some(q => q.kind === 'monorail lift') } : null,
    states: P.states, counts: { caves: W.caves.length, tubes: W.caves.filter(c => c.kind === 'lava tube').length, wrecks: W.wrecks.length, ruins: W.ruins.length, ancient: W.ruins.filter(q => q.era === 'ancient').length, sites: sites.length },
    monorail: W.transit.monorail ? W.transit.monorail.stations.length : 0, tram: W.transit.tram ? W.transit.tram.stops.length : 0, lifts: W.transit.lifts.length,
    checks, passed: !checks.some(c => c.sev === 'Fail'), placementFailures: P.failures
  };
}

// ---------- main: run the seeds, then write the report ----------
const args = process.argv.slice(2), opt = {}, pos = [];
for (let k = 0; k < args.length; k++) { if (args[k].startsWith('--')) opt[args[k].slice(2)] = args[++k]; else pos.push(args[k]); }
const first = pos[0] && pos[0] !== '-' ? Number(pos[0]) : 1, count = pos[1] && pos[1] !== '-' ? Number(pos[1]) : 200, scale = pos[2] && pos[2] !== '-' ? Number(pos[2]) : 100;
const workers = Number(opt.workers || require('os').cpus().length);
const stamp = new Date().toISOString().replace(/[-:]/g, '').replace('T', '-').slice(0, 15);
const out = opt.out || path.join(__dirname, '..', '..', 'reports', 'sites-' + stamp);
main().catch(e => { console.error(e); process.exit(1); });

async function main() {
  console.log(`Site program 2: ${count} seeds from ${first} at ${scale}%, ${workers} at a time`);
  const t0 = Date.now(), results = [], queue = [];
  for (let i = 0; i < count; i++) queue.push((first + i) >>> 0);
  await Promise.all(Array.from({ length: Math.min(workers, count) }, () => new Promise(resolve => {
    const wk = new Worker(__filename);
    const next = () => { if (!queue.length) { wk.terminate(); resolve(); return; } wk.postMessage({ seed: queue.shift(), scale }); };
    wk.on('message', m => { results.push(m); if (results.length % 10 === 0 || results.length === count) console.log(`  ${String(results.length).padStart(4)}/${count}  (${Math.round((Date.now() - t0) / 1000)}s)`); next(); });
    wk.on('error', e => { console.error(e); resolve(); });
    next();
  })));
  results.sort((a, b) => a.seed - b.seed);
  fs.mkdirSync(out, { recursive: true });
  const md = report(results, (Date.now() - t0) / 1000);
  fs.writeFileSync(path.join(out, 'report.md'), md);
  fs.writeFileSync(path.join(out, 'seeds.csv'), csv(results));
  fs.writeFileSync(path.join(out, 'summary.json'), JSON.stringify(results, null, 0));
  console.log('\n' + md.split('\n## ')[0]);
  console.log(`Report: ${path.join(out, 'report.md')}`);
}

// ---------- report ----------
const pct = (a, b) => b ? (100 * a / b).toFixed(1) + '%' : '–';
function q(arr, f) { const a = arr.filter(v => Number.isFinite(v)).sort((x, y) => x - y); if (!a.length) return '–'; const at = p => a[Math.min(a.length - 1, Math.floor(p * (a.length - 1) + 0.5))]; return `${f(a[0])} / ${f(at(0.5))} / ${f(a[a.length - 1])}`; }
const f0 = v => v.toFixed(0), f1 = v => v.toFixed(1), f2 = v => v.toFixed(2);
function report(R, secs) {
  const ok = R.filter(r => !r.error), N = ok.length, L = [];
  const pass = ok.filter(r => r.passed).length;
  const fails = new Map(), warns = new Map();
  ok.forEach(r => r.checks.forEach(c => { const m = c.sev === 'Fail' ? fails : warns; m.set(c.key, (m.get(c.key) || 0) + 1); }));
  L.push(`# Site program 2 (P4a): ${R.length} seeds from ${first} at ${scale}%`);
  L.push('');
  L.push(`Generated ${N} of ${R.length}; **${pass} of ${N} seeds (${pct(pass, N)}) pass every Fail check**. Failing: ${fails.size ? [...fails].map(([k, v]) => `${k} ${v}`).join(', ') : 'none'}. Warnings: ${warns.size ? [...warns].map(([k, v]) => `${k} ${v}`).join(', ') : 'none'}.`);
  L.push(`Generation in Node ${q(ok.map(r => r.genSec), f2)} s (min / median / max); with the report's measuring ${q(ok.map(r => r.seconds), f2)} s. Run took ${secs.toFixed(0)} s.`);
  R.filter(r => r.error).forEach(r => L.push(`Seed ${r.seed} threw: ${r.error.split('\n')[0]}`));
  L.push('');
  // ---- terrain roles ----
  const role = (name, found, size, serves, fallback) => ({ name, found, size, serves, fallback });
  const ruin = (r, id) => r.ruins.find(o => o.id === id);
  const secRole = (r, c) => r.roles['site:' + c] || {};
  const roles = [
    role('Central high ground', r => !!r.roles.high.found, r => r.roles.high.km2, 'The Court, ADM beneath it, the lines\' crossing, the monorail hub', 'A worn tableland or old cone at the centre'),
    role('Prehistoric basin', r => ruin(r, 'RUIN-01').placed, r => r.roles.basin.km2, 'RUIN-01 Unhatched Court', 'A guaranteed basin of minimum size'),
    role('Inland ravine', r => ruin(r, 'RUIN-02').placed, r => r.roles.ravine.km2, 'RUIN-02 House of Borrowed Breath', 'A narrow valley cut into one shield\'s flank'),
    role('Wet river lowland', r => secRole(r, 'ORC').onRole && ruin(r, 'RUIN-03').placed, r => r.roles.lowland.km2, 'Glass Orchard, RUIN-03', 'A broad valley with a guaranteed river; deltas and floodplains (P4.10)'),
    role('High ridge', r => secRole(r, 'WTH').onRole && ruin(r, 'RUIN-04').placed, r => r.roles.ridge.km2, 'Ninth Weather, RUIN-04', 'A summit ridge on the higher shield'),
    role('Sea-cliff coast', r => secRole(r, 'CHO').onRole && ruin(r, 'RUIN-05').placed, r => r.roles.cliff.km2, 'Choir Below, RUIN-05', 'A guaranteed cliff stretch'),
    role('Reef (the reef band)', r => secRole(r, 'DRW').onRole && ruin(r, 'RUIN-06').placed, r => r.roles.reef.km2, 'Drowned Observatory, RUIN-06 (by causeway)', '–'),
    role('Reef flat that dries at low tide', r => ruin(r, 'RUIN-06').placed && ruin(r, 'RUIN-06').approach === 'reef flat at low tide', r => r.roles.reefFlat.km2, 'RUIN-06 on foot at low tide', 'A fringing reef with a flat that dries at low tide, or keep the causeway (P4.7 allows either)'),
    role('Paddock plains', r => r.pads.length === 5 && r.pads.every(p => p.km2 >= 0.9 * { apato: 9, tyrant: 2.2, ceratopsian: 1.4, crownback: 1.4, hadrosaur: 2.5 }[p.key]), r => r.roles.plains.km2, 'Five paddocks and the raptor pen', 'Guaranteed grassland area')
  ];
  L.push('## Terrain roles: found, how big, and whether a fallback anchor is needed');
  L.push('');
  L.push(`A role counts as found when its site (and its authored ruin, 1.5–6 km from the sector's compound) sits on that ground. A role found on fewer than ${100 * ROLE_FOUND_MIN}% of seeds needs its fallback anchor (P4.4), matching the P4 gate.`);
  L.push('');
  L.push('| Role | Found | Area on the island, km² (min / median / max) | Serves | Verdict |');
  L.push('|---|---|---|---|---|');
  for (const ro of roles) {
    const n = ok.filter(ro.found).length, need = n / N < ROLE_FOUND_MIN;
    L.push(`| ${ro.name} | ${n} of ${N} (${pct(n, N)}) | ${q(ok.map(ro.size), f1)} | ${ro.serves} | ${need ? '**anchor needed:** ' + ro.fallback : 'no anchor'} |`);
  }
  L.push('');
  L.push(`- Central high ground: Court height ${q(ok.map(r => r.roles.high.h), f0)} m; distance from the template centre (the saddle's midpoint) ${q(ok.map(r => r.roles.high.fromCentre / 1000), f1)} km.`);
  L.push(`- Prehistoric basin: crater basin ${q(ok.map(r => r.roles.basin.craterKm2), f1)} km² of it; the rest are enclosed valleys (50 m or more below the 1.5 km mean).`);
  L.push(`- Wet river lowland: along rivers of 1 m³/s or more, only ${q(ok.map(r => r.roles.lowland.bigRiverKm2), f2)} km²; largest single patch ${q(ok.map(r => r.roles.lowland.largestKm2), f2)} km².`);
  L.push(`- Sea-cliff coast: largest stretch ${q(ok.map(r => r.roles.cliff.largestKm2), f2)} km². Ridge: largest patch ${q(ok.map(r => r.roles.ridge.largestKm2), f1)} km².`);
  const r6 = ok.map(r => ruin(r, 'RUIN-06')).filter(o => o.placed);
  L.push(`- RUIN-06: ${r6.filter(o => o.approach === 'causeway').length} of ${r6.length} placed perimeters need the causeway; causeway ${q(r6.map(o => o.causewayM), f0)} m.`);
  L.push('');
  L.push('How each sector compound was placed (role = on its own ground with its ruin\'s ground in reach):');
  L.push('');
  L.push('On its role: MEN, beside paddock plains (the paddocks themselves are measured above); VIT, in the caldera (the old orgone ground); ORC, WTH, CHO and DRW on their roles (DRW and CHO within 600 m of the reef band and the cliff).');
  L.push('');
  L.push('| Sector | Role | Fallback | Role, ruin ground out of reach | Fallback, ruin ground out of reach | On its role |');
  L.push('|---|---|---|---|---|---|');
  for (const c of ['MEN', 'VIT', 'ORC', 'WTH', 'CHO', 'DRW']) {
    const h = k => ok.filter(r => secRole(r, c).how === k).length;
    L.push(`| ${c} | ${h('role')} | ${h('fallback')} | ${h('role, no ruin ground in reach')} | ${h('fallback, no ruin ground in reach')} | ${pct(ok.filter(r => secRole(r, c).onRole).length, N)} |`);
  }
  L.push('');
  // ---- ruins ----
  L.push('## The seven authored ruins');
  L.push('');
  L.push('| Ruin | Placed | km from its sector | Facility area: same / neighbour / other | Walk from the Court, h | Not placed: reasons |');
  L.push('|---|---|---|---|---|---|');
  for (const id of ['RUIN-01', 'RUIN-02', 'RUIN-03', 'RUIN-04', 'RUIN-05', 'RUIN-06', 'RUIN-07']) {
    const o = ok.map(r => ruin(r, id)), pl = o.filter(x => x.placed);
    const ar = k => pl.filter(x => (x.role === 'reef' ? x.landingArea : x.area) === k).length;
    const why = new Map(); o.filter(x => !x.placed).forEach(x => { const k = (x.reason || '').replace(/[0-9]+/g, 'n'); why.set(k, (why.get(k) || 0) + 1); });
    L.push(`| ${id} | ${pl.length} of ${N} | ${id === 'RUIN-07' ? 'at the Court' : q(pl.map(x => x.km), f1)} | ${id === 'RUIN-07' ? '–' : `${ar('same')} / ${ar('neighbour')} / ${ar('other')}`} | ${id === 'RUIN-07' ? '–' : q(pl.map(x => x.walk && x.walk.status === 'Ok' ? x.walk.hours : NaN), f1)} | ${[...why].map(([k, v]) => `${v}× ${k}`).join('; ') || '–'} |`);
  }
  L.push('');
  // ---- walks ----
  L.push('## Walking times between the Court and every sector');
  L.push('');
  L.push('On foot today (no road tunnels, no rivers too deep to wade); with transit (lifts, tram, monorail) allowed. Hours, min / median / max.');
  L.push('');
  L.push('| Sector | Walkable | On foot | With transit | Must ford a river |');
  L.push('|---|---|---|---|---|');
  for (const c of ['MEN', 'VIT', 'ORC', 'WTH', 'CHO', 'DRW']) {
    const v = ok.map(r => r.courtWalk[c]).filter(Boolean);
    L.push(`| ${c} | ${v.filter(x => x.status === 'Ok').length} of ${v.length} | ${q(v.map(x => x.status === 'Ok' ? x.hours : NaN), f1)} | ${q(v.map(x => x.transitHours), f1)} | ${v.filter(x => x.mustFord).length} |`);
  }
  const slow = ok.map(r => Math.max(0, ...Object.values(r.courtWalk).filter(v => v.status === 'Ok').map(v => v.hours)));
  L.push('');
  L.push(`Slowest Court–sector walk per seed: ${q(slow, f1)} h. Slowest walk between ADM's key sites (docks, airstrip, comms, Court, watch posts): ${q(ok.map(r => r.s24.slowestKeyH), f1)} h.`);
  L.push('');
  // ---- sites ----
  L.push('## Sites: placement and spacing');
  L.push('');
  L.push('| Kind | Placed per seed | km from the Court | Nearest other site, km | Height, m | Facility area, km² |');
  L.push('|---|---|---|---|---|---|');
  const kinds = [...new Set(ok.flatMap(r => r.sites.map(s => s.kind)))];
  for (const k of kinds) {
    const all = ok.flatMap(r => r.sites.filter(s => s.kind === k)), per = ok.map(r => r.sites.filter(s => s.kind === k).length);
    L.push(`| ${k} | ${q(per, f0)} | ${q(all.map(s => s.fromCourt), f1)} | ${q(all.map(s => s.nearest), f1)} | ${q(all.map(s => s.h), f0)} | ${q(all.map(s => s.areaKm2), f1)} |`);
  }
  L.push('');
  L.push(`Sites per seed ${q(ok.map(r => r.counts.sites), f0)}. Outstations chosen ${q(ok.map(r => r.outstations.chosen), f0)}, placed ${q(ok.map(r => r.outstations.placed), f0)}, on their sector's ground ${pct(ok.reduce((a, r) => a + r.outstations.onRole, 0), ok.reduce((a, r) => a + r.outstations.placed, 0))}.`);
  L.push(`Paddocks: ${q(ok.map(r => r.pads.length), f0)} per seed; ${['apato', 'tyrant', 'ceratopsian', 'crownback', 'hadrosaur'].map(k => `${k} ${q(ok.map(r => (r.pads.find(p => p.key === k) || { km2: NaN }).km2), f1)} km²`).join('; ')}; centre to the Menagerie ${q(ok.flatMap(r => r.pads.map(p => p.fromMen)), f1)} km.`);
  L.push(`Lesser sites: caves ${q(ok.map(r => r.counts.caves), f0)} (lava tubes ${q(ok.map(r => r.counts.tubes), f0)}), wrecks ${q(ok.map(r => r.counts.wrecks), f0)}, generic ruins ${q(ok.map(r => r.counts.ruins), f0)} (ancient satellites ${q(ok.map(r => r.counts.ancient), f0)}).`);
  L.push(`Ley lines: ${q(ok.map(r => r.lines.length), f0)} per seed, survey posts ${q(ok.map(r => r.lines.filter(l => l.survey).length), f0)}. ADM: B1 ${q(ok.map(r => r.adm ? r.adm.b1Depth : NaN), f0)} m below the Court; a river crosses the footprint on ${ok.filter(r => r.adm && r.adm.rivers).length} seeds; service adit found on ${ok.filter(r => r.adm && r.adm.adit).length} seeds, monorail lift on ${ok.filter(r => r.adm && r.adm.lift).length}.`);
  L.push(`Transit: monorail stations ${q(ok.map(r => r.monorail), f0)}, tram stops ${q(ok.map(r => r.tram), f0)}, lifts ${q(ok.map(r => r.lifts), f0)}. Shipwreck beaches walkable from the Court ${q(ok.map(r => r.beaches.ok), f0)} (of ${q(ok.map(r => r.beaches.stretches), f0)} beach stretches of ${BEACH_MIN_CELLS} cells or more).`);
  const st = {}; ok.forEach(r => Object.values(r.states).forEach(v => st[v.state] = (st[v.state] || 0) + 1));
  L.push(`Seeded facility states (data only): ${Object.entries(st).map(([k, v]) => `${k} ${v}`).join(', ')}.`);
  L.push('');
  L.push('## Step-24 measures under program 2');
  L.push('');
  L.push(`Land ${q(ok.map(r => r.s24.landPct), f1)}% of the frame; lowland ${q(ok.map(r => r.s24.lowlandPct), f1)}%; beach ${q(ok.map(r => r.s24.beachPct), f1)}% of the coast; walkable and connected ${q(ok.map(r => r.s24.walkPct), f1)}%; islets ${q(ok.map(r => r.s24.islets), f0)}.`);
  L.push('');
  L.push('## Failing seeds');
  L.push('');
  const bad = ok.filter(r => !r.passed);
  if (!bad.length) L.push('None.');
  bad.forEach(r => L.push(`- ${r.seed}: ${r.checks.filter(c => c.sev === 'Fail').map(c => `${c.key}: ${c.reason}`).join('; ')}`));
  L.push('');
  return L.join('\n');
}
function csv(R) {
  const head = ['seed', 'seconds', 'passed', 'fails', 'warnings', 'court_h', 'court_from_centre_km', 'slowest_court_sector_h', 'outstations', 'paddocks', 'beaches_ok',
    'ruin01', 'ruin02', 'ruin03', 'ruin04', 'ruin05', 'ruin06', 'causeway_m', 'orc_on_role', 'wth_on_role', 'cho_on_role', 'drw_on_role', 'lowland_km2', 'basin_km2', 'ravine_km2', 'error'];
  const rows = [head.join(',')];
  for (const r of R) {
    if (r.error) { rows.push(`${r.seed},${','.repeat(head.length - 3)},${r.error.split('\n')[0].replace(/,/g, ';')}`); continue; }
    const ru = id => { const o = r.ruins.find(x => x.id === id); return o.placed ? (o.walk ? o.walk.status : 'placed') : 'missing'; };
    const r6 = r.ruins.find(x => x.id === 'RUIN-06');
    rows.push([r.seed, r.seconds.toFixed(2), r.passed ? 1 : 0, r.checks.filter(c => c.sev === 'Fail').map(c => c.key).join(' '), r.checks.filter(c => c.sev === 'Warn').map(c => c.key).join(' '),
      r.roles.high.h.toFixed(0), (r.roles.high.fromCentre / 1000).toFixed(2), Math.max(0, ...Object.values(r.courtWalk).filter(v => v.status === 'Ok').map(v => v.hours)).toFixed(2),
      `${r.outstations.placed}/${r.outstations.chosen}`, r.pads.length, r.beaches.ok, ru('RUIN-01'), ru('RUIN-02'), ru('RUIN-03'), ru('RUIN-04'), ru('RUIN-05'), ru('RUIN-06'), r6.placed ? Math.round(r6.causewayM) : '',
      +!!(r.roles['site:ORC'] || {}).onRole, +!!(r.roles['site:WTH'] || {}).onRole, +!!(r.roles['site:CHO'] || {}).onRole, +!!(r.roles['site:DRW'] || {}).onRole,
      r.roles.lowland.km2.toFixed(2), r.roles.basin.km2.toFixed(1), r.roles.ravine.km2.toFixed(1), ''].join(','));
  }
  return rows.join('\n') + '\n';
}
