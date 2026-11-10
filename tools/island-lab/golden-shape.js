'use strict';
// Golden hashes for stage 2 of the C# port (IslandShape). One hash per group, so a failing test
// says which part of the shape drifted. Prints the C# table used by IslandShapeTests.
const G = require('./gen.js');
const PRIME = 1099511628211n, MASK = (1n << 64n) - 1n;
function hasher() { // BlueHarvest.Sim.Core.StateHasher
  let h = 14695981039346656037n;
  const add = (u) => { for (let i = 0n; i < 8n; i++) { h ^= (u >> (i * 8n)) & 255n; h = (h * PRIME) & MASK; } };
  const f64 = new Float64Array(1), u64 = new BigUint64Array(f64.buffer);
  const api = {
    dbl(v) { f64[0] = v; add(u64[0]); return api; },
    int(v) { add(BigInt.asUintN(64, BigInt(v))); return api; },
    nan(v) { if (v !== v) api.int(-7); else api.dbl(v); return api; }, // NaN bit patterns differ between runtimes
    dbls(a) { api.int(a.length); for (let i = 0; i < a.length; i++) api.dbl(a[i]); return api; },
    ints(a) { api.int(a.length); for (let i = 0; i < a.length; i++) api.int(a[i]); return api; },
    hex() { return '0x' + h.toString(16).toUpperCase().padStart(16, '0') + 'UL'; }
  };
  return api;
}
function groups(w) {
  const A = w.A, n = w.nx * w.ny, g = {};
  {
    const h = hasher().dbl(A.s).dbl(A.W).dbl(A.H).dbl(A.d[0]).dbl(A.d[1]).dbl(A.n[0]).dbl(A.n[1]);
    for (const S of [A.old, A.young]) h.dbls(S.xs).dbls(S.ys).dbl(S.total).dbl(S.peak).dbl(S.wPos).dbl(S.wNeg).dbl(S.paliSign).dbl(S.paliStr).dbl(S.paliAt).int(S.nPali);
    const sg = A.saddle.seg[0];
    h.dbl(sg.ax).dbl(sg.ay).dbl(sg.dx).dbl(sg.dy).dbl(sg.l2).dbl(sg.len).dbl(sg.c0).dbl(A.saddle.w).dbl(A.saddle.crest);
    const C = A.caldera, V = A.vent;
    h.dbl(C.x).dbl(C.y).dbl(C.R).dbl(C.depth).dbl(C.rim).dbl(V.x).dbl(V.y).dbl(V.h).dbl(V.R);
    h.int(A.islets.length); for (const I of A.islets) h.dbl(I.x).dbl(I.y).dbl(I.h).dbl(I.R);
    for (const k of ['nWarpX', 'nWarpY', 'nRidge', 'nDetail', 'nCoast', 'nLava', 'nErod', 'nCliff', 'nBiome', 'nReef', 'nTile', 'nTile2', 'nCover']) h.int(A[k]);
    g.Anchors = h.hex();
  }
  g.Landmass = hasher().dbls(w.oldW).dbls(w.lava).dbls(w.erod).dbls(w.pali).hex();
  g.Coast = hasher().dbls(w.H0).ints(w.land).ints(w.ocean).dbls(w.distLand).dbls(w.distSea).dbl(w.cutKm2).hex();
  g.Erosion = hasher().dbls(w.H).dbls(w.filled).dbls(w.rain).hex();
  {
    const T = w.T, h = hasher().ints(T.rec).dbls(T.rdist);
    for (let i = 0; i < n; i++) if (T.rec[i] === -1) h.int(T.mouth[i]);
    h.int(T.ns); for (let i = 0; i < T.ns; i++) h.int(T.stack[i]);
    g.Tree = h.hex();
  }
  {
    const h = hasher().int(w.lakes.length);
    for (const L of w.lakes) h.int(L.id).dbl(L.area).dbl(L.depth).dbl(L.level).dbl(L.cx).dbl(L.cy);
    h.int(n); for (let i = 0; i < n; i++) h.nan(w.lake[i]);
    g.Lakes = h.hex();
  }
  {
    const h = hasher().dbls(w.Q).dbls(w.toSea).ints(w.isRiver).ints(w.heads).int(w.rivers.length);
    for (const r of w.rivers) h.int(r.end === 'sea' ? 1 : 0).int(r.head).dbls(r.xs).dbls(r.ys).dbls(r.qs).dbls(r.hs);
    h.dbl(w.streamLen);
    g.Rivers = h.hex();
  }
  {
    const h = hasher().int(w.falls.length);
    for (const f of w.falls) h.dbl(f.x).dbl(f.y).dbl(f.drop).dbl(f.q).int(f.sea ? 1 : 0).int(f.river ? 1 : 0);
    g.Falls = h.hex();
  }
  g.CoastClasses = hasher().ints(w.coast).ints(w.sea).dbls(w.coastNX).dbls(w.coastNY).dbls(w.coastEx).dbls(w.cliffMask)
    .dbl(w.seaCliffTop).int(w.seaCliffCells).int(w.paliCells).dbl(w.paliDrop).hex();
  return g;
}
const CASES = [
  ['Default', {}],
  ['Seed12', { seed: 12 }],
  ['SmallNoCliffs', { seed: 44, scale: 50, wind: 100, cliffs: 0, iterations: 5, pali: 0.5 }]
];
const out = [];
for (const [name, params] of CASES) {
  const w = G.generate(Object.assign({ stopAfter: 'shape' }, params));
  const g = groups(w);
  const facts = { nx: w.nx, ny: w.ny, islets: w.A.islets.length, lakes: w.lakes.length, rivers: w.rivers.length, falls: w.falls.length };
  out.push({ name, params, facts, g });
}
if (process.argv[2] === 'json') { console.log(JSON.stringify(out, null, 1)); process.exit(0); }
for (const c of out) {
  console.log(`        ["${c.name}"] = new(${c.facts.nx}, ${c.facts.ny}, ${c.facts.islets}, ${c.facts.lakes}, ${c.facts.rivers}, ${c.facts.falls},`);
  const keys = Object.keys(c.g);
  console.log('            ' + keys.map(k => c.g[k]).join(', ') + '),');
}
console.log('// groups: ' + Object.keys(out[0].g).join(', '));
