'use strict';
// Golden values for step 25b-2 (FacilityAreas): each golden island's facility areas and their ten park-map
// sheets, from gen.js's facilityAreas. Prints the C# table used by FacilityAreasTests.
const G = require('./gen.js');
const PRIME = 1099511628211n, MASK = (1n << 64n) - 1n;
function hasher() { // BlueHarvest.Sim.Core.StateHasher (GenHasher.I)
  let h = 14695981039346656037n;
  const add = (u) => { for (let i = 0n; i < 8n; i++) { h ^= (u >> (i * 8n)) & 255n; h = (h * PRIME) & MASK; } };
  const api = { int(v) { add(BigInt.asUintN(64, BigInt(v))); return api; }, hex() { return '0x' + h.toString(16).toUpperCase().padStart(16, '0') + 'UL'; } };
  return api;
}
const CASES = [
  ['Default', {}],
  ['Seed12', { seed: 12, scale: 75 }],
  ['SmallNoCliffs', { seed: 44, scale: 50, wind: 100, cliffs: 0, iterations: 5, pali: 0.5 }],
  ['CoolWet', { seed: 9, scale: 60, seaTemp: 21, cloudBase: 500, wetlands: 1.5, rain: 1.3 }]
];
const verbose = process.argv.includes('--verbose');
for (const [name, params] of CASES) {
  const w = G.generate(params), A = G.facilityAreas(w), sites = w.works.sites, N = w.nx * w.ny;
  const ha = hasher().int(N), hsh = hasher().int(N);
  for (let i = 0; i < N; i++) { ha.int(A.area[i]); hsh.int(A.sheet[i]); }
  const counts = sites.map(() => 0);
  for (let i = 0; i < N; i++) if (A.area[i] >= 0) counts[A.area[i]]++;
  const land = counts.reduce((a, b) => a + b, 0);
  const orders = A.order.map(o => o.join('')).join(' ');
  console.log(`        ["${name}"] = new(${land}, ${ha.hex()}, ${hsh.hex()}, "${counts.join(',')}", "${A.facilitySheet.join(',')}", "${orders}"),`);
  if (verbose) sites.forEach((t, a) => console.log(`   ${t.major ? 'M' : 'm'} ${t.name.padEnd(34)} ${String(counts[a]).padStart(5)} cells (${(counts[a] * w.cell * w.cell / 1e6).toFixed(1)} km²)`));
}
