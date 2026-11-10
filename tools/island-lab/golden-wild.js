'use strict';
// Golden hashes for stage 4d of the C# port (caves, wrecks, ruins, wildlife). Prints the C# table used by IslandWildTests.
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
const pts = (h, a) => { h.int(a.length); for (const q of a) h.dbl(q[0]).dbl(q[1]); return h; };
for (const [name, params] of CASES) {
  const w = G.generate(Object.assign({ stopAfter: 'works' }, params));
  const hc = hasher().int(w.caves.length);
  for (const c of w.caves) { hc.int(c.id).int(c.kind === 'sea cave' ? 1 : 0).str(c.name).dbls(c.xs).dbls(c.ys).dbl(c.depth).dbl(c.lengthKm); pts(hc, c.mouths); pts(hc, c.skylights); }
  const hw = hasher().int(w.wrecks.length);
  for (const q of w.wrecks) hw.int(q.id).dbl(q.x).dbl(q.y).str(q.kind).str(q.era).dbl(q.length).dbl(q.heading).int(q.onReef ? 1 : 0).int(q.beached ? 1 : 0).int(q.inland ? 1 : 0).str(q.name);
  const hr = hasher().int(w.ruins.length);
  for (const q of w.ruins) hr.int(q.id).str(q.kind).dbl(q.x).dbl(q.y).dbl(q.r).str(q.era).str(q.name).dbl(q.rot);
  const hf = hasher().int(w.fauna.length);
  for (const f of w.fauna) hf.str(f.key).ints(f.density).dbl(f.population);
  console.log(`        ["${name}"] = new(${w.caves.length}, ${w.wrecks.length}, ${w.ruins.length}, ${w.fauna[0].population}, ${hc.hex()}, ${hw.hex()}, ${hr.hex()}, ${hf.hex()}),`);
}
