'use strict';
// Golden hashes for stage 5 of the C# port (IslandChunker: 1 m tiles). Picks chunks that cover every kind of
// feature and prints the C# table used by IslandChunkTests.
const G = require('./gen.js');
const PRIME = 1099511628211n, MASK = (1n << 64n) - 1n;
function hasher() { // BlueHarvest.Sim.Core.StateHasher
  let h = 14695981039346656037n;
  const add = (u) => { for (let i = 0n; i < 8n; i++) { h ^= (u >> (i * 8n)) & 255n; h = (h * PRIME) & MASK; } };
  const f64 = new Float64Array(1), u64 = new BigUint64Array(f64.buffer);
  const api = {
    dbl(v) { f64[0] = v; add(u64[0]); return api; },
    int(v) { add(BigInt.asUintN(64, BigInt(v))); return api; },
    str(s) { api.int(s.length); for (let i = 0; i < s.length; i++) api.int(s.charCodeAt(i)); return api; },
    dbls(a) { api.int(a.length); for (let i = 0; i < a.length; i++) api.dbl(a[i]); return api; },
    ints(a) { api.int(a.length); for (let i = 0; i < a.length; i++) api.int(a[i]); return api; },
    hex() { return '0x' + h.toString(16).toUpperCase().padStart(16, '0') + 'UL'; }
  };
  return api;
}
const CASES = [
  ['Default', {}],
  ['Seed12', { seed: 12 }],
  ['SmallNoCliffs', { seed: 44, scale: 50, wind: 100, cliffs: 0, iterations: 5, pali: 0.5 }],
  ['CoolWet', { seed: 9, scale: 60, seaTemp: 21, cloudBase: 500, wetlands: 1.5, rain: 1.3 }]
];
function chunkHash(c) {
  const h = hasher().int(c.size).int(c.x0).int(c.y0).dbls(c.height).ints(c.cover);
  h.int(c.water.length); for (let i = 0; i < c.water.length; i++) { const v = c.water[i]; if (v !== v) h.int(-7); else h.dbl(v); }
  return h.dbls(c.shade).ints(c.biome).ints(c.label).hex();
}
for (const [name, params] of CASES) {
  const w = G.generate(params), bins = G.indexRivers(w), W = w.works, T = W.transit, picks = [], seen = new Set();
  const at = (x, y) => { const cx = Math.floor(x / 64), cy = Math.floor(y / 64), k = cx + ',' + cy; if (!seen.has(k)) { seen.add(k); picks.push([cx, cy]); } };
  const site = k => W.sites.find(t => t.kind === k);
  at(0, 0); at(w.W / 2, w.H / 2);
  for (const k of ['genetics', 'raptors', 'nuclear', 'weather', 'comms']) { const t = site(k); if (t) at(t.x, t.y); }
  { const t = site('docks'); if (t) { at(t.x + t.dir[0] * (t.r + 60), t.y + t.dir[1] * (t.r + 60)); at(t.x + t.r, t.y + t.r); } }
  { const t = site('airstrip'); if (t) at((t.runway.x1 + t.runway.x2) / 2, (t.runway.y1 + t.runway.y2) / 2); }
  for (const q of W.paddocks) if (q.key === 'rex' || q.key === 'trike') { at(q.gate[0], q.gate[1]); if (q.wallow) at(q.wallow[0], q.wallow[1]); }
  { const r = w.rivers[0]; at(r.xs[r.xs.length >> 1], r.ys[r.ys.length >> 1]); at(r.xs[r.xs.length - 1], r.ys[r.ys.length - 1]); }
  if (w.lakes.length) at(w.lakes[0].cx, w.lakes[0].cy);
  if (name === 'Default') {
    for (const c of W.caves.slice(0, 2)) { at(c.mouths[0][0], c.mouths[0][1]); if (c.skylights.length) at(c.skylights[0][0], c.skylights[0][1]); }
    at(W.caves[W.caves.length - 1].xs[0], W.caves[W.caves.length - 1].ys[0]);
    at(W.wrecks[0].x, W.wrecks[0].y); at(W.wrecks[W.wrecks.length - 1].x, W.wrecks[W.wrecks.length - 1].y);
    for (const k of ['stones', 'terraces', 'lighthouse', 'chapel']) { const q = W.ruins.find(r => r.kind === k); if (q) at(q.x, q.y); }
    if (T.tram) { const m = T.tram.xs.length >> 1; at(T.tram.xs[m], T.tram.ys[m]); at(T.tram.stops[1].x, T.tram.stops[1].y); }
    if (T.monorail) at(T.monorail.stations[0].x, T.monorail.stations[0].y);
    if (T.lifts.length) at(T.lifts[0].x2, T.lifts[0].y2);
    { const l = W.power[0]; at((l.x1 + l.x2) / 2, (l.y1 + l.y2) / 2); }
    { const rd = W.roads.find(r => Array.from(r.flags).some(f => f & 1)); if (rd) { const k = Array.from(rd.flags).findIndex(f => f & 1); at(rd.xs[k], rd.ys[k]); } }
    for (const want of [c => w.coast[c] === 3, c => w.pali[c] > 0.5 && w.slope[c] > 30, c => w.sea[c] === 3, c => w.coast[c] === 1, c => w.biome[c] === 2, c => w.biome[c] === 8])
      for (let c = 0; c < w.nx * w.ny; c += 7) if (want(c)) { at((c % w.nx + 0.5) * w.cell, (Math.floor(c / w.nx) + 0.5) * w.cell); break; }
  }
  const wide = G.chunk(w, bins, picks[2][0], picks[2][1], 65);
  console.log(`        ["${name}"] = new(${picks[2][0]}, ${picks[2][1]}, ${chunkHash(wide)},`);
  console.log('        [');
  let covers = new Set();
  for (const [cx, cy] of picks) { const c = G.chunk(w, bins, cx, cy); c.cover.forEach(v => covers.add(v)); console.log(`            (${cx}, ${cy}, ${chunkHash(c)}),`); }
  console.log(`        ]), // ${picks.length} chunks, ${covers.size} of ${G.COVER.length} covers seen`);
}
