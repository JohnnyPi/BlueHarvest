'use strict';
// Golden values for step 25a-1 (RoomIndex): every named room of every building, read the way
// RoomIndex.Build reads the C# generator's primitives. Prints the C# table used by RoomIndexTests.
// The room rules below must match src/BlueHarvest.Sim/World/Generation/RoomRules.cs.
const G = require('./gen.js');
const PRIME = 1099511628211n, MASK = (1n << 64n) - 1n;
function hasher() { // BlueHarvest.Sim.Core.StateHasher
  let h = 14695981039346656037n;
  const add = (u) => { for (let i = 0n; i < 8n; i++) { h ^= (u >> (i * 8n)) & 255n; h = (h * PRIME) & MASK; } };
  const api = {
    int(v) { add(BigInt.asUintN(64, BigInt(v))); return api; },
    str(s) { api.int(s.length); for (let i = 0; i < s.length; i++) api.int(s.charCodeAt(i)); return api; },
    hex() { return '0x' + h.toString(16).toUpperCase().padStart(16, '0') + 'UL'; }
  };
  return api;
}
const SECURE = new Set(['Server hall', 'Data centre', 'Cryo vault', 'Armoury', 'Security office', 'Security', 'Sequencing lab', 'Embryology lab',
  'Fertilisation lab', 'Clean room', 'Genetics lab', 'Specimen store', 'Records', 'Health physics',
  'Fuel pool', 'Cask store', 'Accumulator chamber', 'Holding cell 1', 'Holding cell 2', 'Holding cell 3', 'Holding cell 4', 'Animal holding']);
const LEVEL3 = new Set(['Cryo vault', 'Armoury', 'Records', 'Fuel pool', 'Cask store', 'Accumulator chamber']);
const MOTOR = new Set(['Elevator', 'Airlock', 'Vehicle bays', 'Hangar floor', 'Boat bays', 'Cargo hall']);
const level = n => LEVEL3.has(n) ? 3 : SECURE.has(n) ? 2 : 0;

const CASES = [
  ['Default', {}],
  ['Seed12', { seed: 12 }],
  ['SmallNoCliffs', { seed: 44, scale: 50, wind: 100, cliffs: 0, iterations: 5, pali: 0.5 }],
  ['CoolWet', { seed: 9, scale: 60, seaTemp: 21, cloudBase: 500, wetlands: 1.5, rain: 1.3 }]
];
for (const [name, params] of CASES) {
  const s = G.generate(Object.assign({ stopAfter: 'sites' }, params));
  const w = G.generate(Object.assign({ stopAfter: 'transit' }, params));
  const siteIndex = new Map();
  s.sites.forEach((t, i) => { if (!siteIndex.has(t.name)) siteIndex.set(t.name, i); });
  const paddocks = new Set(s.paddocks.map(q => q.name));
  const rows = [];
  for (const q of w.prims) {
    if (q.t !== 'bld' || !q.rooms) continue;
    const parts = w.labels[q.label].split(' · ');
    const owner = parts[0], building = parts.length > 1 ? parts[1] : parts[0];
    let place, kind, si = -1;
    if (siteIndex.has(owner)) { si = siteIndex.get(owner); place = owner; kind = s.sites[si].kind; }
    else if (paddocks.has(owner)) { place = owner; kind = 'paddock'; }
    else { place = owner + ' station'; kind = 'transit'; }
    for (const r of q.rooms) {
      const rp = w.labels[r.label].split(' · '), rn = rp[rp.length - 1];
      rows.push({ id: rows.length, name: rn, building, place, si, kind, x0: r.x0, y0: r.y0, x1: r.x1, y1: r.y1, level: level(rn), motor: MOTOR.has(rn), label: r.label });
    }
  }
  const h = hasher().int(rows.length);
  for (const r of rows)
    h.int(r.id).str(r.name).str(r.building).str(r.place).int(r.si).str(r.kind)
      .int(r.x0).int(r.y0).int(r.x1).int(r.y1).int(r.level).int(r.motor ? 1 : 0).int(r.label);
  const perPlace = new Map();
  for (const r of rows) perPlace.set(r.place, (perPlace.get(r.place) || 0) + 1);
  const places = [...perPlace].map(([p, n]) => `${p}=${n}`).join(';');
  const secure = rows.filter(r => r.level > 0).length, l3 = rows.filter(r => r.level === 3).length, motor = rows.filter(r => r.motor).length;
  console.log(`        ["${name}"] = new(${rows.length}, ${secure}, ${l3}, ${motor}, ${h.hex()},`);
  console.log(`            "${places.replace(/"/g, '\\"')}"),`);
}
