'use strict';
// Golden hashes for stage 4c of the C# port (IslandWorks walking times and transit).
// Prints the C# table used by IslandTransitTests.
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
const FACK = G.FAC.map(f => f.key), PADK = G.PADDOCKS.map(q => q.key), BIOK = G.BIOMES.map(b => b.key), COVK = G.COVER.map(c => c.key);
const PK = { bld: 0, fur: 1, circle: 2, rect: 3, tower: 4, line: 5, pen: 6 };
function prim(h, q) {
  h.int(PK[q.t]);
  switch (q.t) {
    case 'bld':
      h.dbl(q.x0).dbl(q.y0).dbl(q.x1).dbl(q.y1).int(q.label).int(q.rooms.length);
      for (const r of q.rooms) h.dbl(r.x0).dbl(r.y0).dbl(r.x1).dbl(r.y1).int(r.label);
      h.int(q.doors.length); for (const d of q.doors) h.dbl(d[0]).dbl(d[1]);
      break;
    case 'fur': h.dbl(q.x0).dbl(q.y0).dbl(q.x1).dbl(q.y1).int(COVK.indexOf(q.cover)).int(q.label); break;
    case 'circle': h.dbl(q.x).dbl(q.y).dbl(q.r).int(COVK.indexOf(q.cover)).int(q.ring ? 1 : 0).int(q.fill ? COVK.indexOf(q.fill) : -1).int(q.door ? 1 : 0).int(q.label); break;
    case 'rect': h.dbl(q.x0).dbl(q.y0).dbl(q.x1).dbl(q.y1).int(COVK.indexOf(q.cover)).int(q.fence ? 1 : 0).int(q.label); break;
    case 'tower': h.dbl(q.x).dbl(q.y).int(q.label); break;
    case 'line': h.dbl(q.ax).dbl(q.ay).dbl(q.bx).dbl(q.by).dbl(q.w).int(COVK.indexOf(q.cover)).dbl(q.depth).int(q.label); break;
    case 'pen': h.dbl(q.x0).dbl(q.y0).dbl(q.x1).dbl(q.y1).int(q.label); break;
    default: throw new Error('unknown prim ' + q.t);
  }
}
const CASES = [
  ['Default', {}],
  ['Seed12', { seed: 12 }],
  ['SmallNoCliffs', { seed: 44, scale: 50, wind: 100, cliffs: 0, iterations: 5, pali: 0.5 }],
  ['CoolWet', { seed: 9, scale: 60, seaTemp: 21, cloudBase: 500, wetlands: 1.5, rain: 1.3 }]
];
const mat = (h, m) => { h.int(m.length); for (const row of m) h.dbls(row); return h; };
const near = (h, a) => { h.int(a.length); for (const q of a) { if (q) h.int(q.id).dbl(q.minutes); else h.int(-1); } return h; };
for (const [name, params] of CASES) {
  const w = G.generate(Object.assign({ stopAfter: 'transit' }, params)), T = w.transit;
  const hw = near(mat(hasher(), w.walk), w.nearest);
  const hl = hasher().int(T.lifts.length);
  for (const l of T.lifts) { hl.str(l.name).str(l.from).str(l.to).dbl(l.x1).dbl(l.y1).dbl(l.x2).dbl(l.y2).dbl(l.km).dbl(l.crest).int(l.towers.length); for (const q of l.towers) hl.dbl(q[0]).dbl(q[1]); }
  const ht = hasher();
  if (T.tram) { ht.int(1).dbls(T.tram.xs).dbls(T.tram.ys).dbl(T.tram.km).int(T.tram.stops.length); for (const q of T.tram.stops) ht.str(q.name).dbl(q.x).dbl(q.y); } else ht.int(0);
  const hm = hasher(), M = T.monorail;
  if (M) {
    hm.int(1).dbl(M.km).dbl(M.minCover).dbl(M.deepest).int(M.stations.length);
    for (const q of M.stations) hm.str(q.name).dbl(q.x).dbl(q.y).dbl(q.z).dbl(q.shaft).str(q.power);
    hm.int(M.vents.length); for (const q of M.vents) hm.dbl(q[0]).dbl(q[1]);
  } else hm.int(0);
  hm.int(w.objectives.length); for (const o of w.objectives) hm.str(o.key).str(o.site).str(o.room).dbl(o.x).dbl(o.y);
  const hx = near(mat(hasher(), w.walkTransit), w.nearestTransit);
  const hc = hasher().int(w.labels.length); for (const s of w.labels) hc.str(s);
  hc.int(w.prims.length); for (const q of w.prims) prim(hc, q);
  console.log(`        ["${name}"] = new(${T.lifts.length}, ${T.tram ? T.tram.stops.length : 0}, ${M ? M.stations.length : 0}, ${w.labels.length}, ${w.prims.length},`);
  console.log(`            ${hw.hex()}, ${hl.hex()}, ${ht.hex()}, ${hm.hex()}, ${hx.hex()}, ${hc.hex()}),`);
}
