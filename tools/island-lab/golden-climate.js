'use strict';
// Golden hashes for stage 3 of the C# port (IslandClimate). Prints the C# table used by IslandClimateTests.
const G = require('./gen.js');
const PRIME = 1099511628211n, MASK = (1n << 64n) - 1n;
function hasher() { // BlueHarvest.Sim.Core.StateHasher
  let h = 14695981039346656037n;
  const add = (u) => { for (let i = 0n; i < 8n; i++) { h ^= (u >> (i * 8n)) & 255n; h = (h * PRIME) & MASK; } };
  const f64 = new Float64Array(1), u64 = new BigUint64Array(f64.buffer);
  const api = {
    dbl(v) { f64[0] = v; add(u64[0]); return api; },
    int(v) { add(BigInt.asUintN(64, BigInt(v))); return api; },
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
for (const [name, params] of CASES) {
  const c = G.generate(Object.assign({ stopAfter: 'climate' }, params));
  const fields = hasher().dbls(c.temp).dbls(c.fog).dbls(c.wet).dbls(c.crest).dbls(c.slopeD).ints(c.nearLake).dbl(c.tMin).dbl(c.tMax).hex();
  const biomes = hasher().ints(c.biome).dbls(c.biomeKm2).hex();
  const rh = hasher().ints(c.region).dbls(c.regionKm2).int(c.regionLabels.length);
  for (const L of c.regionLabels) rh.int(L.region).dbl(L.x).dbl(L.y).dbl(L.km2);
  console.log(`        ["${name}"] = new(${c.regionLabels.length}, ${fields}, ${biomes}, ${rh.hex()}),`);
}
