'use strict';
// Golden values for step 25b-1 (MapSight, MapKnowledge): the island-scale view rule ported from
// src/BlueHarvest.Sim/World/Generation/MapSight.cs, run over two scripted walks per golden island.
// Prints the C# table used by MapKnowledgeTests.
const G = require('./gen.js');
const PRIME = 1099511628211n, MASK = (1n << 64n) - 1n;
function hasher() { // BlueHarvest.Sim.Core.StateHasher (GenHasher.I)
  let h = 14695981039346656037n;
  const add = (u) => { for (let i = 0n; i < 8n; i++) { h ^= (u >> (i * 8n)) & 255n; h = (h * PRIME) & MASK; } };
  const api = { int(v) { add(BigInt.asUintN(64, BigInt(v))); return api; }, hex() { return '0x' + h.toString(16).toUpperCase().padStart(16, '0') + 'UL'; } };
  return api;
}
const OPEN = 250, SHORT = 60, HIGH = 1.5, ABOVE = 20, CAVE_SIGHT = 30;
const DENSE = new Set([6, 7, 8]); // Moist (jungle), Wet (rainforest), Cloud (cloud forest)

function make(w) {
  const { nx, ny, cell } = w, W = w.works;
  const cellAt = (x, y) => Math.min(Math.max(Math.floor(y / cell), 0), ny - 1) * nx + Math.min(Math.max(Math.floor(x / cell), 0), nx - 1);
  const ruins = new Map(), wrecks = new Map(), openings = new Map();
  const add = (m, k, v) => { if (!m.has(k)) m.set(k, []); m.get(k).push(v); };
  for (const r of W.ruins) add(ruins, cellAt(r.x, r.y), r.id);
  for (const r of W.wrecks) add(wrecks, cellAt(r.x, r.y), r.id);
  const bk = m => Math.floor(m / 64);
  for (const c of W.caves) {
    for (const [x, y] of c.mouths) add(openings, bk(x) + ',' + bk(y), { cave: c.id, x, y, r: 3.5, sky: false });
    for (const [x, y] of c.skylights) add(openings, bk(x) + ',' + bk(y), { cave: c.id, x, y, r: 2.5, sky: true });
  }
  function near(x, y, range) {
    const out = [];
    for (let by = bk(y - range); by <= bk(y + range); by++) for (let bx = bk(x - range); bx <= bk(x + range); bx++)
      for (const o of openings.get(bx + ',' + by) || []) { const dx = o.x - x, dy = o.y - y; if (dx * dx + dy * dy <= range * range) out.push(o); }
    return out;
  }
  function range(x, y, day, indoors) {
    const cx = Math.min(Math.max(Math.floor(x / cell), 0), nx - 1), cy = Math.min(Math.max(Math.floor(y / cell), 0), ny - 1), i = cy * nx + cx;
    let r = day && !indoors && !DENSE.has(w.biome[i]) ? OPEN : SHORT;
    let sum = 0, n = 0;
    for (let dy = -1; dy <= 1; dy++) for (let dx = -1; dx <= 1; dx++) {
      const ax = cx + dx, ay = cy + dy;
      if ((dx === 0 && dy === 0) || ax < 0 || ay < 0 || ax >= nx || ay >= ny) continue;
      sum += w.height[ay * nx + ax]; n++;
    }
    if (n > 0 && w.height[i] - sum / n >= ABOVE) r *= HIGH;
    return r;
  }
  function see(k, x, y, r, tick) {
    const here = cellAt(x, y);
    if (!k.trail.length || k.trail[k.trail.length - 1].cell !== here) k.trail.push({ cell: here, tick });
    const seeCell = c => {
      if (k.seen.has(c)) return; k.seen.add(c);
      for (const id of ruins.get(c) || []) if (!k.found.has('0,' + id)) k.found.set('0,' + id, { kind: 0, id, how: 0, tick });
      for (const id of wrecks.get(c) || []) if (!k.found.has('1,' + id)) k.found.set('1,' + id, { kind: 1, id, how: 0, tick });
    };
    seeCell(here);
    const cx0 = Math.max(0, Math.floor((x - r) / cell)), cx1 = Math.min(nx - 1, Math.floor((x + r) / cell));
    const cy0 = Math.max(0, Math.floor((y - r) / cell)), cy1 = Math.min(ny - 1, Math.floor((y + r) / cell));
    for (let cy = cy0; cy <= cy1; cy++) for (let cx = cx0; cx <= cx1; cx++) {
      const ddx = Math.max(0, Math.max(cx * cell - x, x - (cx + 1) * cell)), ddy = Math.max(0, Math.max(cy * cell - y, y - (cy + 1) * cell));
      if (ddx * ddx + ddy * ddy <= r * r) seeCell(cy * nx + cx);
    }
    const caves = new Map();
    for (const o of near(x, y, CAVE_SIGHT)) { const dx = o.x - x, dy = o.y - y, how = dx * dx + dy * dy <= o.r * o.r ? 1 : 0; if (!caves.has(o.cave) || (how === 1 && caves.get(o.cave) === 0)) caves.set(o.cave, how); }
    for (const [id, how] of [...caves].sort((a, b) => a[0] - b[0])) if (!k.found.has('2,' + id)) k.found.set('2,' + id, { kind: 2, id, how, tick });
    W.sites.forEach((t, i) => { if (Math.max(Math.abs(x - t.x), Math.abs(y - t.y)) <= t.r) k.sites.add(i); });
  }
  return { range, see, cellAt };
}

function walk(S, k, a, b, day, tick0) {
  const len = Math.sqrt((b.x - a.x) * (b.x - a.x) + (b.y - a.y) * (b.y - a.y)), steps = Math.max(1, Math.ceil(len / 8));
  for (let s = 0; s <= steps; s++) {
    const x = Math.floor(a.x + (b.x - a.x) * s / steps) + 0.5, y = Math.floor(a.y + (b.y - a.y) * s / steps) + 0.5;
    S.see(k, x, y, S.range(x, y, day, false), tick0 + s * 100);
  }
  return tick0 + (steps + 1) * 100;
}

const CASES = [
  ['Default', {}],
  ['Seed12', { seed: 12, scale: 75 }],
  ['SmallNoCliffs', { seed: 44, scale: 50, wind: 100, cliffs: 0, iterations: 5, pali: 0.5 }],
  ['CoolWet', { seed: 9, scale: 60, seaTemp: 21, cloudBase: 500, wetlands: 1.5, rain: 1.3 }]
];
for (const [name, params] of CASES) {
  const w = G.generate(params), W = w.works, S = make(w);
  const docks = W.sites.find(t => t.kind === 'docks'), court = W.sites.find(t => t.kind === 'court'), genetics = W.sites.find(t => t.kind === 'genetics');
  const k = { seen: new Set(), found: new Map(), trail: [], sites: new Set() };
  let tick = walk(S, k, docks, court, true, 0);
  tick = walk(S, k, court, genetics, false, tick);
  // a cave: from 20 m off its first mouth (seen), then a second cave from standing in its mouth (entered)
  const c0 = W.caves[0], c1 = W.caves[1];
  const fx = Math.floor(c0.mouths[0][0] + 20) + 0.5, fy = Math.floor(c0.mouths[0][1]) + 0.5;
  S.see(k, fx, fy, S.range(fx, fy, true, false), tick);
  const ex = c1.mouths[0][0], ey = c1.mouths[0][1];
  S.see(k, ex, ey, S.range(ex, ey, true, false), tick + 100);
  const cells = [...k.seen].sort((a, b) => a - b);
  const hs = hasher().int(cells.length); cells.forEach(c => hs.int(c));
  const feats = [...k.found.values()].sort((a, b) => a.kind - b.kind || a.id - b.id);
  const hf = hasher().int(feats.length); feats.forEach(f => hf.int(f.kind).int(f.id).int(f.how).int(f.tick));
  const ht = hasher().int(k.trail.length); k.trail.forEach(s => ht.int(s.cell).int(s.tick));
  const count = kind => feats.filter(f => f.kind === kind).length;
  const caveHow = id => { const f = k.found.get('2,' + id); return f ? f.how : -1; };
  console.log(`        ["${name}"] = new(${cells.length}, ${hs.hex()}, ${count(0)}, ${count(1)}, ${count(2)}, ${hf.hex()}, ${k.trail.length}, ${ht.hex()}, "${[...k.sites].sort((a, b) => a - b).join(',')}", ${caveHow(c0.id)}, ${caveHow(c1.id)}),`);
}
