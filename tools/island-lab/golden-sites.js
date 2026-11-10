'use strict';
// Golden hashes for stage 4a of the C# port (IslandWorks: facilities, paddocks, power, compounds).
// Prints the C# table used by IslandWorksTests.
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
for (const [name, params] of CASES) {
  const w = G.generate(Object.assign({ stopAfter: 'sites' }, params));
  const hs = hasher().dbl(w.mx).dbl(w.my).int(w.sites.length);
  for (const t of w.sites) {
    hs.int(t.id).int(FACK.indexOf(t.kind)).dbl(t.x).dbl(t.y).dbl(t.r).dbl(t.h).int(t.region).str(t.name).str(t.owner).int(t.major ? 1 : 0);
    if (t.dir) hs.int(1).dbl(t.dir[0]).dbl(t.dir[1]); else hs.int(0);
    if (t.runway) hs.int(1).dbl(t.runway.x1).dbl(t.runway.y1).dbl(t.runway.x2).dbl(t.runway.y2).dbl(t.runway.h); else hs.int(0);
  }
  const hl = hasher().int(w.leylines.length);
  for (const l of w.leylines) hl.dbl(l.x1).dbl(l.y1).dbl(l.x2).dbl(l.y2);
  const hp = hasher().int(w.paddocks.length);
  for (const q of w.paddocks) {
    hp.int(q.id).int(PADK.indexOf(q.key)).dbl(q.x).dbl(q.y).dbls(q.poly).dbl(q.km2).int(BIOK.indexOf(q.biome))
      .dbl(q.gate[0]).dbl(q.gate[1]).dbl(q.out[0]).dbl(q.out[1]).int(q.hasLake ? 1 : 0);
    if (q.wallow) hp.int(1).dbl(q.wallow[0]).dbl(q.wallow[1]); else hp.int(0);
    if (q.inner) hp.dbls(q.inner); else hp.int(-1);
  }
  hp.ints(w.padCell);
  const hw = hasher().int(w.power.length);
  for (const l of w.power) hw.dbl(l.x1).dbl(l.y1).dbl(l.x2).dbl(l.y2).str(l.from).str(l.to);
  hw.dbl(w.powerKm);
  const hc = hasher().int(w.labels.length);
  for (const s of w.labels) hc.str(s);
  hc.int(w.prims.length);
  for (const q of w.prims) prim(hc, q);
  console.log(`        ["${name}"] = new(${w.sites.length}, ${w.paddocks.length}, ${w.power.length}, ${w.labels.length}, ${w.prims.length},`);
  console.log(`            ${hs.hex()}, ${hl.hex()}, ${hp.hex()}, ${hw.hex()}, ${hc.hex()}),`);
}
