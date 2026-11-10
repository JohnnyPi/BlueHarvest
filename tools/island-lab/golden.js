'use strict';
// Golden values for the C# port's stage 1 tests, computed from the lab's reference generator.
const G = require('./gen.js'), I = G._internal, D = I.D;
const PRIME = 1099511628211n, MASK = (1n << 64n) - 1n;
// Same as BlueHarvest.Sim.Core.StateHasher: FNV-1a over each ulong's 8 bytes, little-endian.
function hasher() {
  let h = 14695981039346656037n;
  const add = (u) => { for (let i = 0n; i < 8n; i++) { h ^= (u >> (i * 8n)) & 255n; h = (h * PRIME) & MASK; } };
  const f64 = new Float64Array(1), u64 = new BigUint64Array(f64.buffer);
  return {
    dbl(v) { f64[0] = v; add(u64[0]); return this; },
    int(v) { add(BigInt.asUintN(64, BigInt(v))); return this; },
    hex() { return '0x' + h.toString(16).toUpperCase().padStart(16, '0') + 'UL'; }
  };
}
const out = {};
const bits = (v) => { const f = new Float64Array([v]); return '0x' + new BigUint64Array(f.buffer)[0].toString(16).toUpperCase().padStart(16, '0') + 'UL'; };

// hashing
out.hashStr = ['', 'worldgen.anchors', 'chunk.detail', 'Ωψ'].map(s => [s, I.hashStr(s)]);
out.mix = [0, 1, 0xdeadbeef, 4294967295].map(v => [v, I.mix(v)]);
out.hash2 = [[0, 0, 0], [7, -3, 12], [123456789, 1000000, -2000000], [4294967295, 65535, -1]].map(([s, x, y]) => [s, x, y, I.hash2(s, x, y)]);
out.stream = [[7, 'worldgen.anchors'], [4294967295, 'chunk.cover']].map(([s, n]) => [s, n, I.stream(s, n)]);
{ const r = I.makeRng(7, 'worldgen.anchors'), h = hasher(); const first = []; for (let i = 0; i < 1000; i++) { const v = r(); if (i < 3) first.push(bits(v)); h.dbl(v); } out.rng = { first, hash: h.hex() }; }

// maths
{
  const xs = []; for (let i = 0; i < 4001; i++) xs.push(i * 0.0137 - 27.4);
  const pos = xs.map(x => Math.abs(x) * 3.1 + 0.001);
  const one = (f, arr) => { const h = hasher(); for (const x of arr) h.dbl(f(x)); return h.hex(); };
  out.math = {
    round: one(D.round, xs), hyp: one(x => D.hyp(x, x * 0.7 - 3), xs), exp: one(D.exp, xs), log: one(D.log, pos), pow16: one(x => D.pow(x, 1.6), pos), pow13: one(x => D.pow(x, 1.3), pos),
    tanh: one(D.tanh, xs), sin: one(D.sin, xs), cos: one(D.cos, xs), atan: one(D.atan, xs), atan2: one(x => D.atan2(x, 1.7 - x * 0.31), xs),
    smax: one(x => I.smax(x, -x * 0.5 + 3, 4), xs), smoothstep: one(x => I.smoothstep(-5, 7, x), xs)
  };
  out.mathPoints = [['Exp', 1, bits(D.exp(1))], ['Log', 10, bits(D.log(10))], ['Sin', 1, bits(D.sin(1))], ['Atan', 1, bits(D.atan(1))], ['Tanh', 0.5, bits(D.tanh(0.5))]];
}

// noise
{
  const s = I.stream(7, 'test.noise'), hn = hasher(), hf = hasher(), hr = hasher();
  for (let j = 0; j < 48; j++) for (let i = 0; i < 48; i++) {
    const x = i * 0.731 - 17.3, y = j * 0.613 + 4.1;
    hn.dbl(I.noise(s, x, y)); hf.dbl(I.fbm(s, x, y, 4)); hr.dbl(I.ridged(s, x, y, 4));
  }
  out.noise = { seed: s, noise: hn.hex(), fbm: hf.hex(), ridged: hr.hex(), sample: bits(I.noise(s, 1.25, -3.5)) };
}

// grid helpers
{
  const nx = 40, ny = 30, n = nx * ny, mask = new Uint8Array(n);
  for (let i = 0; i < n; i++) mask[i] = I.hash2(123, i, 9) % 3 !== 0 ? 1 : 0;
  const ch = I.chamfer(mask, nx, ny, 128), hc = hasher(); for (let i = 0; i < n; i++) hc.dbl(ch[i]);
  const label = new Int32Array(n), sizes = I.components(mask, nx, ny, label), hl = hasher(); for (let i = 0; i < n; i++) hl.int(label[i]);
  const bx = 33, by = 21, F = new Float64Array(bx * by); for (let i = 0; i < F.length; i++) F[i] = I.hash2(5, i, 1) / 4294967296 * 100;
  I.boxBlur(F, bx, by, 3, 2); const hb = hasher(); for (const v of F) hb.dbl(v);
  const heap = new I.Heap(4000), rng = I.makeRng(11, 'test.heap'); for (let i = 0; i < 2000; i++) heap.push(Math.floor(rng() * 50) / 5, i);
  const hh = hasher(); while (heap.n > 0) hh.int(heap.pop());
  const r2 = I.makeRng(3, 'test.chaikin'), pts = []; for (let i = 0; i < 12; i++) pts.push([r2() * 1000, r2() * 1000, r2() * 5, r2() * 300]);
  const sm = I.chaikin(pts, 2), hk = hasher(); hk.int(sm.length); for (const q of sm) for (const v of q) hk.dbl(v);
  out.grid = { chamfer: hc.hex(), components: hl.hex(), componentCount: sizes.length, largest: Math.max(...sizes), boxBlur: hb.hex(), heap: hh.hex(), chaikin: hk.hex(), chaikinPoints: sm.length };
}
// drainage: priority-flood + receivers + stack on a hashed dome
{
  const nx = 48, ny = 40, n = nx * ny, cell = 128, H = new Float64Array(n), land = new Uint8Array(n);
  for (let y = 0; y < ny; y++) for (let x = 0; x < nx; x++) {
    const i = y * nx + x, dx = (x - 23.5) / 20, dy = (y - 19.5) / 16, r2 = dx * dx + dy * dy;
    land[i] = r2 < 1 ? 1 : 0;
    H[i] = (1 - r2) * 400 + I.hash2(77, x, y) / 4294967296 * 60;
  }
  const T = I.makeTree(nx, ny, cell), filled = new Float64Array(n), closed = new Uint8Array(n), heap = new I.Heap(n), pit = new Int32Array(n);
  I.flood(H, land, nx, ny, filled, closed, heap, pit);
  I.buildTree(T, H, filled, land);
  const hf = hasher(); for (let i = 0; i < n; i++) hf.dbl(filled[i]);
  const ht = hasher(); for (let i = 0; i < n; i++) { ht.int(T.rec[i]); ht.dbl(T.rdist[i]); if (T.rec[i] === -1) ht.int(T.mouth[i]); }
  const hs = hasher(); hs.int(T.ns); for (let i = 0; i < T.ns; i++) hs.int(T.stack[i]);
  let landN = 0; for (let i = 0; i < n; i++) landN += land[i];
  out.drainage = { filled: hf.hex(), tree: ht.hex(), stack: hs.hex(), ns: T.ns, land: landN };
}
console.log(JSON.stringify(out, null, 1));
