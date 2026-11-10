'use strict';
// Island generator for the Forgotten Land island lab.
// Pure functions, no DOM: runs in a Web Worker, on the main thread, or in Node.
// Everything is a function of (params, seed); per-point randomness is addressed
// (hash of seed, stream, coordinates) so chunks agree at their seams.
var IslandGen = (function () {
  const SQ2 = Math.SQRT2;
  const DX = [1, 1, 0, -1, -1, -1, 0, 1];
  const DY = [0, 1, 1, 1, 0, -1, -1, -1];
  const DL = [1, SQ2, 1, SQ2, 1, SQ2, 1, SQ2];
  const OAHU_W = 86000, OAHU_H = 62000; // frame around an Oahu-sized island, metres
  const CHUNK = 64;

  const DEFAULTS = {
    seed: 7, scale: 75, cell: 128, peak: 1100, saddle: 240, rough: 0.6, warp: 5,
    wind: 60, rain: 1, erosion: 1, iterations: 8, lakeDepth: 40, lakeArea: 0.15, riverQ: 0.1,
    pali: 0.85, cliffs: 1.2, seaTemp: 24, cloudBase: 650, wetlands: 1,
    paddockSize: 1, tubes: 5, wrecks: 10, ruins: 16, transit: 1
  };

  // ---------- portable maths ----------
  // Built only from + - * / and sqrt, which IEEE 754 makes identical on every machine,
  // so the C# port (BlueHarvest.Sim.World.Generation.GenMath) reproduces every bit.
  const D = (function () {
    const LN2_HI = 0.6931471803691238, LN2_LO = 1.9082149292705877e-10, LN2 = 0.6931471805599453, PI = 3.141592653589793, TWO_PI = 6.283185307179586, HALF_PI = 1.5707963267948966;
    function round(x) { return Math.floor(x + 0.5); }
    function hyp(x, y) { return Math.sqrt(x * x + y * y); }
    function scale2(v, k) { // v * 2^k, exactly
      if (k > 0) { while (k > 0) { v *= 2; k--; } } else { while (k < 0) { v *= 0.5; k++; } }
      return v;
    }
    function exp(x) {
      if (x > 700) return Infinity; if (x < -700) return 0;
      const k = Math.floor(x / LN2 + 0.5), r = (x - k * LN2_HI) - k * LN2_LO;
      // Taylor series to r^13, |r| <= 0.35
      let t = 1; for (let i = 13; i >= 1; i--) t = 1 + r * t / i;
      return scale2(t, k);
    }
    function log(x) {
      if (!(x > 0)) return x === 0 ? -Infinity : NaN;
      let e = 0; while (x >= 2) { x *= 0.5; e++; } while (x < 1) { x *= 2; e--; }
      if (x > 1.4142135623730951) { x *= 0.5; e++; }
      const s = (x - 1) / (x + 1), s2 = s * s;
      let sum = 0; for (let i = 41; i >= 1; i -= 2) sum = 1 / i + s2 * sum;
      return 2 * s * sum + e * LN2;
    }
    function pow(a, b) { if (a <= 0) return a === 0 ? (b === 0 ? 1 : 0) : NaN; if (b === 0.5) return Math.sqrt(a); if (b === 1.5) return a * Math.sqrt(a); return exp(b * log(a)); }
    function tanh(x) { if (x > 20) return 1; if (x < -20) return -1; const e2 = exp(2 * x); return (e2 - 1) / (e2 + 1); }
    function sin(x) {
      x = x - TWO_PI * Math.floor(x / TWO_PI + 0.5); // to [-pi, pi]
      if (x > HALF_PI) x = PI - x; else if (x < -HALF_PI) x = -PI - x; // to [-pi/2, pi/2]
      const x2 = x * x; let t = 1;
      for (let i = 23; i >= 3; i -= 2) t = 1 - x2 * t / (i * (i - 1));
      return x * t;
    }
    function cos(x) { return sin(x + HALF_PI); }
    function atan(x) {
      if (x < 0) return -atan(-x);
      let off = 0;
      if (x > 1) { x = 1 / x; off = 1; }
      // two half-angle steps bring x under tan(pi/16)
      const h1 = x / (1 + Math.sqrt(1 + x * x)), h2 = h1 / (1 + Math.sqrt(1 + h1 * h1)), z2 = h2 * h2;
      let sum = 0; for (let i = 31; i >= 1; i -= 2) sum = 1 / i - z2 * sum;
      const a = 4 * h2 * sum;
      return off ? HALF_PI - a : a;
    }
    function atan2(y, x) {
      if (x > 0) return atan(y / x);
      if (x < 0) return y >= 0 ? atan(y / x) + PI : atan(y / x) - PI;
      return y > 0 ? HALF_PI : y < 0 ? -HALF_PI : 0;
    }
    return { round, hyp, exp, log, pow, tanh, sin, cos, atan, atan2, PI };
  })();

  // ---------- hashing, RNG, noise ----------
  function hashStr(s) {
    let h = 0x811c9dc5;
    for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 0x01000193); }
    return h >>> 0;
  }
  function mix(h) {
    h ^= h >>> 16; h = Math.imul(h, 0x7feb352d);
    h ^= h >>> 15; h = Math.imul(h, 0x846ca68b);
    h ^= h >>> 16; return h >>> 0;
  }
  function hash2(s, x, y) {
    return mix(Math.imul(x | 0, 0x27d4eb2d) ^ mix(Math.imul(y | 0, 0x165667b1) ^ s));
  }
  function stream(seed, name) { return mix((seed >>> 0) ^ hashStr(name)); }
  function makeRng(seed, name) {
    let a = stream(seed, name) | 0;
    return function () {
      a = (a + 0x6D2B79F5) | 0;
      let t = Math.imul(a ^ (a >>> 15), 1 | a);
      t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
      return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
  }
  const GX = new Float64Array(16), GY = new Float64Array(16);
  for (let i = 0; i < 16; i++) { const a = (i + 0.5) * Math.PI / 8; GX[i] = D.cos(a); GY[i] = D.sin(a); }
  function noise(s, x, y) {
    const ix = Math.floor(x), iy = Math.floor(y), fx = x - ix, fy = y - iy;
    const u = fx * fx * fx * (fx * (fx * 6 - 15) + 10), v = fy * fy * fy * (fy * (fy * 6 - 15) + 10);
    let h = hash2(s, ix, iy) & 15; const a = GX[h] * fx + GY[h] * fy;
    h = hash2(s, ix + 1, iy) & 15; const b = GX[h] * (fx - 1) + GY[h] * fy;
    h = hash2(s, ix, iy + 1) & 15; const c = GX[h] * fx + GY[h] * (fy - 1);
    h = hash2(s, ix + 1, iy + 1) & 15; const d = GX[h] * (fx - 1) + GY[h] * (fy - 1);
    const ab = a + u * (b - a), cd = c + u * (d - c);
    return (ab + v * (cd - ab)) * 1.41;
  }
  function fbm(s, x, y, oct) {
    let sum = 0, amp = 1, norm = 0;
    for (let o = 0; o < oct; o++) {
      sum += amp * noise((s + Math.imul(o + 1, 0x9E3779B1)) >>> 0, x, y);
      norm += amp; amp *= 0.5; x = x * 2.03 + 17.1; y = y * 2.03 - 9.7;
    }
    return sum / norm;
  }
  function ridged(s, x, y, oct) {
    let sum = 0, amp = 1, norm = 0, w = 1;
    for (let o = 0; o < oct; o++) {
      let n = 1 - Math.abs(noise((s + Math.imul(o + 1, 0x85EBCA6B)) >>> 0, x, y));
      n *= n; n *= w; w = Math.min(1, n * 2);
      sum += n * amp; norm += amp; amp *= 0.5; x = x * 2.03 + 3.3; y = y * 2.03 + 7.9;
    }
    return sum / norm;
  }
  function clamp(v, a, b) { return v < a ? a : v > b ? b : v; }
  function smax(a, b, k) { const h = Math.max(k - Math.abs(a - b), 0) / k; return Math.max(a, b) + h * h * k * 0.25; }
  function smoothstep(a, b, v) { const t = clamp((v - a) / (b - a), 0, 1); return t * t * (3 - 2 * t); }

  // ---------- global anchors, drawn once per save ----------
  function polySegs(xs, ys) {
    const seg = []; let c0 = 0;
    for (let k = 0; k + 1 < xs.length; k++) {
      const dx = xs[k + 1] - xs[k], dy = ys[k + 1] - ys[k], l2 = dx * dx + dy * dy, len = Math.sqrt(l2);
      seg.push({ ax: xs[k], ay: ys[k], dx, dy, l2, len, c0 }); c0 += len;
    }
    return { seg, total: c0 };
  }
  function pointAlong(xs, ys, f) {
    const ps = polySegs(xs, ys); let target = f * ps.total;
    for (const g of ps.seg) { if (target <= g.len) { const t = target / g.len; return [g.ax + g.dx * t, g.ay + g.dy * t]; } target -= g.len; }
    return [xs[xs.length - 1], ys[ys.length - 1]];
  }

  function makeAnchors(p) {
    const s = p.scale / 100, r = makeRng(p.seed, 'worldgen.anchors');
    const W = OAHU_W * s, H = OAHU_H * s;
    const th = (38 + (r() - 0.5) * 24) * Math.PI / 180;
    const d = [D.cos(th), D.sin(th)];   // along the shields, NW -> SE
    const n = [D.sin(th), -D.cos(th)];  // across, SW -> NE
    const c = [W * (0.5 + (r() - 0.5) * 0.05), H * (0.5 + (r() - 0.5) * 0.05)];
    const sep = (20000 + r() * 6000) * s;
    function ridge(cx, cy, len, bend, count) {
      const xs = [], ys = [];
      for (let k = 0; k < count; k++) {
        const t = k / (count - 1) - 0.5;
        const off = bend * (1 - 4 * t * t) + (r() - 0.5) * 2400 * s;
        xs.push(cx + d[0] * t * len + n[0] * off);
        ys.push(cy + d[1] * t * len + n[1] * off);
      }
      return { xs, ys };
    }
    function shield(name, cx, cy, len, bend, count, peak, wPos, wNeg, old) {
      const rg = ridge(cx, cy, len, bend, count), ps = polySegs(rg.xs, rg.ys);
      return { name, xs: rg.xs, ys: rg.ys, seg: ps.seg, total: ps.total, peak, wPos, wNeg, old };
    }
    const oa = ((r() - 0.5) * 7000 - 3500) * s, ya = ((r() - 0.5) * 7000 + 2500) * s;
    const oldC = [c[0] - n[0] * sep / 2 + d[0] * oa, c[1] - n[1] * sep / 2 + d[1] * oa];
    const youngC = [c[0] + n[0] * sep / 2 + d[0] * ya, c[1] + n[1] * sep / 2 + d[1] * ya];
    const old = shield('older shield', oldC[0], oldC[1], (27000 + r() * 8000) * s, (r() - 0.5) * 5000 * s, 4,
      p.peak * (0.8 + r() * 0.1), (6000 + r() * 2000) * s, (8000 + r() * 2000) * s, 1);
    const young = shield('younger shield', youngC[0], youngC[1], (40000 + r() * 10000) * s, (r() - 0.5) * 6000 * s, 5,
      p.peak, (5500 + r() * 2000) * s, (7500 + r() * 2000) * s, 0);
    // Pali: the flank facing the trade wind is cut back into a wall (younger shield strongest).
    const wa = p.wind * Math.PI / 180, wf = [D.sin(wa), -D.cos(wa)], wn = wf[0] * n[0] + wf[1] * n[1];
    for (const S of [old, young]) {
      S.s = s; S.paliSign = wn >= 0 ? 1 : -1;
      S.paliStr = p.pali * clamp(Math.abs(wn) * 1.4, 0, 1) * (S.old ? 0.55 : 1);
      S.paliAt = S.old ? 0.2 : 0.08; S.nPali = stream(p.seed, 'worldgen.pali.' + S.name); S.lastPali = 0;
    }
    const sad = polySegs([oldC[0], youngC[0]], [oldC[1], youngC[1]]);
    const saddle = { seg: sad.seg, w: (6500 + r() * 2500) * s, crest: p.saddle };
    const cp = pointAlong(old.xs, old.ys, 0.35 + r() * 0.3);
    const caldera = { x: cp[0], y: cp[1], R: (1700 + r() * 1000) * s, depth: old.peak * (0.2 + r() * 0.08), rim: 45 };
    const yl = young.xs.length - 1;
    const vo = (2000 + r() * 3000) * s;
    const vent = {
      x: young.xs[yl] + d[0] * 1500 * s - n[0] * vo, y: young.ys[yl] + d[1] * 1500 * s - n[1] * vo,
      h: 220 + r() * 220, R: (2400 + r() * 1400) * s
    };
    const A = {
      s, W, H, d, n, old, young, saddle, caldera, vent, islets: [], peak: p.peak, rough: p.rough,
      warp: p.warp * 1000 * s,
      nWarpX: stream(p.seed, 'worldgen.warpx'), nWarpY: stream(p.seed, 'worldgen.warpy'),
      nRidge: stream(p.seed, 'worldgen.ridge'), nDetail: stream(p.seed, 'worldgen.detail'),
      nCoast: stream(p.seed, 'worldgen.coast'), nLava: stream(p.seed, 'worldgen.lava'),
      nErod: stream(p.seed, 'worldgen.erod'), nCliff: stream(p.seed, 'worldgen.cliff'), nBiome: stream(p.seed, 'worldgen.biome'), nReef: stream(p.seed, 'worldgen.reef'),
      nTile: stream(p.seed, 'chunk.detail'), nTile2: stream(p.seed, 'chunk.fine'), nCover: stream(p.seed, 'chunk.cover')
    };
    // Islets: rejection sampling on the base field (offshore, not too far out)
    const ri = makeRng(p.seed, 'worldgen.islets'), count = 3 + Math.floor(ri() * 6), o = {};
    const margin = 9000 * s;
    for (let k = 0, tries = 0; k < count && tries < 600; tries++) {
      const x = margin + ri() * (W - 2 * margin), y = margin + ri() * (H - 2 * margin);
      sample(A, x, y, o);
      if (o.e > -40 || o.e < -380) continue;
      let ok = true;
      for (const q of A.islets) if (D.hyp(q.x - x, q.y - y) < 3500 * s) ok = false;
      if (!ok) continue;
      A.islets.push({ x, y, h: 25 + ri() * 140, R: (500 + ri() * 1300) * s }); k++;
    }
    return A;
  }

  function shieldAt(S, x, y) {
    let best = 1e30, sd = 0, tpos = 0;
    for (let k = 0; k < S.seg.length; k++) {
      const g = S.seg[k];
      let t = ((x - g.ax) * g.dx + (y - g.ay) * g.dy) / g.l2;
      t = t < 0 ? 0 : t > 1 ? 1 : t;
      const px = g.ax + g.dx * t - x, py = g.ay + g.dy * t - y, d2 = px * px + py * py;
      if (d2 < best) {
        best = d2;
        sd = -(g.dx * (y - g.ay) - g.dy * (x - g.ax)) / g.len; // + on the NE side
        tpos = (g.c0 + t * g.len) / S.total - 0.5;
      }
    }
    const dist = Math.sqrt(best);
    const f = 0.5 + 0.5 * D.tanh(sd / 2500);
    const w = S.wNeg + (S.wPos - S.wNeg) * f;
    const crest = S.peak * (1 - 1.5 * tpos * tpos);
    const xr = dist / w;
    S.lastPali = 0;
    if (xr >= 1) return -(xr - 1) * crest * 0.35;
    let e = crest * D.pow(1 - xr, 1.5);
    if (S.paliStr > 0) {
      const fw = 0.5 + 0.5 * D.tanh(S.paliSign * sd / 500); // how much this point is on the windward flank
      if (fw > 0.02) {
        // the wall line wanders and bites back in amphitheatre-headed scallops
        const along = (tpos + 0.5) * S.total;
        const sc = noise(S.nPali, along / (2600 * S.s), 3.7) * 0.55 + Math.abs(noise((S.nPali ^ 0x77) >>> 0, along / (900 * S.s), 1.3)) * 0.45;
        const xc = S.paliAt + 0.12 * sc, wallN = 420 * Math.sqrt(S.s) / w;
        const m = smoothstep(xc, xc + wallN, xr);
        if (m > 0) {
          const top = crest * D.pow(1 - xc, 1.5), u = (xr - xc) / (1 - xc);
          const apron = top * 0.13 * D.pow(Math.max(0, 1 - u), 1.6) + e * 0.12; // talus apron and a low coastal plain
          if (apron < e) { const k = m * S.paliStr * fw; e -= (e - apron) * k; S.lastPali = k; }
        }
      }
    }
    return e;
  }
  function saddleAt(S, x, y) {
    const g = S.seg[0];
    const t0 = ((x - g.ax) * g.dx + (y - g.ay) * g.dy) / g.l2, t = t0 < 0 ? 0 : t0 > 1 ? 1 : t0;
    // the saddle only fills between the shields: past either centre it falls away four times faster
    const over = (t0 < 0 ? -t0 : t0 > 1 ? t0 - 1 : 0) * g.len * 3;
    const perp = Math.abs(g.dx * (y - g.ay) - g.dy * (x - g.ax)) / g.len;
    const xr = D.hyp(perp, over + D.hyp(g.ax + g.dx * t - x, g.ay + g.dy * t - y) * 0) / S.w;
    return xr < 1 ? S.crest * D.pow(1 - xr, 0.9) : -(xr - 1) * S.crest * 0.5;
  }

  // Base height (m), older-shield weight and young-lava weight at a point.
  function sample(A, x, y, out) {
    const s = A.s, lw = 16000 * s;
    const wx = x + A.warp * fbm(A.nWarpX, x / lw, y / lw, 3);
    const wy = y + A.warp * fbm(A.nWarpY, x / lw, y / lw, 3);
    const eo = shieldAt(A.old, wx, wy), ey = shieldAt(A.young, wx, wy);
    let e = smax(eo, ey, 400);
    e = smax(e, saddleAt(A.saddle, wx, wy), 250);
    const C = A.caldera;
    let rr = D.hyp(wx - C.x, wy - C.y) / C.R;
    if (rr < 2) { if (rr < 1) e -= C.depth * (1 - rr * rr); const q = (rr - 1) / 0.3; e += C.rim * D.exp(-q * q); }
    const V = A.vent;
    rr = D.hyp(wx - V.x, wy - V.y) / V.R;
    const lava = clamp(1.15 - rr / 2.2 + 0.35 * fbm(A.nLava, x / (1800 * s), y / (1800 * s), 3), 0, 1);
    if (rr < 1) { e += V.h * D.pow(1 - rr, 1.6); const rc = rr / 0.16; if (rc < 1) e -= V.h * 0.3 * (1 - rc * rc); }
    const mount = clamp(e / A.peak, 0, 1);
    e += A.peak * 0.16 * mount * (ridged(A.nRidge, wx / (6500 * s), wy / (6500 * s), 4) - 0.35);
    e += 18 * fbm(A.nDetail, x / (1400 * s), y / (1400 * s), 3) * (0.3 + mount);
    e += A.rough * 220 * fbm(A.nCoast, wx / (9000 * s), wy / (9000 * s), 4);
    e += A.rough * 70 * fbm(A.nCoast ^ 0x2c1b, x / (2600 * s), y / (2600 * s), 3);
    for (let k = 0; k < A.islets.length; k++) {
      const I = A.islets[k], ri = D.hyp(x - I.x, y - I.y) / I.R;
      if (ri < 4) e = smax(e, ri < 1 ? I.h * D.pow(1 - ri, 1.3) : -(ri - 1) * 180, 120);
    }
    const de = Math.min(x, y, A.W - x, A.H - y), m = 7000 * s;
    if (de < m) { const q = 1 - de / m; e -= 3500 * q * q; }
    out.e = e;
    out.pali = eo > ey ? A.old.lastPali : A.young.lastPali;
    out.old = 1 / (1 + D.exp(-(eo - ey) / 150));
    out.lava = lava;
  }

  // ---------- grid helpers ----------
  function chamfer(src, nx, ny, cell) { // distance from every cell with src=1 to the nearest cell with src=0
    const n = nx * ny, D = new Float32Array(n), a = cell, b = cell * SQ2;
    for (let i = 0; i < n; i++) D[i] = src[i] ? 1e9 : 0;
    for (let y = 0; y < ny; y++) for (let x = 0; x < nx; x++) {
      const i = y * nx + x; let v = D[i]; if (v === 0) continue;
      if (x > 0) v = Math.min(v, D[i - 1] + a);
      if (y > 0) { v = Math.min(v, D[i - nx] + a); if (x > 0) v = Math.min(v, D[i - nx - 1] + b); if (x < nx - 1) v = Math.min(v, D[i - nx + 1] + b); }
      D[i] = v;
    }
    for (let y = ny - 1; y >= 0; y--) for (let x = nx - 1; x >= 0; x--) {
      const i = y * nx + x; let v = D[i]; if (v === 0) continue;
      if (x < nx - 1) v = Math.min(v, D[i + 1] + a);
      if (y < ny - 1) { v = Math.min(v, D[i + nx] + a); if (x < nx - 1) v = Math.min(v, D[i + nx + 1] + b); if (x > 0) v = Math.min(v, D[i + nx - 1] + b); }
      D[i] = v;
    }
    return D;
  }
  function boxBlur(F, nx, ny, r, passes) {
    const tmp = new Float64Array(F.length);
    for (let p = 0; p < passes; p++) {
      for (let y = 0; y < ny; y++) {
        const row = y * nx; let acc = 0, cnt = 0;
        for (let x = -r; x < nx; x++) {
          const add = x + r, rem = x - r - 1;
          if (add < nx) { acc += F[row + add]; cnt++; }
          if (rem >= 0) { acc -= F[row + rem]; cnt--; }
          if (x >= 0) tmp[row + x] = acc / cnt;
        }
      }
      for (let x = 0; x < nx; x++) {
        let acc = 0, cnt = 0;
        for (let y = -r; y < ny; y++) {
          const add = y + r, rem = y - r - 1;
          if (add < ny) { acc += tmp[add * nx + x]; cnt++; }
          if (rem >= 0) { acc -= tmp[rem * nx + x]; cnt--; }
          if (y >= 0) F[y * nx + x] = acc / cnt;
        }
      }
    }
  }

  // Min-heap of (key, cell).
  function Heap(cap) { this.k = new Float64Array(cap + 1); this.v = new Int32Array(cap + 1); this.n = 0; }
  Heap.prototype.push = function (key, val) {
    let i = ++this.n; const k = this.k, v = this.v;
    while (i > 1) { const p = i >> 1; if (k[p] <= key) break; k[i] = k[p]; v[i] = v[p]; i = p; }
    k[i] = key; v[i] = val;
  };
  Heap.prototype.pop = function () {
    const k = this.k, v = this.v, top = v[1], lk = k[this.n], lv = v[this.n], n = --this.n;
    let i = 1;
    while (true) {
      let c = i << 1; if (c > n) break;
      if (c < n && k[c + 1] < k[c]) c++;
      if (k[c] >= lk) break;
      k[i] = k[c]; v[i] = v[c]; i = c;
    }
    k[i] = lk; v[i] = lv; return top;
  };

  // Priority-flood + epsilon (Barnes et al. 2014). Fills depressions so every land cell drains.
  const EPS = 1e-4;
  function flood(H, land, nx, ny, filled, closed, heap, pit) {
    const n = nx * ny; closed.fill(0); heap.n = 0;
    for (let i = 0; i < n; i++) {
      if (!land[i]) continue;
      const x = i % nx, y = (i - x) / nx; let coast = false;
      for (let k = 0; k < 8; k++) {
        const xx = x + DX[k], yy = y + DY[k];
        if (xx < 0 || yy < 0 || xx >= nx || yy >= ny || !land[yy * nx + xx]) { coast = true; break; }
      }
      if (coast) { filled[i] = H[i]; closed[i] = 1; heap.push(H[i], i); }
    }
    let ph = 0, pt = 0;
    while (heap.n > 0 || ph < pt) {
      let c;
      if (ph < pt) c = pit[ph++]; else { ph = pt = 0; c = heap.pop(); }
      const x = c % nx, y = (c - x) / nx, fc = filled[c] + EPS;
      for (let k = 0; k < 8; k++) {
        const xx = x + DX[k], yy = y + DY[k];
        if (xx < 0 || yy < 0 || xx >= nx || yy >= ny) continue;
        const j = yy * nx + xx;
        if (!land[j] || closed[j]) continue;
        closed[j] = 1;
        if (H[j] <= fc) { filled[j] = fc; pit[pt++] = j; } else { filled[j] = H[j]; heap.push(H[j], j); }
      }
    }
  }

  // Steepest descent receivers on the filled surface, plus a base-to-top stack (Braun & Willett 2013).
  function buildTree(T, H, filled, land) {
    const { nx, ny, n, cell } = T, rec = T.rec, rdist = T.rdist, mouth = T.mouth;
    const cnt = T.donStart; cnt.fill(0);
    for (let i = 0; i < n; i++) {
      rec[i] = -2; if (!land[i]) continue;
      const x = i % nx, y = (i - x) / nx; let best = -1, bs = 0, bd = cell, bo = -1, bos = -1e30;
      for (let k = 0; k < 8; k++) {
        const xx = x + DX[k], yy = y + DY[k];
        if (xx < 0 || yy < 0 || xx >= nx || yy >= ny) continue;
        const j = yy * nx + xx, dl = DL[k] * cell;
        if (!land[j]) { const sl = (filled[i] - 0) / dl; if (sl > bos) { bos = sl; bo = j; } continue; }
        const sl = (filled[i] - filled[j]) / dl;
        if (sl > bs) { bs = sl; best = j; bd = dl; }
      }
      if (bo >= 0 && bos >= bs) { rec[i] = -1; mouth[i] = bo; rdist[i] = D.hyp((bo % nx) - x, Math.floor(bo / nx) - y) * cell; }
      else if (best >= 0) { rec[i] = best; rdist[i] = bd; cnt[best + 1]++; }
      else { rec[i] = -1; mouth[i] = -1; rdist[i] = cell; } // shouldn't happen after flooding
    }
    for (let i = 0; i < n; i++) cnt[i + 1] += cnt[i];
    const fillp = T.tmpI; for (let i = 0; i <= n; i++) fillp[i] = cnt[i];
    for (let i = 0; i < n; i++) { const r = rec[i]; if (r >= 0) T.donors[fillp[r]++] = i; }
    let ns = 0; const st = T.stack, dfs = T.dfs;
    for (let i = 0; i < n; i++) {
      if (rec[i] !== -1) continue;
      let top = 0; dfs[top++] = i;
      while (top > 0) {
        const c = dfs[--top]; st[ns++] = c;
        for (let q = cnt[c]; q < cnt[c + 1]; q++) dfs[top++] = T.donors[q];
      }
    }
    T.ns = ns;
  }
  function makeTree(nx, ny, cell) {
    const n = nx * ny;
    return {
      nx, ny, n, cell, rec: new Int32Array(n), rdist: new Float32Array(n), mouth: new Int32Array(n),
      donStart: new Int32Array(n + 1), donors: new Int32Array(n), tmpI: new Int32Array(n + 1),
      stack: new Int32Array(n), dfs: new Int32Array(n), ns: 0
    };
  }

  // Orographic rain from the trade winds: sweep moisture downwind, lift wrings it out.
  const UX = [0, 1, 1, 1, 0, -1, -1, -1], UY = [-1, -1, 0, 1, 1, 1, 0, -1]; // toward N, NE, E, ... (upwind offsets)
  function computeRain(H, land, nx, ny, cell, windDeg, mult) {
    const n = nx * ny, out = new Float64Array(n), m = new Float64Array(n);
    function sweep(k, weight) {
      if (weight <= 0) return;
      const ox = UX[k], oy = UY[k], step = cell * (ox && oy ? SQ2 : 1);
      const recharge = 1 - D.exp(-step / 15000);
      const ys = oy > 0 ? ny - 1 : 0, ye = oy > 0 ? -1 : ny, yd = oy > 0 ? -1 : 1;
      const xs = ox > 0 ? nx - 1 : 0, xe = ox > 0 ? -1 : nx, xd = ox > 0 ? -1 : 1;
      for (let y = ys; y !== ye; y += yd) for (let x = xs; x !== xe; x += xd) {
        const i = y * nx + x, ux = x + ox, uy = y + oy;
        let mu = 1, hu = 0;
        if (ux >= 0 && uy >= 0 && ux < nx && uy < ny) { const u = uy * nx + ux; mu = m[u]; hu = land[u] ? Math.max(0, H[u]) : 0; }
        let r;
        if (!land[i]) { m[i] = mu + (1 - mu) * recharge; r = 250 + 450 * mu; }
        else {
          const h = Math.max(0, H[i]), lift = h - hu;
          const ro = lift > 0 ? mu * Math.min(0.6, lift / 1700) : 0;
          m[i] = Math.max(0.02, mu - ro);
          r = 220 + 650 * mu + (ro / step) * 4.2e7;
        }
        out[i] += weight * r;
      }
    }
    function from(deg, weight) {
      const a = ((deg % 360) + 360) % 360, k0 = Math.floor(a / 45) % 8, k1 = (k0 + 1) % 8, w1 = (a - k0 * 45) / 45;
      sweep(k0, weight * (1 - w1)); sweep(k1, weight * w1);
    }
    from(windDeg, 1);
    from(windDeg + 150, 0.2); // winter Kona storms come from the other side and wet the leeward slopes
    boxBlur(out, nx, ny, Math.max(1, D.round(700 / cell)), 2);
    for (let i = 0; i < n; i++) out[i] *= mult;
    return out;
  }

  function components(mask, nx, ny, label) { // 8-connected; returns sizes
    label.fill(-1); const sizes = [], q = new Int32Array(nx * ny);
    for (let s0 = 0; s0 < nx * ny; s0++) {
      if (!mask[s0] || label[s0] >= 0) continue;
      const id = sizes.length; let qh = 0, qt = 0; q[qt++] = s0; label[s0] = id; let sz = 0;
      while (qh < qt) {
        const c = q[qh++]; sz++; const x = c % nx, y = (c - x) / nx;
        for (let k = 0; k < 8; k++) {
          const xx = x + DX[k], yy = y + DY[k];
          if (xx < 0 || yy < 0 || xx >= nx || yy >= ny) continue;
          const j = yy * nx + xx; if (mask[j] && label[j] < 0) { label[j] = id; q[qt++] = j; }
        }
      }
      sizes.push(sz);
    }
    return sizes;
  }

  function chaikin(pts, iters) { // pts: array of [x,y,q,h]; endpoints kept
    for (let it = 0; it < iters; it++) {
      if (pts.length < 3) return pts;
      const out = [pts[0]];
      for (let k = 0; k + 1 < pts.length; k++) {
        const a = pts[k], b = pts[k + 1];
        const q1 = [], q2 = [];
        for (let c = 0; c < 4; c++) { q1.push(a[c] * 0.75 + b[c] * 0.25); q2.push(a[c] * 0.25 + b[c] * 0.75); }
        if (k > 0) out.push(q1);
        if (k + 1 < pts.length - 1) out.push(q2);
      }
      out.push(pts[pts.length - 1]);
      pts = out;
    }
    return pts;
  }

  function riverWidth(q) { return Math.max(1.5, 2.6 * Math.sqrt(q)); }
  function riverDepth(q) { return Math.max(0.3, 0.3 * D.pow(q, 0.4)); }

  // ---------- stage 3 tables ----------
  const BIOMES = [
    { key: 'ocean', name: 'Ocean', color: '#14304a' },
    { key: 'strand', name: 'Beach and strand', color: '#e3cf98' },
    { key: 'marsh', name: 'Marsh and wetland', color: '#5d9483' },
    { key: 'dryscrub', name: 'Dry scrub', color: '#c2a067' },
    { key: 'grassland', name: 'Grassland', color: '#bcc47c' },
    { key: 'mesic', name: 'Open woodland', color: '#8cab5e' },
    { key: 'moist', name: 'Jungle', color: '#4e8f4d' },
    { key: 'wet', name: 'Rainforest', color: '#2d6a42' },
    { key: 'cloud', name: 'Cloud forest', color: '#5f9a8e' },
    { key: 'ridge', name: 'Ridge shrub', color: '#8f9c80' },
    { key: 'cliff', name: 'Cliff and bare rock', color: '#9d968b' },
    { key: 'lava', name: 'Young lava', color: '#4b403b' },
    { key: 'lake', name: 'Lake', color: '#3f86ad' }
  ];
  const B = {}; BIOMES.forEach((b, k) => { B[b.key] = k; });
  // The design extract's eight regions (0 = sea). Each will bias the director differently.
  const REGIONS = [
    { key: 'sea', name: 'Sea', color: '#14304a' },
    { key: 'windward', name: 'Windward valleys', color: '#3f8a57' },
    { key: 'ridge', name: 'Ridgelines and pali', color: '#7d8f9c' },
    { key: 'plateau', name: 'Central plateau', color: '#c9b96e' },
    { key: 'leeward', name: 'Leeward dry side', color: '#d39a5c' },
    { key: 'wetland', name: 'Wetlands and stream mouths', color: '#4fa39a' },
    { key: 'vent', name: 'The vent', color: '#b4523c' },
    { key: 'coast', name: 'Coast and reef', color: '#e8d9a8' },
    { key: 'crater', name: 'Crater basin', color: '#8a6fb0' }
  ];
  const RG = {}; REGIONS.forEach((r, k) => { RG[r.key] = k; });

  // One rule for the island grid and for every 1 m tile, so the two always agree.
  // j is a jitter in about -1..1 that makes boundaries ragged.
  function classify(h, rain, temp, fog, wet, slope, lava, dc, beachy, crest, j) {
    if (lava > 0.62 + 0.08 * j) return B.lava;
    if (slope > 40 + 6 * j) return B.cliff;
    if (beachy && dc < 150 + 70 * j && h < 7) return B.strand;
    if (wet > 0.62 - 0.1 * j && h < 40 && slope < 3) return B.marsh;
    if (fog > 0.5 + 0.12 * j && rain > 2200) return B.cloud;
    if (crest > 0.55 + 0.15 * j && h > 550) return B.ridge;
    // aridity: rain against what warm air evaporates (less up high, where it is cooler)
    const ai = rain * (1 + 0.1 * j) / (1300 * (0.4 + 0.6 * temp / 24));
    if (ai > 2.7) return B.wet;
    if (ai > 1.6) return B.moist;
    if (ai > 1.0) return B.mesic;
    if (ai > 0.75) return slope < 14 ? B.grassland : B.dryscrub;
    return slope < 4 && ai > 0.62 ? B.grassland : B.dryscrub;
  }

  // ---------- facility programmes: what each kind of place is built from ----------
  // rooms: [name, width weight, furnishing]; furnishing is one of rack, console, furniture, bunks, tanks, or none.
  const bunkRooms = (n, prefix) => Array.from({ length: n }, (_, k) => [(prefix || 'Bunk room') + ' ' + (k + 1), 1, 'bunks']);
  const PROGRAMS = {
    genetics: {
      buildings: [
        { name: 'Main laboratory', w: 64, h: 30, rooms: [['Sequencing lab', 3, 'console'], ['Embryology lab', 3, 'furniture'], ['Clean room', 2, 'furniture'], ['Microscopy', 2, 'console'], ['Fertilisation lab', 2, 'furniture'], ['Decontamination', 1], ['Lab offices', 2, 'furniture'], ['Washrooms', 1]] },
        { name: 'Server farm', w: 44, h: 28, rooms: [['Server hall', 6, 'rack'], ['Cooling plant', 2, 'tanks'], ['UPS and batteries', 2, 'rack'], ['Network operations', 2, 'console']] },
        { name: 'Hatchery', w: 38, h: 24, rooms: [['Incubation hall', 4, 'rack'], ['Nursery pens', 3], ['Veterinary room', 1, 'furniture'], ['Feed store', 1, 'rack']] },
        { name: 'Deep freeze', w: 26, h: 20, rooms: [['Airlock', 1], ['Cryo vault', 4, 'rack'], ['Liquid nitrogen store', 2, 'tanks']] },
        { name: 'Dormitory', w: 46, h: 17, rooms: bunkRooms(8).concat([['Showers', 1], ['Washrooms', 1], ['Laundry', 1, 'rack'], ['Common room', 2, 'furniture']]) },
        { name: 'Mess hall', w: 28, h: 20, rooms: [['Dining room', 4, 'furniture'], ['Kitchen', 2, 'console'], ['Cold store', 1, 'rack'], ['Pantry', 1, 'rack']] },
        { name: 'Administration and security', w: 28, h: 16, rooms: [['Lobby', 2], ['Security office', 1, 'console'], ['Armoury', 1, 'rack'], ["Director's office", 1, 'furniture'], ['Records', 1, 'rack']] },
        { name: 'Generator house', w: 18, h: 12, rooms: [['Backup generators', 3, 'tanks'], ['Switch room', 1, 'console']] },
        { name: 'Vehicle garage', w: 28, h: 16, rooms: [['Vehicle bays', 4], ['Workshop', 1, 'furniture']] }
      ],
      yard: [['helipad'], ['tanks', 3, 'Fuel tanks'], ['tower', 'Water tower'], ['parking', 30, 16]]
    },
    control: {
      buildings: [
        { name: 'Operations centre', w: 50, h: 28, rooms: [['Control room', 5, 'console'], ['Data centre', 2, 'rack'], ['Briefing room', 2, 'furniture'], ['Radio room', 1, 'console'], ['Offices', 2, 'furniture'], ['Washrooms', 1], ['Canteen', 2, 'furniture']] },
        { name: 'Staff quarters', w: 34, h: 15, rooms: bunkRooms(5).concat([['Showers', 1], ['Washrooms', 1], ['Kitchen', 2, 'console']]) },
        { name: 'Generator house', w: 16, h: 12, rooms: [['Backup generators', 3, 'tanks'], ['Switch room', 1, 'console']] },
        { name: 'Motor pool', w: 26, h: 14, rooms: [['Vehicle bays', 4], ['Parts store', 1, 'rack']] }
      ],
      yard: [['tower', 'Radio mast'], ['helipad'], ['parking', 26, 14]]
    },
    docks: {
      buildings: [
        { name: 'Warehouse', w: 40, h: 24, rooms: [['Cargo hall', 5, 'rack'], ['Cold store', 1, 'rack'], ['Loading office', 1, 'furniture']] },
        { name: 'Harbour office', w: 20, h: 12, rooms: [["Harbourmaster's office", 1, 'console'], ['Radio room', 1, 'console'], ['Customs', 1, 'furniture'], ['Washrooms', 1]] },
        { name: 'Boathouse', w: 24, h: 14, rooms: [['Boat bays', 3], ['Workshop', 1, 'furniture']] }
      ],
      yard: [['tanks', 3, 'Fuel tanks'], ['tower', 'Dock crane'], ['parking', 20, 12]]
    },
    airstrip: {
      buildings: [
        { name: 'Hangar', w: 38, h: 30, rooms: [['Hangar floor', 6], ['Workshop', 1, 'furniture']] },
        { name: 'Terminal', w: 30, h: 16, rooms: [['Departure lounge', 3, 'furniture'], ['Check-in', 1, 'furniture'], ['Operations office', 1, 'console'], ['Cargo room', 2, 'rack'], ['Washrooms', 1]] },
        { name: 'Control tower', w: 9, h: 9, rooms: [['Tower cab', 1, 'console']] }
      ],
      yard: [['tanks', 2, 'Fuel tanks'], ['tower', 'Windsock'], ['parking', 18, 10]]
    },
    comms: {
      buildings: [
        { name: 'Equipment hut', w: 16, h: 11, rooms: [['Radio room', 2, 'console'], ['Battery room', 1, 'rack'], ['Bunk room', 1, 'bunks']] },
        { name: 'Generator shed', w: 9, h: 7, rooms: [['Generator', 1, 'tanks']] }
      ],
      yard: [['tower', 'Lattice mast'], ['dish', 'Satellite dish']]
    },
    nuclear: {
      buildings: [
        { name: 'Turbine hall', w: 64, h: 24, rooms: [['Turbine hall', 6, 'tanks'], ['Feedwater pumps', 1, 'tanks']] },
        { name: 'Control building', w: 32, h: 18, rooms: [['Main control room', 3, 'console'], ['Shift office', 1, 'furniture'], ['Health physics', 1, 'rack'], ['Kitchen', 1, 'console'], ['Washrooms', 1], ['Security', 1, 'console']] },
        { name: 'Spent-fuel store', w: 22, h: 14, rooms: [['Fuel pool', 2, 'tanks'], ['Cask store', 1, 'rack']] },
        { name: 'Pump house', w: 14, h: 10, rooms: [['Cooling-water pumps', 1, 'tanks']] },
        { name: 'Guard post', w: 8, h: 6, rooms: [['Gatehouse', 1, 'console']] }
      ],
      yard: [['dome', 'Reactor containment', 22], ['switchyard']]
    },
    geothermal: {
      buildings: [
        { name: 'Power house', w: 42, h: 20, rooms: [['Turbine hall', 4, 'tanks'], ['Control room', 1, 'console'], ['Workshop', 1, 'furniture']] },
        { name: 'Separator station', w: 14, h: 10, rooms: [['Steam separators', 1, 'tanks']] }
      ],
      yard: [['ring', 'Cooling tower', 9], ['ring', 'Cooling tower', 9], ['tanks', 4, 'Wellheads']]
    },
    weather: {
      buildings: [
        { name: 'Control bunker', w: 28, h: 16, rooms: [['Array control', 3, 'console'], ['Radar room', 1, 'console'], ['Generator room', 1, 'tanks'], ['Bunk room', 1, 'bunks'], ['Galley', 1, 'furniture'], ['Washroom', 1]] }
      ],
      yard: [['antennas', 'Antenna field'], ['dome', 'Radar dome', 8]]
    },
    orgone: {
      buildings: [
        { name: 'Accumulator hall', w: 34, h: 20, rooms: [['Accumulator chamber', 4, 'rack'], ['Observation room', 1, 'console']] },
        { name: 'Laboratory', w: 26, h: 16, rooms: [['Laboratory', 2, 'furniture'], ['Records', 1, 'rack'], ['Quarters', 1, 'bunks'], ['Washroom', 1]] }
      ],
      yard: [['towers', 'Cloudbuster array', 3]]
    },
    lab: {
      buildings: [
        { name: 'Laboratory', w: 38, h: 20, rooms: [['Wet lab', 2, 'furniture'], ['Genetics lab', 2, 'console'], ['Specimen store', 1, 'rack'], ['Animal holding', 2, 'rack'], ['Offices', 1, 'furniture'], ['Washrooms', 1]] },
        { name: 'Bunkhouse', w: 24, h: 13, rooms: bunkRooms(3).concat([['Kitchen', 1, 'console'], ['Washrooms', 1]]) },
        { name: 'Generator shed', w: 10, h: 8, rooms: [['Generator', 1, 'tanks']] }
      ],
      yard: [['tanks', 2, 'Water tanks']]
    },
    depot: {
      buildings: [
        { name: 'Warehouse A', w: 36, h: 22, rooms: [['Store', 5, 'rack'], ['Office', 1, 'furniture']] },
        { name: 'Warehouse B', w: 30, h: 20, rooms: [['Store', 5, 'rack']] },
        { name: 'Depot office', w: 14, h: 10, rooms: [['Dispatch', 1, 'console'], ['Washroom', 1]] }
      ],
      yard: [['tanks', 4, 'Fuel tanks'], ['parking', 30, 16]]
    },
    ley: {
      buildings: [{ name: 'Survey hut', w: 16, h: 10, rooms: [['Map room', 2, 'furniture'], ['Instrument room', 1, 'console'], ['Bunks', 1, 'bunks']] }],
      yard: [['tower', 'Instrument mast'], ['marker', 'Survey marker']]
    },
    watch: {
      buildings: [{ name: 'Bunk hut', w: 12, h: 8, rooms: [['Bunk room', 1, 'bunks'], ['Radio room', 1, 'console']] }, { name: 'Watchtower', w: 6, h: 6, rooms: [['Lookout', 1, 'console']] }],
      yard: [['tower', 'Searchlight']]
    },
    raptors: {
      buildings: [
        { name: 'Observation bunker', w: 22, h: 10, rooms: [['Observation room', 3, 'console'], ['Airlock', 1]] },
        { name: 'Keeper station', w: 22, h: 12, rooms: [["Keeper's office", 2, 'console'], ['Armoury', 1, 'rack'], ['Washroom', 1]] },
        { name: 'Holding block', w: 34, h: 10, rooms: [['Holding cell 1', 1], ['Holding cell 2', 1], ['Holding cell 3', 1], ['Holding cell 4', 1], ['Squeeze chute', 1]] }
      ],
      yard: [['pen', 'Raptor enclosure', 120, 90], ['tower', 'Feeding gantry']]
    }
  };

  // Shelf-pack a programme into a square compound centred on 0,0; the result gives its half-size.
  function yardSize(y) {
    switch (y[0]) {
      case 'helipad': return [24, 24]; case 'tanks': return [y[1] * 8, 8]; case 'tower': return [6, 6]; case 'parking': return [y[1], y[2]];
      case 'dish': return [14, 14]; case 'dome': return [y[2] * 2 + 4, y[2] * 2 + 4]; case 'ring': return [y[2] * 2 + 4, y[2] * 2 + 4];
      case 'switchyard': return [40, 28]; case 'antennas': return [72, 56]; case 'towers': return [y[2] * 10, 10]; case 'marker': return [4, 4];
      case 'pen': return [y[2], y[3]];
    }
    return [8, 8];
  }
  const layoutCache = {};
  function layoutProgram(kind) {
    if (layoutCache[kind]) return layoutCache[kind];
    const P = PROGRAMS[kind], items = [];
    for (const b of P.buildings) items.push({ type: 'bld', w: b.w, h: b.h, b });
    for (const y of P.yard || []) { const [w, h] = yardSize(y); items.push({ type: 'yard', w, h, spec: y }); }
    items.sort((a, b) => b.h - a.h || b.w - a.w);
    const gap = 6, total = items.reduce((a, it) => a + (it.w + gap) * (it.h + gap), 0);
    const rowW = Math.max(Math.max(...items.map(i => i.w)), Math.ceil(Math.sqrt(total * 1.3)));
    let x = 0, y = 0, rowH = 0, maxW = 0;
    for (const it of items) {
      if (x > 0 && x + it.w > rowW) { y += rowH + gap; x = 0; rowH = 0; }
      it.x = x; it.y = y; x += it.w + gap; rowH = Math.max(rowH, it.h); maxW = Math.max(maxW, x - gap);
    }
    const Hh = y + rowH;
    for (const it of items) { it.x = D.round(it.x - maxW / 2); it.y = D.round(it.y - Hh / 2); }
    return (layoutCache[kind] = { r: Math.ceil(Math.max(maxW, Hh) / 2 + 10), items });
  }

  // Turn a programme at an absolute position into drawing primitives with room labels.
  function buildCompound(t, kind, labels, prims) {
    const L = layoutProgram(kind), cx = D.round(t.x), cy = D.round(t.y);
    const lab = name => { labels.push(name); return labels.length - 1; };
    for (const it of L.items) {
      const x0 = cx + it.x, y0 = cy + it.y, x1 = x0 + it.w - 1, y1 = y0 + it.h - 1;
      if (it.type === 'bld') building(it.b, x0, y0, x1, y1, t.name, lab, prims);
      else yardFeature(it.spec, x0, y0, x1, y1, t.name, lab, prims);
    }
  }
  function building(b, x0, y0, x1, y1, site, lab, prims) {
    const w = x1 - x0 + 1, h = y1 - y0 + 1, horiz = w >= h;
    // work in (u along the long side, v across), then map back
    const U = horiz ? w : h, V = horiz ? h : w;
    const toXY = (u, v) => horiz ? [x0 + u, y0 + v] : [x0 + v, y0 + u];
    const rect = (u0, v0, u1, v1) => { const a = toXY(u0, v0), c = toXY(u1, v1); return [Math.min(a[0], c[0]), Math.min(a[1], c[1]), Math.max(a[0], c[0]), Math.max(a[1], c[1])]; };
    const B = { t: 'bld', x0, y0, x1, y1, rooms: [], doors: [], label: lab(site + ' · ' + b.name) };
    const rooms = b.rooms;
    if (rooms.length === 1 || V < 7) {
      const R = rooms[0]; B.rooms.push({ x0, y0, x1, y1, label: lab(site + ' · ' + b.name + ' · ' + R[0]) });
      B.doors.push(toXY(Math.floor(U / 2), V - 1));
      if (V >= 7 && rooms.length > 1) { /* unreachable */ }
      furnish(R[2], x0, y0, x1, y1, B.rooms[0].label, prims);
    } else {
      // a 2-wide corridor down the middle, rooms either side; doors onto the corridor; entrances at both ends
      const two = V >= 11, c0 = two ? Math.floor(V / 2) - 1 : V - 3, c1 = c0 + 1;
      const sides = two ? [[], []] : [[]];
      let wA = 0, wB = 0;
      for (const R of rooms) { if (!two || wA <= wB) { sides[0].push(R); wA += R[1]; } else { sides[1].push(R); wB += R[1]; } }
      sides.forEach((list, si) => {
        if (!list.length) return;
        const v0 = si === 0 ? 0 : c1 + 1, v1 = si === 0 ? c0 - 1 : V - 1, tot = list.reduce((a, R) => a + R[1], 0);
        let u = 0;
        list.forEach((R, k) => {
          const uEnd = k === list.length - 1 ? U - 1 : Math.max(u + 3, D.round(u + (U - 1) * R[1] / tot));
          const [rx0, ry0, rx1, ry1] = rect(u, v0, uEnd, v1);
          const label = lab(site + ' · ' + b.name + ' · ' + R[0]);
          B.rooms.push({ x0: rx0, y0: ry0, x1: rx1, y1: ry1, label });
          B.doors.push(toXY(Math.floor((u + uEnd) / 2), si === 0 ? v1 : v0));
          furnish(R[2], rx0, ry0, rx1, ry1, label, prims);
          u = uEnd;
        });
      });
      B.doors.push(toXY(0, c0), toXY(U - 1, c0));
    }
    prims.unshift(B); // buildings first so furniture draws over their floors
  }
  function furnish(kind, x0, y0, x1, y1, label, prims) {
    const ix0 = x0 + 2, iy0 = y0 + 2, ix1 = x1 - 2, iy1 = y1 - 2;
    if (!kind || ix1 < ix0 || iy1 < iy0) return;
    const horiz = (x1 - x0) >= (y1 - y0), add = (a, b, c, d, cover) => prims.push({ t: 'fur', x0: a, y0: b, x1: c, y1: d, cover, label });
    if (kind === 'rack') { // rows with aisles between
      if (horiz) for (let y = iy0; y <= iy1; y += 2) add(ix0, y, ix1, y, 'rack'); else for (let x = ix0; x <= ix1; x += 2) add(x, iy0, x, iy1, 'rack');
    } else if (kind === 'console') { // a bank of consoles along the far wall, desks in the middle
      if (horiz) { add(ix0, iy0, ix1, iy0, 'console'); if (iy1 - iy0 >= 3) add(ix0 + 1, Math.floor((iy0 + iy1) / 2) + 1, ix1 - 1, Math.floor((iy0 + iy1) / 2) + 1, 'furniture'); }
      else { add(ix0, iy0, ix0, iy1, 'console'); if (ix1 - ix0 >= 3) add(Math.floor((ix0 + ix1) / 2) + 1, iy0 + 1, Math.floor((ix0 + ix1) / 2) + 1, iy1 - 1, 'furniture'); }
    } else if (kind === 'furniture') { // tables on a grid
      for (let y = iy0; y + 1 <= iy1; y += 3) for (let x = ix0; x + 1 <= ix1; x += 4) add(x, y, x + 1, y, 'furniture');
    } else if (kind === 'bunks') { // bunks along both long walls
      if (horiz) for (let x = ix0 - 1; x <= ix1 + 1; x += 2) { add(x, iy0 - 1, x, iy0 - 1, 'furniture'); if (iy1 > iy0) add(x, iy1 + 1, x, iy1 + 1, 'furniture'); }
      else for (let y = iy0 - 1; y <= iy1 + 1; y += 2) { add(ix0 - 1, y, ix0 - 1, y, 'furniture'); if (ix1 > ix0) add(ix1 + 1, y, ix1 + 1, y, 'furniture'); }
    } else if (kind === 'lift') { // the elevator car fills the room
      add(x0 + 1, y0 + 1, x1 - 1, y1 - 1, 'elevator');
    } else if (kind === 'tanks') { // tanks and machinery blocks
      for (let y = iy0; y + 1 <= iy1; y += 4) for (let x = ix0; x + 1 <= ix1; x += 4) add(x, y, x + 1, y + 1, 'tank');
    }
  }
  function yardFeature(y, x0, y0, x1, y1, site, lab, prims) {
    const cx = (x0 + x1 + 1) / 2, cy = (y0 + y1 + 1) / 2;
    switch (y[0]) {
      case 'helipad': prims.push({ t: 'circle', x: cx, y: cy, r: 10, cover: 'helipad', label: lab(site + ' · Helipad') }); break;
      case 'tanks': { const label = lab(site + ' · ' + y[2]); for (let k = 0; k < y[1]; k++) prims.push({ t: 'circle', x: x0 + 4 + k * 8, y: cy, r: 3, cover: 'tank', label }); break; }
      case 'tower': prims.push({ t: 'tower', x: cx, y: cy, label: lab(site + ' · ' + y[1]) }); break;
      case 'parking': prims.push({ t: 'rect', x0, y0, x1, y1, cover: 'road', label: lab(site + ' · Car park') }); break;
      case 'dish': prims.push({ t: 'circle', x: cx, y: cy, r: 6, cover: 'console', ring: true, fill: 'floor', label: lab(site + ' · ' + y[1]) }); break;
      case 'dome': prims.push({ t: 'circle', x: cx, y: cy, r: y[2], cover: 'wall', ring: true, fill: 'floor', door: true, label: lab(site + ' · ' + y[1]) }); break;
      case 'ring': prims.push({ t: 'circle', x: cx, y: cy, r: y[2], cover: 'wall', ring: true, fill: 'floor', label: lab(site + ' · ' + y[1]) }); break;
      case 'switchyard': { const label = lab(site + ' · Switchyard'); prims.push({ t: 'rect', x0, y0, x1, y1, cover: 'yard', label, fence: true }); for (let a = 0; a < 3; a++) for (let b = 0; b < 2; b++) prims.push({ t: 'tower', x: x0 + 8 + a * 12, y: y0 + 8 + b * 12, label }); break; }
      case 'antennas': { const label = lab(site + ' · ' + y[1]); for (let a = 0; a < 5; a++) for (let b = 0; b < 4; b++) prims.push({ t: 'tower', x: x0 + 6 + a * 15, y: y0 + 6 + b * 14, label }); break; }
      case 'towers': { const label = lab(site + ' · ' + y[1]); for (let k = 0; k < y[2]; k++) prims.push({ t: 'tower', x: x0 + 5 + k * 10, y: cy, label }); break; }
      case 'marker': prims.push({ t: 'circle', x: cx, y: cy, r: 1.6, cover: 'ruin', label: lab(site + ' · ' + y[1]) }); break;
      case 'pen': prims.push({ t: 'pen', x0, y0, x1, y1, label: lab(site + ' · ' + y[1]) }); break;
    }
  }

  // Trace the outline of a cell mask into a closed polygon (largest loop), in metres.
  function traceMask(mask, nx, ny, cell) {
    const next = new Map(), key = (x, y) => x * 100003 + y;
    const edge = (ax, ay, bx, by) => next.set(key(ax, ay), [bx, by, ax, ay]);
    for (let y = 0; y < ny; y++) for (let x = 0; x < nx; x++) {
      if (!mask[y * nx + x]) continue;
      const out = (xx, yy) => xx < 0 || yy < 0 || xx >= nx || yy >= ny || !mask[yy * nx + xx];
      // inside on the left as we walk each edge
      if (out(x, y - 1)) edge(x + 1, y, x, y);
      if (out(x, y + 1)) edge(x, y + 1, x + 1, y + 1);
      if (out(x - 1, y)) edge(x, y, x, y + 1);
      if (out(x + 1, y)) edge(x + 1, y + 1, x + 1, y);
    }
    let best = null;
    const used = new Set();
    for (const [k0, e0] of next) {
      if (used.has(k0)) continue;
      const loop = []; let k = k0, e = e0, guard = 0;
      while (e && !used.has(k) && guard++ < 200000) { used.add(k); loop.push([e[2], e[3]]); k = key(e[0], e[1]); e = next.get(k); }
      if (!best || loop.length > best.length) best = loop;
    }
    if (!best) return [];
    // drop collinear points, smooth, scale to metres
    const pts = best.filter((q, i) => { const a = best[(i + best.length - 1) % best.length], b = best[(i + 1) % best.length]; return (q[0] - a[0]) * (b[1] - q[1]) !== (q[1] - a[1]) * (b[0] - q[0]); });
    let ring = pts.map(q => [q[0] * cell, q[1] * cell]);
    for (let it = 0; it < 3; it++) { const out = []; for (let i = 0; i < ring.length; i++) { const a = ring[i], b = ring[(i + 1) % ring.length]; out.push([a[0] * 0.75 + b[0] * 0.25, a[1] * 0.75 + b[1] * 0.25], [a[0] * 0.25 + b[0] * 0.75, a[1] * 0.25 + b[1] * 0.75]); } ring = out; }
    return ring.flat();
  }

  // Paddock species: size in km² at full island scale, what ground they want, fence and extras.
  const PADDOCKS = [
    { key: 'apato', name: 'Apatosaurus paddock', occupant: 'Apatosaurus herd', km2: 9, biomes: { grassland: 1, mesic: 0.9, moist: 0.5, marsh: 0.6, lake: 0.7 }, water: true, fence: 'low', wallow: false },
    { key: 'rex', name: 'Tyrannosaur paddock', occupant: 'Tyrannosaurus rex', km2: 2.2, biomes: { mesic: 1, moist: 0.9, grassland: 0.8, wet: 0.6 }, fence: 'heavy', moat: true },
    { key: 'trike', name: 'Triceratops paddock', occupant: 'Triceratops herd', km2: 1.4, biomes: { grassland: 1, dryscrub: 0.8, mesic: 0.7 }, fence: 'standard', wallow: true },
    { key: 'stego', name: 'Stegosaurus paddock', occupant: 'Stegosaurus herd', km2: 1.4, biomes: { mesic: 1, moist: 0.8, grassland: 0.6 }, fence: 'standard', wallow: false }
  ];

  // Wildlife: where each kind lives, as habitat weights by biome plus a few modifiers.
  const FAUNA = [
    { key: 'compy', name: 'Compsognathus', note: 'free-roaming everywhere; thickest near people, ruins and forest edges', perKm2: 40, biomes: { strand: 0.4, marsh: 0.5, dryscrub: 0.6, grassland: 0.7, mesic: 1, moist: 1, wet: 0.8, cloud: 0.5, ridge: 0.4, cliff: 0.1, lava: 0.2 }, people: 0.8 },
    { key: 'pig', name: 'Feral pigs', note: 'forest and woodland near water', perKm2: 12, biomes: { mesic: 1, moist: 1, wet: 0.8, cloud: 0.5, marsh: 0.6, grassland: 0.3 }, water: 0.5 },
    { key: 'goat', name: 'Feral goats', note: 'dry slopes, crests and cliffs', perKm2: 10, biomes: { dryscrub: 1, ridge: 0.9, cliff: 0.8, grassland: 0.5, lava: 0.4 }, steep: 0.6 },
    { key: 'fowl', name: 'Jungle fowl', note: 'forest edges everywhere', perKm2: 60, biomes: { mesic: 1, moist: 0.8, wet: 0.6, grassland: 0.5, dryscrub: 0.4, strand: 0.3 } },
    { key: 'bat', name: 'Fruit bats', note: 'roost in caves and tall forest, forage at night', perKm2: 30, biomes: { moist: 1, wet: 1, cloud: 0.6, mesic: 0.5 }, caves: 1 },
    { key: 'monitor', name: 'Monitor lizards', note: 'escaped; lowland forest and wetlands', perKm2: 3, biomes: { marsh: 1, moist: 0.8, mesic: 0.6, strand: 0.4 }, low: true },
    { key: 'toad', name: 'Cane toads', note: 'wetlands and streams', perKm2: 200, biomes: { marsh: 1, moist: 0.4, mesic: 0.3 }, water: 1 },
    { key: 'mongoose', name: 'Mongoose', note: 'dry lowland, around rubbish', perKm2: 8, biomes: { dryscrub: 1, grassland: 0.8, strand: 0.6, mesic: 0.5 }, low: true, people: 0.5 },
    { key: 'macaque', name: 'Macaques', note: 'escaped from the labs; forest near facilities', perKm2: 6, biomes: { moist: 1, wet: 0.9, mesic: 0.7 }, people: 1.5, peopleOnly: true },
    { key: 'turtle', name: 'Sea turtles', note: 'nest on beaches', perKm2: 20, biomes: { strand: 1 } },
    { key: 'seal', name: 'Monk seals', note: 'haul out on beaches and rocky shores', perKm2: 4, biomes: { strand: 0.8 }, rocky: 1 },
    { key: 'seabird', name: 'Seabird colonies', note: 'sea cliffs, islets and crests', perKm2: 150, biomes: { cliff: 1, ridge: 0.3 }, cliffs: 1 }
  ];

  // ---------- stage 4 tables ----------
  const CORPS = [
    { name: 'Joint Operations Authority', goal: 'shared' },
    { name: 'Halcyon Genomics', goal: 'experimental' },
    { name: 'Ostrander Power & Light', goal: 'extractive' },
    { name: 'Vantage Survey Group', goal: 'expansionist' },
    { name: 'Kestrel Maritime', goal: 'isolationist' },
    { name: 'Meridian Atmospherics', goal: 'preservationist' }
  ];
  // Placed in this order: objective anchors (docks, airstrip, comms, control) first.
  const FAC = [
    { key: 'docks', name: 'Docks', icon: 'D', r: 60, count: 2, corp: 4, apart: 12000, major: true },
    { key: 'airstrip', name: 'Airstrip', icon: 'A', r: 70, count: 1, corp: 3, major: true },
    { key: 'comms', name: 'Comms relay', icon: 'C', r: 30, count: 1, corp: 3 },
    { key: 'control', name: 'Control centre', icon: 'H', r: 90, count: 1, corp: 0, major: true },
    { key: 'genetics', name: 'Genetics complex', icon: 'X', r: 130, count: 1, corp: 1, major: true },
    { key: 'raptors', name: 'Velociraptor pen', icon: 'V', r: 90, count: 1, corp: 1 },
    { key: 'nuclear', name: 'Nuclear plant', icon: 'N', r: 120, count: 1, corp: 2, major: true },
    { key: 'geothermal', name: 'Geothermal plant', icon: 'G', r: 70, count: 1, corp: 2, major: true },
    { key: 'weather', name: 'Weather control array', icon: 'W', r: 60, count: 1, corp: 5 },
    { key: 'orgone', name: 'Orgone research station', icon: 'O', r: 50, count: 1, corp: 5 },
    { key: 'lab', name: 'Research lab', icon: 'R', r: 55, count: 2, corp: 1, apart: 6000, major: true },
    { key: 'depot', name: 'Supply depot', icon: 'S', r: 45, count: 2, corp: 4, apart: 6000, major: true },
    { key: 'ley', name: 'Ley-line survey station', icon: 'L', r: 30, count: 3, corp: 3 },
    { key: 'watch', name: 'Cordon watch post', icon: 'P', r: 18, count: 3, corp: 4, apart: 10000 }
  ];
  const WRECK_ERAS = [
    ['three-masted barque', '1790s', 40], ['whaling ship', '1840s', 34], ['steam freighter', '1890s', 70], ['Pacific schooner', '1920s', 30],
    ['navy landing craft', '1940s', 24], ['fishing trawler', '1970s', 26], ['research yacht', '2000s', 18], ['corporate supply barge', 'year 4', 45]
  ];
  const RUIN_KINDS = [
    { key: 'terraces', name: 'Stone-walled terraces', era: 'ancient', r: 30 },
    { key: 'stones', name: 'Ring of standing stones', era: 'ancient', r: 12 },
    { key: 'platform', name: 'Stone platform', era: 'ancient', r: 16 },
    { key: 'chapel', name: 'Mission chapel', era: '1800s', r: 10 },
    { key: 'mill', name: 'Sugar mill', era: '1800s', r: 18 },
    { key: 'lighthouse', name: 'Lighthouse', era: '1890s', r: 8 },
    { key: 'bunker', name: 'Coastal gun bunker', era: '1940s', r: 10 },
    { key: 'radar', name: 'Radar station', era: '1950s', r: 14 },
    { key: 'camp', name: 'Abandoned survey camp', era: 'year 2', r: 14 },
    { key: 'fieldlab', name: 'Burned field lab', era: 'year 5', r: 12 }
  ];
  const DIRS8 = ['east', 'south-east', 'south', 'south-west', 'west', 'north-west', 'north', 'north-east'];

  function works(C) {
    const { p, A, nx, ny, cell, n, area, H, H0, land, lake, rain, slopeD, biome, region, distLand, coast, coastNX, coastNY, coastEx, sea, Q, isRiver, lava, oldW, crest, say, lap } = C;
    const s = A.s, sq = Math.sqrt(s);
    const cxOf = i => (i % nx + 0.5) * cell, cyOf = i => (Math.floor(i / nx) + 0.5) * cell;
    const idx = (x, y) => clamp(Math.floor(y / cell), 0, ny - 1) * nx + clamp(Math.floor(x / cell), 0, nx - 1);
    const dry = i => land[i] && isNaN(lake[i]);
    const r = makeRng(p.seed, 'worldgen.works');
    // facilities go on the main island only (islets are for wrecks, stacks and later set pieces)
    const lblM = new Int32Array(n), lszM = components(land, nx, ny, lblM);
    let mainL = 0; lszM.forEach((v, k) => { if (v > lszM[mainL]) mainL = k; });
    const onMain = i => lblM[i] === mainL && dry(i);
    // island centre (for compass names) and a seaward-openness map for headlands
    let mx = 0, my = 0, mc = 0;
    for (let i = 0; i < n; i++) if (land[i]) { mx += cxOf(i); my += cyOf(i); mc++; }
    mx /= mc; my /= mc;
    const seaFrac = new Float64Array(n); for (let i = 0; i < n; i++) seaFrac[i] = land[i] ? 0 : 1;
    boxBlur(seaFrac, nx, ny, Math.max(1, D.round(1500 / cell)), 1);
    function compass(x, y) { const a = D.atan2(y - my, x - mx); return DIRS8[((D.round(a / (Math.PI / 4)) % 8) + 8) % 8]; }

    // ---------- facilities ----------
    say('Placing facilities');
    const sites = [];
    const tooClose = (x, y, rad, kind, apart) => sites.some(t =>
      D.hyp(t.x - x, t.y - y) < (kind === 'raptors' || t.kind === 'raptors' ? rad + t.r + 150 : Math.max(2500 * s, rad + t.r + 600)) || (kind === t.kind && apart && D.hyp(t.x - x, t.y - y) < apart * s));
    const nearRiver = new Uint8Array(n); // within one cell of a river
    for (let i = 0; i < n; i++) if (isRiver[i]) { const x = i % nx, y = (i - x) / nx; for (let k = 0; k < 8; k++) { const xx = x + DX[k], yy = y + DY[k]; if (xx >= 0 && yy >= 0 && xx < nx && yy < ny) nearRiver[yy * nx + xx] = 1; } }
    const SC = {
      docks: i => (coast[i] && coast[i] !== 3 && slopeD[i] < 6) ? 2 - coastEx[i] * 1.5 + (nearRiver[i] ? 0.4 : 0) + (region[i] === RG.leeward ? 0.3 : 0) : -1e9,
      comms: i => slopeD[i] < 30 && biome[i] !== B.cliff ? H[i] / 300 + crest[i] : -1e9,
      control: i => slopeD[i] < 5 && region[i] !== RG.coast ? 3 - D.hyp(cxOf(i) - mx, cyOf(i) - my) / (6000 * s) - slopeD[i] / 6 + (region[i] === RG.plateau ? 1 : 0) : -1e9,
      genetics: i => slopeD[i] < 8 && H[i] > 30 && H[i] < 450 && rain[i] > 1400 && region[i] !== RG.coast ? 2 + Math.min(1, rain[i] / 4000) - slopeD[i] / 10 + (region[i] === RG.windward ? 0.6 : 0) : -1e9,
      nuclear: i => (distLand[i] < 700 && slopeD[i] < 4 && H[i] < 40 && D.hyp(cxOf(i) - A.vent.x, cyOf(i) - A.vent.y) > 6000 * s) ? 2 - coastEx[i] - slopeD[i] / 4 + (region[i] === RG.leeward ? 0.5 : 0) : -1e9,
      geothermal: i => { const d = D.hyp(cxOf(i) - A.vent.x, cyOf(i) - A.vent.y) / A.vent.R; return d > 1.1 && d < 3 && slopeD[i] < 10 ? 3 - Math.abs(d - 1.6) - slopeD[i] / 10 : -1e9; },
      weather: i => slopeD[i] < 20 && H[i] > 0.55 * p.peak && region[i] === RG.ridge ? H[i] / 400 + crest[i] : -1e9,
      orgone: i => { const d = D.hyp(cxOf(i) - A.caldera.x, cyOf(i) - A.caldera.y) / A.caldera.R; return d < 1.3 && slopeD[i] < 14 ? 3 - Math.abs(d - 0.7) : -1e9; },
      lab: i => slopeD[i] < 8 && H[i] < 300 && region[i] !== RG.coast ? 2 - slopeD[i] / 8 + (region[i] === RG.leeward ? 0.7 : 0) : -1e9,
      depot: i => slopeD[i] < 6 && H[i] < 120 && distLand[i] < 3000 ? 2 - slopeD[i] / 6 + (region[i] === RG.leeward ? 0.4 : 0) : -1e9,
      raptors: i => { const g0 = sites.find(t => t.kind === 'genetics'); if (!g0) return -1e9; const d = D.hyp(cxOf(i) - g0.x, cyOf(i) - g0.y); return d > g0.r + 250 && d < 1600 && slopeD[i] < 10 ? 3 - d / 1600 - slopeD[i] / 10 : -1e9; },
      watch: i => land[i] && distLand[i] < 500 && H[i] > 20 && slopeD[i] < 25 ? seaFrac[i] * 3 + Math.min(1, H[i] / 150) : -1e9
    };
    function placeBest(F, k) {
      const sc = SC[F.key], js = stream(p.seed, 'sites.' + F.key + '.' + k);
      let best = -1, bs = -1e9;
      for (let i = 0; i < n; i++) {
        if (!onMain(i)) continue;
        let v = sc(i); if (v < -1e8) continue;
        v += 0.6 * hash2(js, i, 7) / 4294967296;
        if (v > bs && !tooClose(cxOf(i), cyOf(i), layoutProgram(F.key).r, F.key, F.apart)) { bs = v; best = i; }
      }
      return best;
    }
    function addSite(F, x, y, extra) {
      const i = idx(x, y);
      const t = Object.assign({ id: sites.length, kind: F.key, icon: F.icon, kindName: F.name, x: D.round(x), y: D.round(y), r: layoutProgram(F.key).r,
        h: Math.max(2, H[i]), region: region[i], owner: CORPS[F.corp].name, ownerGoal: CORPS[F.corp].goal, major: !!F.major }, extra || {});
      sites.push(t); return t;
    }
    // airstrip: the flattest straight 1.4 km on low ground
    function placeAirstrip(F) {
      const L = 1400 * Math.min(1, 0.45 + s * 0.55), half = L / 2;
      let best = null, bs = -1e9; const js = stream(p.seed, 'sites.airstrip');
      for (let i = 0; i < n; i += 2) {
        if (!onMain(i) || slopeD[i] > 3 || H[i] > 400 || biome[i] === B.marsh) continue;
        const x = cxOf(i), y = cyOf(i);
        if (tooClose(x, y, half, F.key)) continue;
        for (let a = 0; a < 8; a++) {
          const ang = a * Math.PI / 8, dx = D.cos(ang), dy = D.sin(ang);
          let dev = 0, ok = true;
          for (let t = -half; t <= half; t += cell) {
            const j = idx(x + dx * t, y + dy * t);
            if (!dry(j) || slopeD[j] > 5) { ok = false; break; }
            dev = Math.max(dev, Math.abs(H[j] - H[i]));
          }
          if (!ok) continue;
          const v = 3 - dev / 10 + (region[i] === RG.plateau ? 0.6 : 0) + 0.5 * hash2(js, i, a) / 4294967296;
          if (v > bs) { bs = v; best = { x, y, dx, dy, half }; }
        }
      }
      if (!best) return;
      // the terminal compound sits beside the runway, not on it
      const off = 22.5 + layoutProgram('airstrip').r + 15;
      const t = addSite(F, best.x - best.dy * off, best.y + best.dx * off);
      const i1 = idx(best.x - best.dx * half, best.y - best.dy * half), i2 = idx(best.x + best.dx * half, best.y + best.dy * half);
      t.runway = { x1: best.x - best.dx * half, y1: best.y - best.dy * half, x2: best.x + best.dx * half, y2: best.y + best.dy * half, h: (H[i1] + H[i2] + t.h) / 3 };
      t.h = t.runway.h;
    }
    // ley lines: straight lines that the herds follow; survey stations sit where they cross
    const leylines = [];
    const landCells = []; for (let i = 0; i < n; i += 3) if (land[i]) landCells.push(i);
    const big = A.W + A.H;
    const lineThrough = (ax, ay, bx, by) => { const d = D.hyp(bx - ax, by - ay) || 1, dx = (bx - ax) / d, dy = (by - ay) / d; leylines.push({ x1: ax - dx * big, y1: ay - dy * big, x2: ax + dx * big, y2: ay + dy * big }); };
    for (const F of FAC) {
      if (F.key === 'airstrip') { placeAirstrip(F); continue; }
      if (F.key === 'ley') {
        // pick the three crossings first, on the main island and clear of other sites, then draw the lines
        // through them as a triangle (so every crossing is on land), plus a fourth line for the herds
        const rl = makeRng(p.seed, 'worldgen.ley'), pts = [];
        for (let tries = 0; tries < 4000 && pts.length < F.count; tries++) {
          const i = landCells[Math.floor(rl() * landCells.length)]; if (!onMain(i) || slopeD[i] >= 18) continue;
          const x = cxOf(i), y = cyOf(i);
          if (tooClose(x, y, layoutProgram('ley').r, F.key) || pts.some(q => D.hyp(q[0] - x, q[1] - y) < 6000 * s)) continue;
          if (pts.length === 2) { // not too flat a triangle
            const [a, b] = pts, ab = D.hyp(b[0] - a[0], b[1] - a[1]), area2 = Math.abs((b[0] - a[0]) * (y - a[1]) - (b[1] - a[1]) * (x - a[0]));
            if (area2 / (ab * ab) < 0.25) continue;
          }
          pts.push([x, y]);
        }
        for (let k = 0; k < pts.length; k++) { const a = pts[k], b = pts[(k + 1) % pts.length]; if (pts.length > 1 && (pts.length > 2 || k === 0)) lineThrough(a[0], a[1], b[0], b[1]); }
        const i4 = landCells[Math.floor(rl() * landCells.length)], ang = rl() * Math.PI;
        lineThrough(cxOf(i4), cyOf(i4), cxOf(i4) + D.cos(ang), cyOf(i4) + D.sin(ang));
        for (const [x, y] of pts) addSite(F, x, y);
        continue;
      }
      for (let k = 0; k < F.count; k++) { const i = placeBest(F, k); if (i < 0) break; addSite(F, cxOf(i), cyOf(i)); }
    }
    // names: add a compass word where a kind repeats
    const cnt = {}; sites.forEach(t => cnt[t.kind] = (cnt[t.kind] || 0) + 1);
    sites.forEach(t => {
      t.name = cnt[t.kind] > 1 ? t.kindName + ', ' + compass(t.x, t.y) : t.kindName;
      const same = sites.filter(u => u !== t && u.name === t.name && u.id < t.id).length;
      if (same) t.name += ' ' + (same + 1);
      if (t.kind === 'docks') { const i = idx(t.x, t.y); t.dir = [coastNX[i], coastNY[i]]; }
    });

    // ---------- species paddocks, grown over suitable ground to each species' size ----------
    say('Fencing paddocks');
    const paddocks = [], padCell = new Int16Array(n).fill(-1);
    const gen = sites.find(t => t.kind === 'genetics');
    if (gen && p.paddockSize > 0) {
      const blocked = new Uint8Array(n);
      for (const t of sites) {
        const rr = Math.ceil((t.r + 200) / cell), x0 = Math.floor(t.x / cell), y0 = Math.floor(t.y / cell);
        for (let yy = Math.max(0, y0 - rr); yy <= Math.min(ny - 1, y0 + rr); yy++) for (let xx = Math.max(0, x0 - rr); xx <= Math.min(nx - 1, x0 + rr); xx++) blocked[yy * nx + xx] = 1;
      }
      const ph = new Heap(n * 4), js = stream(p.seed, 'paddock.seed');
      for (const S of PADDOCKS) {
        const target = Math.max(4, S.km2 * p.paddockSize * s * 1e6 / area);
        const suit = new Float64Array(n);
        for (let i = 0; i < n; i++) {
          if (!land[i] || blocked[i] || region[i] === RG.coast || region[i] === RG.vent || region[i] === RG.crater) continue;
          if (!isNaN(lake[i])) { suit[i] = S.biomes.lake || 0; continue; }
          if (slopeD[i] > 28) continue;
          let v = S.biomes[BIOMES[biome[i]].key] || 0;
          if (S.water && (isRiver[i] || nearRiver[i])) v += 0.4;
          suit[i] = v;
        }
        const blur = Float64Array.from(suit); boxBlur(blur, nx, ny, Math.max(1, D.round(Math.sqrt(target) / 2)), 1);
        let seed = -1, bs = -1e9;
        for (let i = 0; i < n; i++) if (suit[i] > 0.5) {
          const v = blur[i] - 0.05 * D.hyp(cxOf(i) - gen.x, cyOf(i) - gen.y) / (1000 * sq) + 0.08 * hash2(js, i, S.key.length) / 4294967296;
          if (v > bs) { bs = v; seed = i; }
        }
        if (seed < 0) continue;
        // best-first growth: good ground first, staying compact around the seed
        const inP = new Uint8Array(n), sx0 = seed % nx, sy0 = Math.floor(seed / nx), rad = Math.sqrt(target / Math.PI);
        ph.n = 0; ph.push(0, seed); let count = 0;
        while (ph.n > 0 && count < target) {
          const c = ph.pop(); if (inP[c]) continue; inP[c] = 1; count++;
          const x = c % nx, y = (c - x) / nx;
          for (let k = 0; k < 8; k += 2) {
            const xx = x + DX[k], yy = y + DY[k]; if (xx < 0 || yy < 0 || xx >= nx || yy >= ny) continue;
            const j = yy * nx + xx; if (inP[j] || suit[j] < 0.25) continue;
            ph.push((1 - suit[j]) + 0.9 * D.hyp(xx - sx0, yy - sy0) / rad, j);
          }
        }
        if (count < target * 0.4) continue;
        const id = paddocks.length;
        for (let i = 0; i < n; i++) if (inP[i]) {
          padCell[i] = id;
          const x = i % nx, y = (i - x) / nx;
          for (let k = 0; k < 8; k++) { const xx = x + DX[k], yy = y + DY[k]; if (xx >= 0 && yy >= 0 && xx < nx && yy < ny) blocked[yy * nx + xx] = 1; }
        }
        const poly = traceMask(inP, nx, ny, cell);
        let pcx = 0, pcy = 0; for (let v = 0; v < poly.length; v += 2) { pcx += poly[v]; pcy += poly[v + 1]; } pcx /= poly.length / 2; pcy /= poly.length / 2;
        let bv = 0, bd = 1e30;
        for (let v = 0; v < poly.length; v += 2) { const d = D.hyp(poly[v] - gen.x, poly[v + 1] - gen.y); if (d < bd) { bd = d; bv = v; } }
        const gate = [poly[bv], poly[bv + 1]], gl = D.hyp(gate[0] - pcx, gate[1] - pcy) || 1, out = [(gate[0] - pcx) / gl, (gate[1] - pcy) / gl];
        // a point well inside, for the wallow
        let inner = seed, ib = -1;
        for (let i = 0; i < n; i++) if (inP[i] && isNaN(lake[i])) { let e = 1e9; const x = i % nx, y = (i - x) / nx; for (let k = 0; k < 8; k++) { const xx = x + DX[k] * 2, yy = y + DY[k] * 2; if (xx < 0 || yy < 0 || xx >= nx || yy >= ny || !inP[yy * nx + xx]) e = 0; } if (e > ib) { ib = e; inner = i; if (e > 0) break; } }
        const bk = BIOMES[biome[seed]].key;
        paddocks.push({ id, key: S.key, name: S.name, occupant: S.occupant, x: pcx, y: pcy, poly, km2: count * area / 1e6, biome: bk, fence: S.fence,
          gate, out, moat: !!S.moat, wallow: S.wallow ? [cxOf(inner), cyOf(inner)] : null, hasLake: Array.from(inP).some((v, i) => v && !isNaN(lake[i])) });
      }
    }
    lap('facilities and paddocks');

    // ---------- roads ----------
    say('Routing roads');
    const roadCell = new Uint8Array(n), roads = [];
    const heap = new Heap(n * 3), g = new Float64Array(n), seen = new Int32Array(n), done = new Int32Array(n), par = new Int32Array(n);
    let stamp = 0;
    function stepCost(a, b, k) {
      const d = DL[k] * cell, gr = Math.abs(H[b] - H[a]) / d;
      let c = gr > 0.22 ? d * 30 : d * (1 + (gr / 0.07) * (gr / 0.07));
      if (isRiver[b]) c += 250 + 40 * Math.min(10, Q[b]);
      const bi = biome[b];
      if (bi === B.marsh) c += d; else if (bi === B.wet || bi === B.moist || bi === B.cloud) c += d * 0.3; else if (bi === B.cliff) c += d * 3;
      if (padCell[b] >= 0) c += d * 25;
      return roadCell[b] ? c * 0.35 : c;
    }
    function route(src, dst, costFn) {
      const cost = costFn || stepCost;
      stamp++; heap.n = 0;
      const tx = dst % nx, ty = Math.floor(dst / nx);
      const hcost = j => D.hyp(j % nx - tx, Math.floor(j / nx) - ty) * cell * 0.35;
      g[src] = 0; seen[src] = stamp; par[src] = -1; heap.push(hcost(src), src);
      while (heap.n > 0) {
        const c = heap.pop(); if (c === dst) break;
        if (done[c] === stamp) continue; done[c] = stamp;
        const x = c % nx, y = (c - x) / nx;
        for (let k = 0; k < 8; k++) {
          const xx = x + DX[k], yy = y + DY[k]; if (xx < 0 || yy < 0 || xx >= nx || yy >= ny) continue;
          const j = yy * nx + xx; if (!dry(j) || done[j] === stamp) continue;
          const ng = g[c] + cost(c, j, k);
          if (seen[j] !== stamp || ng < g[j]) { seen[j] = stamp; g[j] = ng; par[j] = c; if (heap.n < n * 3 - 1) heap.push(ng + hcost(j), j); }
        }
      }
      if (seen[dst] !== stamp) return null;
      const path = []; for (let c = dst; c >= 0; c = par[c]) path.push(c);
      return path.reverse();
    }
    function addRoad(path, cls) {
      // keep only the runs that are new, with one shared cell at each end so they join the network
      let run = [];
      const flush = () => {
        if (run.length >= 2) {
          let pts = run.map((i, k) => {
            const prev = run[Math.max(0, k - 1)], d = D.hyp(i % nx - prev % nx, Math.floor(i / nx) - Math.floor(prev / nx)) * cell || cell;
            const tunnel = k > 0 && Math.abs(H[i] - H[prev]) / d > 0.22 ? 1 : 0;
            return [cxOf(i), cyOf(i), H[i], (isRiver[i] ? 1 : 0) | (tunnel ? 2 : 0)];
          });
          // road surface: smoothed along the road, then tunnels bored straight between portals
          const hs = pts.map((q, k) => { let a = 0, c = 0; for (let m = Math.max(0, k - 2); m <= Math.min(pts.length - 1, k + 2); m++) { a += pts[m][2]; c++; } return a / c; });
          pts.forEach((q, k) => { q[2] = Math.max(1, hs[k]); });
          let k = 0;
          while (k < pts.length) {
            if (!(pts[k][3] & 2)) { k++; continue; }
            let e = k; while (e < pts.length && pts[e][3] & 2) e++;
            const a = Math.max(0, k - 1), b = Math.min(pts.length - 1, e);
            for (let m = a; m <= b; m++) { pts[m][2] = pts[a][2] + (pts[b][2] - pts[a][2]) * (m - a) / Math.max(1, b - a); if (m > a && m < b) pts[m][3] |= 2; }
            k = e;
          }
          const fl = pts.map(q => q[3]);
          const sm = chaikin(pts.map(q => [q[0], q[1], q[2], 0]), 2);
          const L = sm.length, xs = new Float32Array(L), ys = new Float32Array(L), hs2 = new Float32Array(L), flags = new Uint8Array(L);
          for (let m = 0; m < L; m++) { xs[m] = sm[m][0]; ys[m] = sm[m][1]; hs2[m] = sm[m][2]; flags[m] = fl[Math.min(fl.length - 1, D.round(m * (fl.length - 1) / Math.max(1, L - 1)))]; }
          roads.push({ xs, ys, hs: hs2, flags, cls });
        }
        run = [];
      };
      for (let k = 0; k < path.length; k++) {
        const i = path[k];
        if (!roadCell[i]) { if (!run.length && k > 0) run.push(path[k - 1]); run.push(i); }
        else if (run.length) { run.push(i); flush(); }
      }
      flush();
      for (const i of path) roadCell[i] = cls === 'main' ? 2 : Math.max(roadCell[i], 1);
    }
    // network: a spanning tree over the sites (shortest links first), a few extra loops between major sites,
    // and a service track from the genetics complex to every paddock gate
    const nodes = sites.map(t => ({ x: t.x, y: t.y, major: t.major, site: t }));
    const inTree = new Uint8Array(nodes.length), edges = [];
    if (nodes.length) {
      inTree[0] = 1;
      for (let added = 1; added < nodes.length; added++) {
        let ba = -1, bb = -1, bd = 1e30;
        for (let a = 0; a < nodes.length; a++) if (inTree[a]) for (let b = 0; b < nodes.length; b++) if (!inTree[b]) {
          const d = D.hyp(nodes[a].x - nodes[b].x, nodes[a].y - nodes[b].y); if (d < bd) { bd = d; ba = a; bb = b; }
        }
        inTree[bb] = 1; edges.push([ba, bb]);
      }
      const extra = [];
      for (let a = 0; a < nodes.length; a++) for (let b = a + 1; b < nodes.length; b++) {
        if (!nodes[a].major || !nodes[b].major || edges.some(e => (e[0] === a && e[1] === b) || (e[0] === b && e[1] === a))) continue;
        extra.push([D.hyp(nodes[a].x - nodes[b].x, nodes[a].y - nodes[b].y), a, b]);
      }
      extra.sort((u, v) => u[0] - v[0]).slice(0, 3).forEach(e => edges.push([e[1], e[2]]));
    }
    edges.sort((u, v) => (nodes[v[0]].major && nodes[v[1]].major ? 1 : 0) - (nodes[u[0]].major && nodes[u[1]].major ? 1 : 0));
    let bridges = 0, tunnels = 0;
    for (const [a, b] of edges) {
      const path = route(idx(nodes[a].x, nodes[a].y), idx(nodes[b].x, nodes[b].y));
      if (path) addRoad(path, nodes[a].major && nodes[b].major ? 'main' : 'track');
    }
    if (gen) for (const q of paddocks) {
      // gate: the fence vertex nearest the complex
      let bv = 0, bd = 1e30;
      for (let v = 0; v < q.poly.length; v += 2) { const d = D.hyp(q.poly[v] - gen.x, q.poly[v + 1] - gen.y); if (d < bd) { bd = d; bv = v; } }
      q.gate = [q.poly[bv], q.poly[bv + 1]];
      const path = route(idx(gen.x, gen.y), idx(q.gate[0], q.gate[1]));
      if (path) addRoad(path, 'track');
    }
    let roadKm = 0, mainKm = 0;
    for (const rd of roads) {
      for (let k = 1; k < rd.xs.length; k++) {
        const d = D.hyp(rd.xs[k] - rd.xs[k - 1], rd.ys[k] - rd.ys[k - 1]) / 1000; roadKm += d; if (rd.cls === 'main') mainKm += d;
        if ((rd.flags[k] & 1) && !(rd.flags[k - 1] & 1)) bridges++;
        if ((rd.flags[k] & 2) && !(rd.flags[k - 1] & 2)) tunnels++;
      }
    }
    // gates in compound fences face the nearest road
    for (const t of sites) {
      let bd = 1e30, gx = t.x, gy = t.y + t.r;
      for (const rd of roads) for (let k = 0; k < rd.xs.length; k++) {
        const d = D.hyp(rd.xs[k] - t.x, rd.ys[k] - t.y); if (d > t.r * 1.5 && d < bd) { bd = d; gx = rd.xs[k]; gy = rd.ys[k]; }
      }
      const dx = gx - t.x, dy = gy - t.y;
      t.gate = Math.abs(dx) > Math.abs(dy) ? (dx > 0 ? 'e' : 'w') : (dy > 0 ? 's' : 'n');
    }
    lap('roads');
    if (p.stopAfter === 'roads') return { stop: { roads, roadCell, roadKm, mainKm, bridges, tunnels, gates: sites.map(t => t.gate) } };

    // ---------- power: straight lines from the generators to every site ----------
    say('Stringing power lines');
    const power = [];
    {
      const roots = sites.filter(t => t.kind === 'nuclear' || t.kind === 'geothermal');
      const on = new Set(roots.map(t => t.id));
      if (!roots.length && sites.length) on.add(sites[0].id);
      while (on.size < sites.length) {
        let ba = null, bb = null, bd = 1e30;
        for (const a of sites) if (on.has(a.id)) for (const b of sites) if (!on.has(b.id)) {
          const d = D.hyp(a.x - b.x, a.y - b.y); if (d < bd) { bd = d; ba = a; bb = b; }
        }
        on.add(bb.id); power.push({ x1: ba.x, y1: ba.y, x2: bb.x, y2: bb.y, from: ba.name, to: bb.name });
      }
      // paddock fences hang on the genetics complex's substation
      if (gen) for (const q of paddocks) if (q.gate) power.push({ x1: gen.x, y1: gen.y, x2: q.gate[0], y2: q.gate[1], from: gen.name, to: q.name });
    }
    const powerKm = power.reduce((a, l) => a + D.hyp(l.x2 - l.x1, l.y2 - l.y1) / 1000, 0);

    // ---------- compounds: each facility built from its programme ----------
    const labels = [], prims = [];
    for (const t of sites) buildCompound(t, t.kind, labels, prims);
    const VIEW = { name: 'Viewing station', w: 16, h: 9, rooms: [['Viewing gallery', 3, 'furniture'], ["Keeper's room", 1, 'console']] };
    for (const q of paddocks) {
      const vx = D.round(q.gate[0] + q.out[0] * 30), vy = D.round(q.gate[1] + q.out[1] * 30);
      building(VIEW, vx - 8, vy - 4, vx + 7, vy + 4, q.name, nm => { labels.push(nm); return labels.length - 1; }, prims);
      const fx = D.round(q.gate[0] - q.out[0] * 28), fy = D.round(q.gate[1] - q.out[1] * 28);
      labels.push(q.name + ' · Feeding platform'); prims.push({ t: 'rect', x0: fx - 4, y0: fy - 4, x1: fx + 4, y1: fy + 4, cover: 'floor', label: labels.length - 1 });
      if (q.wallow) { labels.push(q.name + ' · Mud wallow'); const L = labels.length - 1; prims.push({ t: 'circle', x: q.wallow[0], y: q.wallow[1], r: 16, cover: 'mud', label: L }, { t: 'circle', x: q.wallow[0], y: q.wallow[1], r: 9, cover: 'shallow', label: L }); }
      if (q.fence === 'heavy') { // a second fence 5 m inside, and a dry moat where visitors look in
        const inner = [];
        for (let v = 0; v < q.poly.length; v += 2) { const dx = q.x - q.poly[v], dy = q.y - q.poly[v + 1], dl = D.hyp(dx, dy) || 1; inner.push(q.poly[v] + dx / dl * 5, q.poly[v + 1] + dy / dl * 5); }
        q.inner = inner;
      }
      if (q.moat) {
        labels.push(q.name + ' · Moat (engineered drop)'); const L = labels.length - 1;
        for (let v = 0; v < q.poly.length; v += 2) {
          const w2 = (v + 2) % q.poly.length;
          if (D.hyp(q.poly[v] - vx, q.poly[v + 1] - vy) > 180) continue;
          const mxp = q.poly[v], myp = q.poly[v + 1], nxp = q.poly[w2], nyp = q.poly[w2 + 1];
          const dx = q.x - mxp, dy = q.y - myp, dl = D.hyp(dx, dy) || 1, o = 12;
          prims.push({ t: 'line', ax: mxp + dx / dl * o, ay: myp + dy / dl * o, bx: nxp + dx / dl * o, by: nyp + dy / dl * o, w: 8, cover: 'moat', depth: 6, label: L });
        }
      }
    }
    if (p.stopAfter === 'sites') return { stop: { sites, leylines, paddocks, padCell, power, powerKm, labels, prims, mx, my } };
    // ---------- caves: lava tubes on the young flanks, sea caves in the cliffs ----------
    say('Tracing lava tubes');
    const caves = [];
    {
      const rc = makeRng(p.seed, 'worldgen.caves');
      const grad = (x, y) => {
        const gx = x / cell - 0.5, gy = y / cell - 0.5, e = 0.5;
        return [(bilinear(H0, nx, ny, gx + e, gy) - bilinear(H0, nx, ny, gx - e, gy)) / (2 * e * cell), (bilinear(H0, nx, ny, gx, gy + e) - bilinear(H0, nx, ny, gx, gy - e)) / (2 * e * cell)];
      };
      for (let k = 0, tries = 0; k < p.tubes && tries < 300; tries++) {
        const i = landCells[Math.floor(rc() * landCells.length)];
        const vd = D.hyp(cxOf(i) - A.vent.x, cyOf(i) - A.vent.y) / A.vent.R;
        if (!(oldW[i] < 0.45 || vd < 3) || H0[i] < 200 || H0[i] > 950 || slopeD[i] > 25) continue;
        if (caves.some(c => D.hyp(c.xs[0] - cxOf(i), c.ys[0] - cyOf(i)) < 2500 * s)) continue;
        let x = cxOf(i), y = cyOf(i), heading = null;
        const xs = [x], ys = [y], maxLen = (3000 + rc() * 7000) * sq, step = Math.min(cell * 0.6, 60), depth = 5 + rc() * 12;
        const nsd = stream(p.seed, 'tube.' + k);
        let len = 0, toSea = false;
        while (len < maxLen) {
          const [gx, gy] = grad(x, y), gl = D.hyp(gx, gy);
          if (gl < 0.004) break;
          let a = D.atan2(-gy, -gx) + 0.7 * noise(nsd, len / 600, 0.5);
          if (heading !== null) { let da = a - heading; while (da > Math.PI) da -= 2 * Math.PI; while (da < -Math.PI) da += 2 * Math.PI; a = heading + clamp(da, -0.5, 0.5); }
          heading = a; x += D.cos(a) * step; y += D.sin(a) * step; len += step;
          const j = idx(x, y);
          if (!land[j]) { toSea = true; break; }
          xs.push(x); ys.push(y);
        }
        if (len < 1500 * sq) continue;
        // where valleys have since cut below the tube's roof, the tube opens
        const mouths = [], skylights = [];
        let open = false;
        for (let m = 0; m < xs.length; m++) {
          const j = idx(xs[m], ys[m]), breached = H0[j] - H[j] > depth;
          if (breached !== open) { const last = mouths[mouths.length - 1]; if (!last || D.hyp(last[0] - xs[m], last[1] - ys[m]) > 300) mouths.push([xs[m], ys[m]]); open = breached; }
          if (m > 0 && m % Math.max(4, D.round(1300 / step)) === 0 && !breached && rc() < 0.7) skylights.push([xs[m], ys[m]]);
        }
        if (toSea) mouths.push([xs[xs.length - 1], ys[ys.length - 1]]);
        if (!mouths.length) mouths.push([xs[0], ys[0]]); // a collapse at the head
        caves.push({ id: caves.length, kind: 'lava tube', xs, ys, depth, lengthKm: len / 1000, mouths, skylights, name: 'Lava tube ' + (caves.length + 1) });
        k++;
      }
      const cliffCells = []; for (let i = 0; i < n; i++) if (coast[i] === 3) cliffCells.push(i);
      for (let k = 0, tries = 0; k < 4 && tries < 200 && cliffCells.length; tries++) {
        const i = cliffCells[Math.floor(rc() * cliffCells.length)], x = cxOf(i) + coastNX[i] * cell * 0.45, y = cyOf(i) + coastNY[i] * cell * 0.45;
        if (caves.some(c => c.kind === 'sea cave' && D.hyp(c.xs[0] - x, c.ys[0] - y) < 3000 * s)) continue;
        caves.push({ id: caves.length, kind: 'sea cave', xs: [x], ys: [y], depth: 0, lengthKm: 0.05 + rc() * 0.2, mouths: [[x, y]], skylights: [], name: 'Sea cave ' + (k + 1) });
        k++;
      }
    }

    // ---------- wrecks: where reefs and swell trap ships; a few aircraft inland ----------
    say('Scattering wrecks and ruins');
    const wrecks = [];
    {
      const rw = makeRng(p.seed, 'worldgen.wrecks'), js = stream(p.seed, 'wrecks.pick'), cand = [];
      for (let i = 0; i < n; i++) {
        let ex = -1, onReef = false, beached = false;
        if (!land[i] && (sea[i] === 3 || sea[i] === 2) && C.distSea[i] < 1500) {
          onReef = sea[i] === 3;
          // exposure of the nearest coast: look one step toward land
          const x = i % nx, y = (i - x) / nx; let best = 1e9;
          for (let k = 0; k < 8; k++) { const xx = x + DX[k] * 2, yy = y + DY[k] * 2; if (xx < 0 || yy < 0 || xx >= nx || yy >= ny) continue; const j = yy * nx + xx; if (coast[j] && C.distSea[i] < best) { best = C.distSea[i]; ex = coastEx[j]; } }
          if (ex < -0.9) ex = 0;
        } else if (coast[i] === 1) { ex = coastEx[i]; beached = true; }
        else continue;
        cand.push([i, ex + (onReef ? 0.6 : 0) + (beached ? 0.2 : 0) + 0.8 * hash2(js, i, 1) / 4294967296, onReef, beached]);
      }
      cand.sort((a, b) => b[1] - a[1]);
      for (const [i, , onReef, beached] of cand) {
        if (wrecks.length >= p.wrecks) break;
        const x = cxOf(i), y = cyOf(i);
        if (wrecks.some(w => D.hyp(w.x - x, w.y - y) < 1500 * s)) continue;
        const e = WRECK_ERAS[Math.floor(rw() * WRECK_ERAS.length)];
        wrecks.push({ id: wrecks.length, x, y, kind: e[0], era: e[1], length: e[2], heading: rw() * Math.PI, onReef, beached, name: e[0][0].toUpperCase() + e[0].slice(1) + ', ' + e[1] });
      }
      for (let k = 0, tries = 0; k < (p.wrecks > 0 ? 2 : 0) && tries < 300; tries++) {
        const i = landCells[Math.floor(rw() * landCells.length)];
        if (!(biome[i] === B.wet || biome[i] === B.moist || biome[i] === B.cloud || biome[i] === B.ridge) || slopeD[i] > 25) continue;
        const kind = k === 0 ? ['transport aircraft', '1940s', 28] : ['light aircraft', '1990s', 11];
        wrecks.push({ id: wrecks.length, x: cxOf(i), y: cyOf(i), kind: kind[0], era: kind[1], length: kind[2], heading: rw() * Math.PI, onReef: false, beached: false, inland: true, name: kind[0][0].toUpperCase() + kind[0].slice(1) + ', ' + kind[1] });
        k++;
      }
    }

    // ---------- ruins from different human ages ----------
    const ruins = [];
    {
      const ru = makeRng(p.seed, 'worldgen.ruins');
      const RS = {
        terraces: i => rain[i] > 1800 && slopeD[i] > 6 && slopeD[i] < 25 && H[i] > 40 && H[i] < 500 ? 1 + slopeD[i] / 25 : -1,
        stones: i => H[i] > 400 && crest[i] > 0.4 && slopeD[i] < 18 ? crest[i] + H[i] / 1000 : -1,
        platform: i => distLand[i] < 900 && slopeD[i] < 8 && nearRiver[i] ? 1.5 : -1,
        chapel: i => H[i] < 120 && slopeD[i] < 6 && nearRiver[i] ? 1.2 : -1,
        mill: i => H[i] < 150 && slopeD[i] < 5 && nearRiver[i] && rain[i] < 2000 ? 1.2 : -1,
        lighthouse: i => distLand[i] < 300 && seaFrac[i] > 0.45 && H[i] > 8 ? seaFrac[i] * 2 : -1,
        bunker: i => distLand[i] < 500 && seaFrac[i] > 0.35 && H[i] > 15 ? seaFrac[i] * 2 : -1,
        radar: i => H[i] > 0.6 * p.peak && slopeD[i] < 20 ? H[i] / 500 : -1,
        camp: i => slopeD[i] < 10 && H[i] < 600 ? 0.8 : -1,
        fieldlab: i => (biome[i] === B.wet || biome[i] === B.moist) && slopeD[i] < 10 ? 1 : -1
      };
      const singles = { lighthouse: 1, radar: 1 }, used = {};
      const order = RUIN_KINDS.map((K, k) => [ru(), K]).sort((a, b) => a[0] - b[0]).map(e => e[1]);
      for (let k = 0, tries = 0; k < p.ruins && tries < p.ruins * 6; tries++) {
        const K = order[tries % order.length];
        if (singles[K.key] && used[K.key]) continue;
        let best = -1, bs = 0;
        for (let m = 0; m < 2500; m++) {
          const i = landCells[Math.floor(ru() * landCells.length)];
          if (!dry(i)) continue;
          const v = RS[K.key](i); if (v <= bs) continue;
          const x = cxOf(i), y = cyOf(i);
          if (ruins.some(q => D.hyp(q.x - x, q.y - y) < 1800 * s) || sites.some(t => D.hyp(t.x - x, t.y - y) < t.r + 300)) continue;
          bs = v; best = i;
        }
        if (best < 0) continue;
        used[K.key] = 1;
        ruins.push({ id: ruins.length, kind: K.key, x: cxOf(best), y: cyOf(best), r: K.r, era: K.era, name: K.name + ' (' + K.era + ')', rot: ru() * Math.PI / 2 });
        k++;
      }
    }
    lap('caves, wrecks, ruins');

    // ---------- walking times between facilities (Tobler's hiking function) ----------
    say('Timing the walks');
    const f = Math.max(1, D.round(256 / cell)), cnx = Math.ceil(nx / f), cny = Math.ceil(ny / f), cn = cnx * cny, cs = cell * f;
    const ch = new Float64Array(cn), pass = new Uint8Array(cn), spd = new Float64Array(cn), wade = new Float64Array(cn), onRoad = new Uint8Array(cn);
    const SPEED = BIOMES.map(b => ({ strand: 0.85, marsh: 0.35, dryscrub: 0.85, grassland: 1, mesic: 0.8, moist: 0.6, wet: 0.45, cloud: 0.45, ridge: 0.7, cliff: 0.25, lava: 0.55 }[b.key] || 0));
    for (let cy = 0; cy < cny; cy++) for (let cx = 0; cx < cnx; cx++) {
      const c = cy * cnx + cx; let hs = 0, lc = 0, sp = 0, road = 0, river = 0, cliffy = 0, tot = 0;
      for (let y = cy * f; y < Math.min(ny, cy * f + f); y++) for (let x = cx * f; x < Math.min(nx, cx * f + f); x++) {
        const i = y * nx + x; tot++;
        if (roadCell[i]) road = 1; // a road (or its tunnel) can always be walked
        if (!dry(i)) continue;
        lc++; hs += H[i]; sp += SPEED[biome[i]]; if (isRiver[i] && Q[i] > 1) river = Math.max(river, Q[i]); if (slopeD[i] > 38) cliffy++;
      }
      if (!road && (lc * 2 < tot || cliffy * 2 > lc)) continue;
      if (!lc) { lc = 1; hs = 0; }
      pass[c] = 1; ch[c] = hs / lc; spd[c] = road ? 1.05 : sp / lc; onRoad[c] = road;
      wade[c] = road ? 0 : river > 0 ? 120 + 30 * Math.min(10, river) : 0;
    }
    const tob = sl => 6 * D.exp(-3.5 * Math.abs(sl + 0.05)) / 3.6 / (6 * D.exp(-3.5 * 0.05) / 3.6) * 1.4; // m/s, 1.4 on the flat
    const cIdx = t => clamp(Math.floor(t.y / cs), 0, cny - 1) * cnx + clamp(Math.floor(t.x / cs), 0, cnx - 1);
    const wd = new Float64Array(cn), wh = new Heap(cn * 8), wdone = new Uint8Array(cn);
    const targets = sites.map(t => { // nearest passable coarse cell (watch posts sit on narrow headlands)
      const c0 = cIdx(t); if (pass[c0]) return c0;
      const x0 = c0 % cnx, y0 = (c0 - x0) / cnx;
      for (let rr = 1; rr < 6; rr++) for (let yy = y0 - rr; yy <= y0 + rr; yy++) for (let xx = x0 - rr; xx <= x0 + rr; xx++)
        if (xx >= 0 && yy >= 0 && xx < cnx && yy < cny && pass[yy * cnx + xx]) return yy * cnx + xx;
      return c0;
    });
    function travel(extra) { // minutes between every pair of facilities; extra = Map(node -> [[node, seconds], ...])
      const walk = sites.map(() => sites.map(() => Infinity));
      sites.forEach((t, a) => {
        wd.fill(Infinity); wdone.fill(0); wh.n = 0;
        const s0 = targets[a]; if (!pass[s0]) return;
        wd[s0] = 0; wh.push(0, s0); let left = sites.length;
        while (wh.n > 0 && left > 0) {
          const c = wh.pop(); if (wdone[c]) continue; wdone[c] = 1;
          for (let b = 0; b < sites.length; b++) if (targets[b] === c && walk[a][b] === Infinity) { walk[a][b] = wd[c] / 60; left--; }
          const x = c % cnx, y = (c - x) / cnx;
          for (let k = 0; k < 8; k++) {
            const xx = x + DX[k], yy = y + DY[k]; if (xx < 0 || yy < 0 || xx >= cnx || yy >= cny) continue;
            const j = yy * cnx + xx; if (!pass[j] || wdone[j]) continue;
            const d = DL[k] * cs, sl = (ch[j] - ch[c]) / d;
            if (Math.abs(sl) > 0.8 && !(onRoad[c] && onRoad[j])) continue;
            const v = tob(onRoad[c] && onRoad[j] ? sl * 0.6 : sl) * (spd[c] + spd[j]) / 2;
            const nd = wd[c] + d / Math.max(0.05, v) + wade[j];
            if (nd < wd[j]) { wd[j] = nd; wh.push(nd, j); }
          }
          const ex = extra && extra.get(c);
          if (ex) for (const [j, sec] of ex) { const nd = wd[c] + sec; if (nd < wd[j] && wh.n < cn * 8 - 1) { wd[j] = nd; wh.push(nd, j); } }
        }
      });
      return walk;
    }
    const walk = travel(null);
    sites.forEach((t, a) => {
      let best = -1; for (let b = 0; b < sites.length; b++) if (b !== a && (best < 0 || walk[a][b] < walk[a][best])) best = b;
      t.nearest = best >= 0 ? { id: best, name: sites[best].name, minutes: walk[a][best] } : null;
    });
    lap('walking');

    // ---------- transit: a skyway over the pali, a summit lift, a street tram, an underground monorail ----------
    say('Laying out transit');
    const transit = { lifts: [], tram: null, monorail: null }, objectives = [];
    const edges2 = new Map(), link = (a, b, sec) => { for (const [u, v] of [[a, b], [b, a]]) { let L = edges2.get(u); if (!L) edges2.set(u, L = []); L.push([v, sec]); } };
    const nodeAt = (x, y) => { const c0 = clamp(Math.floor(y / cs), 0, cny - 1) * cnx + clamp(Math.floor(x / cs), 0, cnx - 1); return c0; };
    const stationPt = (t, tx, ty) => { const dx = tx - t.x, dy = ty - t.y, dl = D.hyp(dx, dy) || 1; return [D.round(t.x + dx / dl * (t.r + 16)), D.round(t.y + dy / dl * (t.r + 16))]; };
    const stationBld = (pt, line, name, rooms) => building({ name, w: 18, h: 10, rooms }, pt[0] - 9, pt[1] - 5, pt[0] + 8, pt[1] + 4, line, nm => { labels.push(nm); return labels.length - 1; }, prims);
    function makeLift(a, b, name) {
      const A1 = stationPt(a, b.x, b.y), B1 = stationPt(b, a.x, a.y), L = D.hyp(B1[0] - A1[0], B1[1] - A1[1]);
      const towers = [], nT = Math.max(1, D.round(L / 350));
      for (let k = 1; k < nT; k++) towers.push([A1[0] + (B1[0] - A1[0]) * k / nT, A1[1] + (B1[1] - A1[1]) * k / nT]);
      labels.push(name + ' · Lift tower'); const tl = labels.length - 1;
      for (const q of towers) prims.push({ t: 'tower', x: q[0], y: q[1], label: tl });
      stationBld(A1, name, 'Station at ' + a.name, [['Cabin hall', 3], ['Winch room', 1, 'tanks']]);
      stationBld(B1, name, 'Station at ' + b.name, [['Cabin hall', 3], ['Winch room', 1, 'tanks']]);
      let top = 0; for (let k = 0; k <= 20; k++) top = Math.max(top, H[idx(A1[0] + (B1[0] - A1[0]) * k / 20, A1[1] + (B1[1] - A1[1]) * k / 20)]);
      transit.lifts.push({ name, from: a.name, to: b.name, x1: A1[0], y1: A1[1], x2: B1[0], y2: B1[1], km: L / 1000, towers, crest: top });
      link(nodeAt(a.x, a.y), nodeAt(b.x, b.y), L / 5 + 180);
    }
    if (p.transit > 0 && sites.length > 2) {
      // the skyway: from a facility whose walks are slow, straight up over the crest toward where people need to go,
      // down to a road on the far side
      let best = null, bs = 20;
      for (let a = 0; a < sites.length; a++) for (let b = 0; b < sites.length; b++) {
        const A1 = sites[a], B1 = sites[b], d = D.hyp(A1.x - B1.x, A1.y - B1.y);
        if (a === b || d < 2000 * sq || walk[a][b] === Infinity || walk[a][b] / (d / 1.4 / 60) < 1.5) continue;
        const ux = (B1.x - A1.x) / d, uy = (B1.y - A1.y) / d, reach = Math.min(d, 7000 * sq);
        let crest = A1.h, crestAt = 0, end = null;
        for (let t = 100; t <= reach; t += cell / 2) {
          const hh = H[idx(A1.x + ux * t, A1.y + uy * t)];
          if (hh > crest) { crest = hh; crestAt = t; }
          else if (crestAt && crest - A1.h > 200 && crest - hh > 200) {
            const ex = A1.x + ux * t, ey = A1.y + uy * t, ei = idx(ex, ey);
            let roadNear = false; const ex0 = ei % nx, ey0 = (ei - ex0) / nx;
            const rr2 = Math.max(3, D.round(700 / cell));
            for (let yy = Math.max(0, ey0 - rr2); yy <= Math.min(ny - 1, ey0 + rr2); yy++) for (let xx = Math.max(0, ex0 - rr2); xx <= Math.min(nx - 1, ex0 + rr2); xx++) if (roadCell[yy * nx + xx]) roadNear = true;
            if (roadNear && dry(ei)) { end = { x: D.round(ex), y: D.round(ey), t }; break; }
          }
        }
        if (!end) continue;
        const saved = walk[a][b] - (end.t / 5 + 180) / 60 - D.hyp(B1.x - end.x, B1.y - end.y) / 1.1 / 60;
        if (saved > bs) { bs = saved; best = [A1, end]; }
      }
      if (best) {
        const e = best[1], ei = idx(e.x, e.y);
        makeLift(best[0], { x: e.x, y: e.y, r: 0, h: H[ei], name: 'skyway station, ' + compass(e.x, e.y) }, 'Pali skyway');
      }
      // a summit lift up to the highest facility, from the nearest major one
      const summit = sites.filter(t => t.kind === 'weather' || t.kind === 'comms').sort((u, v) => v.h - u.h)[0];
      if (summit && !transit.lifts.some(l => l.from === summit.name || l.to === summit.name)) {
        const m = sites.filter(t => t.major).map(t => [t, D.hyp(t.x - summit.x, t.y - summit.y)]).sort((u, v) => u[1] - v[1])[0];
        if (m && m[1] < 7000 * sq && walk[summit.id][m[0].id] > 60) makeLift(m[0], summit, 'Summit lift');
      }
      // street tram: from the control centre through its nearest major neighbours, on gentle grades, mostly in the road
      const hub = sites.find(t => t.kind === 'control');
      if (hub) {
        const pool = sites.filter(t => t.kind !== 'watch' && t !== hub && D.hyp(t.x - hub.x, t.y - hub.y) < 11000 * sq).sort((u, v) => D.hyp(u.x - hub.x, u.y - hub.y) - D.hyp(v.x - hub.x, v.y - hub.y)).slice(0, 4);
        const order = [hub]; let cur = hub; const left2 = pool.slice();
        while (left2.length) { left2.sort((u, v) => D.hyp(u.x - cur.x, u.y - cur.y) - D.hyp(v.x - cur.x, v.y - cur.y)); cur = left2.shift(); order.push(cur); }
        const tramCost = (a, b, k) => {
          const d = DL[k] * cell, gr = Math.abs(H[b] - H[a]) / d;
          let c = gr > 0.06 ? d * 40 : d * (1 + (gr / 0.03) * (gr / 0.03));
          if (isRiver[b]) c += 300; if (padCell[b] >= 0) c += d * 30;
          return roadCell[b] ? c * 0.25 : c;
        };
        if (order.length >= 3) {
          const xs = [], ys = [], stops = [];
          for (let k = 0; k + 1 < order.length; k++) {
            const path = route(idx(order[k].x, order[k].y), idx(order[k + 1].x, order[k + 1].y), tramCost);
            if (!path) break;
            const pts = chaikin(path.map(i => [cxOf(i), cyOf(i), H[i], 0]), 2);
            let Lp = 0; for (let m = 1; m < pts.length; m++) Lp += D.hyp(pts[m][0] - pts[m - 1][0], pts[m][1] - pts[m - 1][1]);
            if (Lp > 2.2 * D.hyp(order[k + 1].x - order[k].x, order[k + 1].y - order[k].y)) { order.length = k + 1; break; } // too roundabout: end the line here
            for (const q of pts) { xs.push(q[0]); ys.push(q[1]); }
            let Lk = 0; for (let m = 1; m < pts.length; m++) Lk += D.hyp(pts[m][0] - pts[m - 1][0], pts[m][1] - pts[m - 1][1]);
            link(nodeAt(order[k].x, order[k].y), nodeAt(order[k + 1].x, order[k + 1].y), Lk / 7 + 240);
          }
          if (xs.length) { // no line at all if the first leg could not be built
          labels.push('Tram stop'); const sl = labels.length - 1;
          for (const t of order) {
            // a platform beside the line where it leaves the compound
            let bm = 0, bd = 1e30; for (let m = 0; m < xs.length; m++) { const d = Math.abs(D.hyp(xs[m] - t.x, ys[m] - t.y) - t.r - 25); if (d < bd) { bd = d; bm = m; } }
            const px = D.round(xs[bm]), py = D.round(ys[bm]);
            prims.push({ t: 'rect', x0: px - 4, y0: py - 4, x1: px + 4, y1: py + 4, cover: 'floor', label: sl });
            stops.push({ name: t.name, x: px, y: py });
            if (stops.length >= order.length) break;
          }
          let km = 0; for (let m = 1; m < xs.length; m++) km += D.hyp(xs[m] - xs[m - 1], ys[m] - ys[m - 1]) / 1000;
          transit.tram = { name: 'Park tram', xs: Float32Array.from(xs), ys: Float32Array.from(ys), stops, km };
          }
        }
      }
      // underground monorail: the corporate core, straight under everything
      const core = ['genetics', 'control', 'lab', 'orgone', 'nuclear'].map(k => sites.filter(t => t.kind === k).sort((u, v) => (gen ? D.hyp(v.x - gen.x, v.y - gen.y) - D.hyp(u.x - gen.x, u.y - gen.y) : 0))[0]).filter(Boolean).slice(0, 4);
      if (core.length >= 3) {
        const order = [core[0]]; const left3 = core.slice(1);
        while (left3.length) { const cur = order[order.length - 1]; left3.sort((u, v) => D.hyp(u.x - cur.x, u.y - cur.y) - D.hyp(v.x - cur.x, v.y - cur.y)); order.push(left3.shift()); }
        const stations = order.map(t => { const pt = stationPt(t, mx, my); stationBld(pt, 'Underground monorail', 'Station at ' + t.name, [['Elevator lobby', 2, 'console'], ['Elevator', 1, 'lift']]); return { name: t.name, x: pt[0], y: pt[1], z: t.h - 25 }; });
        labels.push('Monorail ventilation shaft'); const vl = labels.length - 1;
        const vents = []; let km = 0, minCover = 1e9;
        for (let k = 0; k + 1 < stations.length; k++) {
          const a = stations[k], b = stations[k + 1], L = D.hyp(b.x - a.x, b.y - a.y); km += L / 1000;
          // the bore stays 20 m under the ground and climbs or falls at most 6 %: the lower envelope of the ground
          // the bore stays 20 m under the ground at a grade of 6 % at most; each station reaches it by elevator, however deep
          const NS = 80, gz = []; for (let m = 0; m <= NS; m++) { const f2 = m / NS, gi = idx(a.x + (b.x - a.x) * f2, a.y + (b.y - a.y) * f2); gz.push((land[gi] ? H[gi] : 0) - 20); }
          gz[0] = Math.min(gz[0], a.z); gz[NS] = Math.min(gz[NS], b.z);
          const zz = gz.map((v, m) => { let z = v; for (let q = 0; q <= NS; q++) z = Math.min(z, gz[q] + 0.06 * Math.abs(q - m) * L / NS); return z; });
          a.z = Math.min(a.z, zz[0]); b.z = Math.min(b.z, zz[NS]);
          for (let m = 0; m <= NS; m++) minCover = Math.min(minCover, gz[m] + 20 - zz[m]);
          const nV = Math.floor(L / 2000);
          for (let m = 1; m <= nV; m++) { const vx = D.round(a.x + (b.x - a.x) * m / (nV + 1)), vy = D.round(a.y + (b.y - a.y) * m / (nV + 1)); if (dry(idx(vx, vy))) { vents.push([vx, vy]); prims.push({ t: 'rect', x0: vx - 2, y0: vy - 2, x1: vx + 2, y1: vy + 2, cover: 'wall', label: vl }); } }
          link(nodeAt(order[k].x, order[k].y), nodeAt(order[k + 1].x, order[k + 1].y), L / 18 + 150);
        }
        stations.forEach((st, k) => { st.shaft = order[k].h - st.z; st.power = order[k].name; });
        // the elevators run on the site's power and need a monorail access key: an objective, kept in a security office
        const keySite = sites.find(t => t.kind === 'genetics') || sites.find(t => t.kind === 'control');
        const keyRoom = keySite && labels.findIndex(l => l.startsWith(keySite.name + ' · ') && /Security office|Security|Offices/.test(l));
        if (keySite) {
          const room = prims.find(q => q.t === 'bld' && q.rooms.some(r => r.label === keyRoom));
          const rr = room ? room.rooms.find(r => r.label === keyRoom) : null;
          objectives.push({ key: 'monorail-key', name: 'Monorail access key', unlocks: 'the monorail station elevators', site: keySite.name,
            room: keyRoom >= 0 ? labels[keyRoom].split(' · ').slice(1).join(' · ') : 'the main building', x: rr ? (rr.x0 + rr.x1) / 2 : keySite.x, y: rr ? (rr.y0 + rr.y1) / 2 : keySite.y });
        }
        transit.monorail = { name: 'Underground monorail', stations, vents, km, minCover, deepest: Math.max(...stations.map(st => st.shaft)) };
      }
    }
    const walkTransit = transit.lifts.length || transit.tram || transit.monorail ? travel(edges2) : walk;
    sites.forEach((t, a) => {
      let best = -1; for (let b = 0; b < sites.length; b++) if (b !== a && (best < 0 || walkTransit[a][b] < walkTransit[a][best])) best = b;
      t.nearestTransit = best >= 0 ? { id: best, name: sites[best].name, minutes: walkTransit[a][best] } : null;
    });
    lap('walking and transit');
    if (p.stopAfter === 'transit') return { stop: { walk, walkTransit, transit, objectives, labels, prims, nearest: sites.map(t => t.nearest), nearestTransit: sites.map(t => t.nearestTransit) } };

    // ---------- wildlife habitat ----------
    say('Mapping wildlife');
    const people = new Uint8Array(n).fill(1);
    for (const t of sites) people[idx(t.x, t.y)] = 0;
    for (const q of ruins) people[idx(q.x, q.y)] = 0;
    const dPeople = chamfer(people, nx, ny, cell);
    const caveNear = new Uint8Array(n);
    for (const c of caves) for (const m of c.mouths) { const i0 = idx(m[0], m[1]), x0 = i0 % nx, y0 = (i0 - x0) / nx; for (let yy = Math.max(0, y0 - 6); yy <= Math.min(ny - 1, y0 + 6); yy++) for (let xx = Math.max(0, x0 - 6); xx <= Math.min(nx - 1, x0 + 6); xx++) caveNear[yy * nx + xx] = 1; }
    const fauna = FAUNA.map(F => {
      const dens = new Uint8Array(n); let pop = 0;
      for (let i = 0; i < n; i++) {
        if (!land[i] || !isNaN(lake[i]) || padCell[i] >= 0) continue;
        let v = F.biomes[BIOMES[biome[i]].key] || 0;
        if (F.rocky && coast[i] === 2) v = Math.max(v, F.rocky);
        if (F.cliffs && coast[i] === 3) v = Math.max(v, F.cliffs);
        if (F.cliffs && lblM[i] !== mainL) v = Math.max(v, 0.8); // islets
        if (!v) continue;
        if (F.water) v *= 0.4 + F.water * Math.min(1, (nearRiver[i] ? 1 : 0) + C.wet[i]);
        if (F.steep) v *= 1 + F.steep * (slopeD[i] > 25 ? 1 : 0);
        if (F.low) v *= H[i] < 300 ? 1 : 0.3;
        if (F.caves) v *= 0.6 + F.caves * caveNear[i];
        if (F.people) { const near = D.exp(-dPeople[i] / 2000); v *= F.peopleOnly ? near * F.people : 1 + F.people * near; }
        v = Math.min(1, v);
        dens[i] = D.round(v * 255);
        pop += v * F.perKm2 * area / 1e6;
      }
      return { key: F.key, name: F.name, note: F.note, density: dens, population: D.round(pop) };
    });
    lap('wildlife');

    return { sites, paddocks, roads, power, caves, wrecks, ruins, leylines, walk, walkTransit, transit, objectives, labels, prims, fauna, roadKm, mainKm, bridges, tunnels, powerKm };
  }

  // ---------- the pipeline ----------
  function generate(params, progress) {
    const p = Object.assign({}, DEFAULTS, params);
    const say = progress || function () { };
    const now = () => (typeof performance !== 'undefined' ? performance.now() : Date.now());
    const timing = {}; let t = now();
    function lap(name) { const t2 = now(); timing[name] = D.round(t2 - t); t = t2; }

    say('Placing shields, vent and islets');
    const A = makeAnchors(p);
    const cell = p.cell, nx = D.round(A.W / cell), ny = D.round(A.H / cell), n = nx * ny, area = cell * cell;
    const H = new Float64Array(n), oldW = new Float32Array(n), lava = new Float32Array(n), erod = new Float32Array(n), pali = new Float32Array(n);
    const o = {};
    say('Raising the landmass');
    for (let y = 0; y < ny; y++) for (let x = 0; x < nx; x++) {
      const i = y * nx + x, wx = (x + 0.5) * cell, wy = (y + 0.5) * cell;
      sample(A, wx, wy, o);
      H[i] = o.e; oldW[i] = o.old; lava[i] = o.lava; pali[i] = o.pali;
      erod[i] = clamp((1 + 0.35 * o.old + 0.25 * fbm(A.nErod, wx / (5000 * A.s), wy / (5000 * A.s), 3)) * (1 - 0.75 * smoothstep(0.35, 0.8, o.lava)), 0.15, 2);
    }
    lap('landmass');

    // Ocean = below sea level and connected to the frame edge; anything else below 0 is inland.
    say('Drawing the coast');
    function oceanMask() {
      const oc = new Uint8Array(n), q = new Int32Array(n); let qh = 0, qt = 0;
      const seed = i => { if (H[i] <= 0 && !oc[i]) { oc[i] = 1; q[qt++] = i; } };
      for (let x = 0; x < nx; x++) { seed(x); seed((ny - 1) * nx + x); }
      for (let y = 0; y < ny; y++) { seed(y * nx); seed(y * nx + nx - 1); }
      while (qh < qt) {
        const c = q[qh++], x = c % nx, y = (c - x) / nx;
        for (let k = 0; k < 8; k += 2) {
          const xx = x + DX[k], yy = y + DY[k];
          if (xx < 0 || yy < 0 || xx >= nx || yy >= ny) continue;
          const j = yy * nx + xx; if (!oc[j] && H[j] <= 0) { oc[j] = 1; q[qt++] = j; }
        }
      }
      return oc;
    }
    let ocean = oceanMask();
    const land = new Uint8Array(n); for (let i = 0; i < n; i++) land[i] = ocean[i] ? 0 : 1;
    let distLand = chamfer(land, nx, ny, cell);   // land: metres to the sea

    // Sea cliffs: waves cut back coasts that face the trade wind or the north swell,
    // further where rock is soft, less on young lava. What is left behind is a cliff.
    const wa = p.wind * Math.PI / 180, wfx = D.sin(wa), wfy = -D.cos(wa);
    const sa = 330 * Math.PI / 180, sfx = D.sin(sa), sfy = -D.cos(sa);
    function exposure(ox, oy) { return Math.max(ox * wfx + oy * wfy, 0.65 * (ox * sfx + oy * sfy)); }
    let cutKm2 = 0;
    if (p.cliffs > 0) {
      const maxR = p.cliffs * 1000 * A.s * 1.6, cut = [];
      for (let i = 0; i < n; i++) {
        if (!land[i] || distLand[i] > maxR) continue;
        const x = i % nx, y = (i - x) / nx;
        const gx = (distLand[y * nx + Math.min(x + 1, nx - 1)] - distLand[y * nx + Math.max(x - 1, 0)]);
        const gy = (distLand[Math.min(y + 1, ny - 1) * nx + x] - distLand[Math.max(y - 1, 0) * nx + x]);
        const gl = D.hyp(gx, gy); if (gl === 0) continue;
        const ex = smoothstep(0.15, 0.75, exposure(-gx / gl, -gy / gl)); if (ex <= 0) continue;
        const wx = (x + 0.5) * cell, wy = (y + 0.5) * cell;
        const nr = clamp(0.85 + 1.1 * fbm(A.nCliff, wx / (3500 * A.s), wy / (3500 * A.s), 3), 0.15, 1.5);
        const R = p.cliffs * 1000 * A.s * ex * nr * (1 - 0.75 * lava[i]) * (0.55 + 0.45 * erod[i]);
        if (distLand[i] < R) cut.push(i);
      }
      for (const i of cut) H[i] = -2;
      ocean = oceanMask();
      for (const i of cut) if (!ocean[i]) H[i] = 1; // a cut cell left inland stays land
      cutKm2 = cut.length * area / 1e6;
      for (let i = 0; i < n; i++) land[i] = ocean[i] ? 0 : 1;
      distLand = chamfer(land, nx, ny, cell);
    }
    // Fragments the sea has cut off (and noise specks) under 0.5 km² are worn down to one sea stack each,
    // so the planned islets stay the only real islets.
    {
      const lbl = new Int32Array(n), sizes = components(land, nx, ny, lbl);
      let mainK = 0; sizes.forEach((v, k) => { if (v > sizes[mainK]) mainK = k; });
      const planned = new Set(A.islets.map(I => lbl[clamp(Math.floor(I.y / cell), 0, ny - 1) * nx + clamp(Math.floor(I.x / cell), 0, nx - 1)]));
      const limit = 0.5 * A.s * 1e6 / area, top = new Map();
      for (let i = 0; i < n; i++) { const k = lbl[i]; if (k < 0 || k === mainK || planned.has(k) || sizes[k] >= limit) continue; const t = top.get(k); if (t === undefined || H[i] > H[t]) top.set(k, i); }
      if (top.size) {
        const keep = new Set(top.values());
        for (let i = 0; i < n; i++) { const k = lbl[i]; if (top.has(k) && !keep.has(i)) H[i] = -2; }
        ocean = oceanMask();
        for (let i = 0; i < n; i++) land[i] = ocean[i] ? 0 : 1;
        distLand = chamfer(land, nx, ny, cell);
      }
    }
    const distSea = chamfer(ocean, nx, ny, cell);   // sea: metres to land
    for (let i = 0; i < n; i++) {
      if (land[i]) { if (H[i] > 0) H[i] += 1; }
      else { const dc = distSea[i]; H[i] = -Math.min(3800, 1 + Math.min(dc, 1500) * 0.012 + Math.max(0, dc - 1500) * 0.12); }
    }
    const H0 = Float32Array.from(H);
    lap('coast');

    // ---------- hydrology and erosion ----------
    const T = makeTree(nx, ny, cell), filled = new Float64Array(n), closed = new Uint8Array(n);
    const heap = new Heap(n), pit = new Int32Array(n), Aeff = new Float64Array(n), Hn = new Float64Array(n);
    let rain = computeRain(H, land, nx, ny, cell, p.wind, p.rain);
    const Kdt = 0.012 * p.erosion * (cell / 128), fillRate = 0.08 * Math.min(1.5, p.erosion);
    const tanT = 0.75;
    for (let it = 0; it < p.iterations; it++) {
      say('Eroding, pass ' + (it + 1) + ' of ' + p.iterations);
      flood(H, land, nx, ny, filled, closed, heap, pit);
      buildTree(T, H, filled, land);
      const st = T.stack, ns = T.ns, rec = T.rec;
      for (let k = 0; k < ns; k++) { const i = st[k]; Aeff[i] = area * rain[i] / 2000; }
      for (let k = ns - 1; k >= 0; k--) { const i = st[k], r = rec[i]; if (r >= 0) Aeff[r] += Aeff[i]; }
      for (let k = 0; k < ns; k++) {
        const i = st[k], r = rec[i], hr = r >= 0 ? Hn[r] : 0, hi = H[i];
        let h;
        if (hi > hr) { const F = Kdt * erod[i] * (1 - 0.4 * pali[i]) * Math.sqrt(Aeff[i]) / T.rdist[i]; h = (hi + F * hr) / (1 + F); }
        else h = hi + (filled[i] - hi) * fillRate;
        Hn[i] = Math.max(h, Math.min(hi, 1));
      }
      for (let k = 0; k < ns; k++) { const i = st[k]; H[i] = Hn[i]; }
      // light thermal pass: crumble crags steeper than ~37 degrees, but leave sea cliffs and young lava alone
      for (let i = 0; i < n; i++) {
        if (!land[i] || distLand[i] < 600 || erod[i] < 0.5 || pali[i] > 0.25) continue;
        const x = i % nx, y = (i - x) / nx; let lo = -1, ld = 0;
        for (let k = 0; k < 8; k++) {
          const xx = x + DX[k], yy = y + DY[k];
          if (xx < 0 || yy < 0 || xx >= nx || yy >= ny) continue;
          const j = yy * nx + xx; if (!land[j]) continue;
          const dd = H[i] - H[j] - tanT * DL[k] * cell; if (dd > ld) { ld = dd; lo = j; }
        }
        if (lo >= 0) { const mv = ld * 0.15; H[i] -= mv; H[lo] += mv; }
      }
    }
    lap('erosion');

    say('Filling basins and finding lakes');
    flood(H, land, nx, ny, filled, closed, heap, pit);
    const depMask = new Uint8Array(n);
    for (let i = 0; i < n; i++) if (land[i] && filled[i] - H[i] > 0.3) depMask[i] = 1;
    const label = new Int32Array(n), dsz = components(depMask, nx, ny, label);
    const dmax = new Float64Array(dsz.length), dlev = new Float64Array(dsz.length).fill(-1e9);
    for (let i = 0; i < n; i++) { const l = label[i]; if (l >= 0) { dmax[l] = Math.max(dmax[l], filled[i] - H[i]); dlev[l] = Math.max(dlev[l], filled[i]); } }
    const lake = new Float32Array(n).fill(NaN), lakes = [];
    const inCal = new Uint8Array(dsz.length);
    for (let i = 0; i < n; i++) { const l = label[i]; if (l >= 0 && D.hyp((i % nx + 0.5) * cell - A.caldera.x, (Math.floor(i / nx) + 0.5) * cell - A.caldera.y) < A.caldera.R) inCal[l] = 1; }
    const keep = dsz.map((sz, l) => (dmax[l] >= p.lakeDepth && sz * area / 1e6 >= p.lakeArea) || (inCal[l] && dmax[l] >= 4 && sz * area / 1e6 >= 0.05));
    dsz.forEach((sz, l) => { if (keep[l]) lakes.push({ id: l, area: sz * area / 1e6, depth: dmax[l], level: dlev[l], cx: 0, cy: 0 }); });
    for (let i = 0; i < n; i++) {
      const l = label[i]; if (l < 0) continue;
      if (keep[l]) {
        lake[i] = dlev[l];
        const L = lakes.find(z => z.id === l); L.cx += (i % nx + 0.5) * cell; L.cy += (Math.floor(i / nx) + 0.5) * cell;
      } else H[i] = filled[i];
    }
    for (const L of lakes) { const c = dsz[L.id]; L.cx /= c; L.cy /= c; }
    buildTree(T, H, filled, land);
    rain = computeRain(H, land, nx, ny, cell, p.wind, p.rain);
    lap('lakes');

    say('Tracing rivers');
    const st = T.stack, ns = T.ns, rec = T.rec, Q = new Float64Array(n);
    const runoff = 0.55, year = 3.156e7;
    for (let k = 0; k < ns; k++) { const i = st[k]; Q[i] = rain[i] / 1000 * area * runoff / year; }
    for (let k = ns - 1; k >= 0; k--) {
      const i = st[k], r = rec[i];
      if (r >= 0) Q[r] += Q[i] * D.pow(0.985, T.rdist[i] / 1000);
    }
    const toSea = new Float64Array(n);
    for (let k = 0; k < ns; k++) { const i = st[k], r = rec[i]; toSea[i] = (r >= 0 ? toSea[r] : 0) + T.rdist[i]; }
    const isRiver = new Uint8Array(n), upRiver = new Uint8Array(n);
    for (let i = 0; i < n; i++) if (land[i] && Q[i] >= p.riverQ) isRiver[i] = 1;
    for (let i = 0; i < n; i++) if (isRiver[i] && rec[i] >= 0 && isRiver[rec[i]]) upRiver[rec[i]] = 1;
    // heads, biggest first so trunks are traced from their longest source
    const heads = [];
    for (let i = 0; i < n; i++) if (isRiver[i] && !upRiver[i]) heads.push(i);
    heads.sort((a, b) => toSea[b] - toSea[a]);
    const visited = new Uint8Array(n), rivers = [];
    const nJit = stream(p.seed, 'worldgen.riverjitter');
    function cx(i) { const x = i % nx; return (x + 0.5 + ((hash2(nJit, i, 1) / 4294967296) - 0.5) * 0.5) * cell; }
    function cy(i) { const y = Math.floor(i / nx); return (y + 0.5 + ((hash2(nJit, i, 2) / 4294967296) - 0.5) * 0.5) * cell; }
    let streamLen = 0;
    for (const h0 of heads) {
      const pts = []; let i = h0, end = 'join';
      while (true) {
        pts.push([cx(i), cy(i), Q[i], Math.max(0, isNaN(lake[i]) ? H[i] : lake[i])]);
        if (visited[i]) break;
        visited[i] = 1;
        const r = rec[i];
        if (r < 0) {
          streamLen += T.rdist[i] / 2;
          const m = T.mouth[i];
          if (m >= 0) { const mx = (m % nx + 0.5) * cell, my = (Math.floor(m / nx) + 0.5) * cell; pts.push([(pts[pts.length - 1][0] + mx) / 2, (pts[pts.length - 1][1] + my) / 2, Q[i], 0]); }
          end = 'sea'; break;
        }
        streamLen += T.rdist[i];
        i = r;
      }
      if (pts.length < 2) continue;
      let sm = chaikin(pts, 2);
      for (let k = 1; k < sm.length; k++) if (sm[k][3] > sm[k - 1][3]) sm[k][3] = sm[k - 1][3];
      const L = sm.length, xs = new Float32Array(L), ys = new Float32Array(L), qs = new Float32Array(L), hs = new Float32Array(L);
      for (let k = 0; k < L; k++) { xs[k] = sm[k][0]; ys[k] = sm[k][1]; qs[k] = sm[k][2]; hs[k] = sm[k][3]; }
      rivers.push({ xs, ys, qs, hs, end, head: h0 });
    }
    // waterfalls: steep steps along a river, merged from the top of each run
    const steep = new Uint8Array(n), stepDrop = new Float64Array(n);
    for (let i = 0; i < n; i++) {
      if (!land[i] || Q[i] < 0.02) continue; // cascades on small streams count too
      const r = rec[i], hr = r >= 0 ? H[r] : 0, drop = H[i] - hr;
      if (drop > Math.max(15, 0.3 * T.rdist[i])) { steep[i] = 1; stepDrop[i] = drop; }
    }
    const falls = [];
    for (let i = 0; i < n; i++) {
      if (!steep[i]) continue;
      let top = true;
      for (let qd = T.donStart[i]; qd < T.donStart[i + 1]; qd++) if (steep[T.donors[qd]]) { top = false; break; }
      if (!top) continue;
      let j = i, total = 0;
      while (j >= 0 && steep[j]) { total += stepDrop[j]; j = rec[j]; }
      if (total < 40 && Q[i] < p.riverQ) continue;
      falls.push({ x: (i % nx + 0.5) * cell, y: (Math.floor(i / nx) + 0.5) * cell, drop: total, q: Q[i], sea: j < 0, river: Q[i] >= p.riverQ });
    }
    lap('rivers');

    // ---------- coast classes and sea bands ----------
    say('Classifying the coast');
    const coast = new Uint8Array(n), sea = new Uint8Array(n), coastNX = new Float32Array(n), coastNY = new Float32Array(n), coastEx = new Float32Array(n);
    const COAST_BEACH = 1, COAST_ROCKY = 2, COAST_CLIFF = 3;
    const BEACH_H = 30, BEACH_EX = 0.2, BEACH_BAY = 0.6; // low, sheltered coast in a bay
    const bayR = Math.max(2, D.round(1000 / cell));
    function seaNear(x, y) { let sc = 0, tot = 0; for (let yy = Math.max(0, y - bayR); yy <= Math.min(ny - 1, y + bayR); yy++) for (let xx = Math.max(0, x - bayR); xx <= Math.min(nx - 1, x + bayR); xx++) { tot++; if (!land[yy * nx + xx]) sc++; } return sc / tot; }
    for (let i = 0; i < n; i++) {
      if (!land[i]) continue;
      const x = i % nx, y = (i - x) / nx; let ox = 0, oy = 0, isC = false;
      for (let k = 0; k < 8; k++) {
        const xx = x + DX[k], yy = y + DY[k];
        if (xx < 0 || yy < 0 || xx >= nx || yy >= ny) continue;
        if (!land[yy * nx + xx]) { ox += DX[k]; oy += DY[k]; if (!(k & 1)) isC = true; }
      }
      if (!isC) continue;
      const ol = D.hyp(ox, oy) || 1, expo = exposure(ox / ol, oy / ol);
      coastNX[i] = ox / ol; coastNY[i] = oy / ol; coastEx[i] = expo;
      let hIn = 0; const rad = Math.max(1, D.round(300 / cell));
      for (let yy = Math.max(0, y - rad); yy <= Math.min(ny - 1, y + rad); yy++)
        for (let xx = Math.max(0, x - rad); xx <= Math.min(nx - 1, x + rad); xx++) { const j = yy * nx + xx; if (land[j]) hIn = Math.max(hIn, H[j]); }
      const slopeIn = hIn / (rad * cell);
      if (lava[i] > 0.6) coast[i] = hIn > 40 ? COAST_CLIFF : COAST_ROCKY;
      else if (hIn > 45 && (expo > 0.25 || slopeIn > 0.2)) coast[i] = COAST_CLIFF;
      else if (hIn < 14) coast[i] = COAST_BEACH;
      else if (hIn < BEACH_H && expo < BEACH_EX && seaNear(x, y) < BEACH_BAY) coast[i] = COAST_BEACH; // sand collects in sheltered bays
      else coast[i] = COAST_ROCKY;
    }
    for (let i = 0; i < n; i++) {
      if (land[i]) continue;
      const dc = distSea[i], x = (i % nx + 0.5) * cell, y = (Math.floor(i / nx) + 0.5) * cell;
      const nr = fbm(A.nReef, x / 2500, y / 2500, 3);
      if (dc > 1800) sea[i] = 1;
      else if (lava[i] < 0.45 && dc >= 300 && dc <= 1100 && nr > -0.05) sea[i] = 3;
      else if (lava[i] < 0.45 && dc < 300 && nr > 0.08) sea[i] = 4;
      else sea[i] = 2;
    }
    // Cliff mask for the chunk detail: cliff-coast land cells and the sea cells beside them.
    const cliffMask = new Float32Array(n);
    let seaCliffTop = 0, seaCliffCells = 0;
    for (let i = 0; i < n; i++) {
      if (coast[i] !== 3) continue;
      cliffMask[i] = 1; seaCliffCells++; seaCliffTop = Math.max(seaCliffTop, H[i]);
      const x = i % nx, y = (i - x) / nx;
      for (let k = 0; k < 8; k++) {
        const xx = x + DX[k], yy = y + DY[k];
        if (xx >= 0 && yy >= 0 && xx < nx && yy < ny && !land[yy * nx + xx]) cliffMask[yy * nx + xx] = 1;
      }
    }
    // Pali: wall cells (steep, inside the cut) and the tallest drop within 800 m.
    let paliCells = 0, paliDrop = 0;
    const pr = Math.max(1, Math.ceil(800 / cell));
    for (let i = 0; i < n; i++) {
      if (!land[i] || pali[i] < 0.3) continue;
      const x = i % nx, y = (i - x) / nx;
      const sx = (H[y * nx + Math.min(x + 1, nx - 1)] - H[y * nx + Math.max(x - 1, 0)]) / (2 * cell);
      const sy = (H[Math.min(y + 1, ny - 1) * nx + x] - H[Math.max(y - 1, 0) * nx + x]) / (2 * cell);
      if (D.hyp(sx, sy) < 0.47) continue; // 25 degrees
      paliCells++;
      let lo = H[i], hi = H[i];
      for (let yy = Math.max(0, y - pr); yy <= Math.min(ny - 1, y + pr); yy++)
        for (let xx = Math.max(0, x - pr); xx <= Math.min(nx - 1, x + pr); xx++) { const j = yy * nx + xx; if (land[j]) { if (H[j] < lo) lo = H[j]; if (H[j] > hi) hi = H[j]; } }
      paliDrop = Math.max(paliDrop, hi - lo);
    }
    lap('coast classes');
    if (p.stopAfter === 'shape') return { A, nx, ny, cell, H, H0, oldW, lava, erod, pali, ocean, land, distLand, distSea, filled, T, rain, lake, lakes, Q, toSea, isRiver, heads, rivers, falls, coast, coastNX, coastNY, coastEx, sea, cliffMask, cutKm2, streamLen, seaCliffTop, seaCliffCells, paliCells, paliDrop };

    // ---------- stage 3: climate, biomes, regions ----------
    say('Working out the climate');
    const temp = new Float32Array(n), fog = new Float32Array(n), wet = new Float32Array(n), crest = new Float32Array(n), slopeD = new Float32Array(n);
    const up = new Float64Array(n); // upslope area, for the wetness index
    for (let k = 0; k < T.ns; k++) up[T.stack[k]] = area;
    for (let k = T.ns - 1; k >= 0; k--) { const i = T.stack[k], r = T.rec[i]; if (r >= 0) up[r] += up[i]; }
    const blurH = new Float64Array(n); for (let i = 0; i < n; i++) blurH[i] = land[i] ? Math.max(0, H[i]) : 0;
    boxBlur(blurH, nx, ny, Math.max(1, D.round(450 / cell)), 2);
    const nearLake = new Uint8Array(n);
    for (let i = 0; i < n; i++) {
      if (isNaN(lake[i])) continue;
      const x = i % nx, y = (i - x) / nx;
      for (let k = 0; k < 8; k++) { const xx = x + DX[k], yy = y + DY[k]; if (xx >= 0 && yy >= 0 && xx < nx && yy < ny) nearLake[yy * nx + xx] = 1; }
    }
    let tMin = 99, tMax = -99;
    for (let i = 0; i < n; i++) {
      const x = i % nx, y = (i - x) / nx, hh = j => Math.max(0, H[j]);
      const sx = (hh(y * nx + Math.min(x + 1, nx - 1)) - hh(y * nx + Math.max(x - 1, 0))) / (2 * cell);
      const sy = (hh(Math.min(y + 1, ny - 1) * nx + x) - hh(Math.max(y - 1, 0) * nx + x)) / (2 * cell);
      const tan = D.hyp(sx, sy);
      slopeD[i] = D.atan(tan) * 180 / Math.PI;
      if (!land[i]) { temp[i] = p.seaTemp; continue; }
      const h = Math.max(0, isNaN(lake[i]) ? H[i] : lake[i]);
      temp[i] = p.seaTemp - 6.5 * h / 1000;  // lapse rate
      tMin = Math.min(tMin, temp[i]); tMax = Math.max(tMax, temp[i]);
      // the trade-wind cloud deck sits on wet slopes above the cloud base
      fog[i] = smoothstep(p.cloudBase - 200, p.cloudBase + 250, h) * clamp((rain[i] - 1500) / 2500, 0, 1);
      const twi = D.log(up[i] / cell / Math.max(tan, 0.004));
      let wv = clamp((twi - 8) / 5, 0, 1.3) * clamp(rain[i] / 1500, 0.45, 1.2) * p.wetlands;
      if (nearLake[i] && tan < 0.05) wv += 0.4;
      wet[i] = wv;
      crest[i] = clamp((H[i] - blurH[i]) / 60, 0, 1);
    }
    say('Sorting biomes and regions');
    const biome = new Uint8Array(n), region = new Uint8Array(n);
    const biomeKm2 = new Float64Array(BIOMES.length);
    const sg = A.saddle.seg[0];
    for (let i = 0; i < n; i++) {
      if (!land[i]) { biome[i] = B.ocean; continue; }
      const x = (i % nx + 0.5) * cell, y = (Math.floor(i / nx) + 0.5) * cell;
      if (!isNaN(lake[i])) biome[i] = B.lake;
      else {
        const jt = clamp(2 * fbm(A.nBiome, x / 2500, y / 2500, 3), -1, 1);
        biome[i] = classify(Math.max(0, H[i]), rain[i], temp[i], fog[i], wet[i], slopeD[i], lava[i], distLand[i], coast[i] === 1, crest[i], jt);
      }
      biomeKm2[biome[i]] += area / 1e6;
      // regions, highest priority first
      let rg;
      const t0 = ((x - sg.ax) * sg.dx + (y - sg.ay) * sg.dy) / sg.l2;
      const perp = Math.abs(sg.dx * (y - sg.ay) - sg.dy * (x - sg.ax)) / sg.len;
      if (D.hyp(x - A.caldera.x, y - A.caldera.y) < A.caldera.R * 1.15) rg = RG.crater;
      else if (D.hyp(x - A.vent.x, y - A.vent.y) < A.vent.R * 1.7 || lava[i] > 0.6) rg = RG.vent;
      else if (biome[i] === B.marsh) rg = RG.wetland;
      else if (wet[i] > 0.45 && H[i] < 25 && slopeD[i] < 4) rg = RG.wetland;
      else if (distLand[i] < 500 && H[i] < 40) rg = RG.coast;
      else if (H[i] > 0.55 * p.peak || (pali[i] > 0.3 && slopeD[i] > 20)) rg = RG.ridge;
      else if (t0 > 0.12 && t0 < 0.88 && perp < A.saddle.w * 0.8 && H[i] < p.saddle + 300) rg = RG.plateau;
      else if (rain[i] > 1800) rg = RG.windward;
      else rg = RG.leeward;
      region[i] = rg;
    }
    // smooth regions into readable areas (majority vote), keeping the crater and the vent
    {
      const rr = Math.max(1, D.round(256 / cell)), votes = new Int32Array(REGIONS.length), tmp = new Uint8Array(n);
      for (let pass = 0; pass < 2; pass++) {
        for (let i = 0; i < n; i++) {
          if (!land[i] || region[i] === RG.crater || region[i] === RG.vent) { tmp[i] = region[i]; continue; }
          votes.fill(0);
          const x = i % nx, y = (i - x) / nx;
          for (let yy = Math.max(0, y - rr); yy <= Math.min(ny - 1, y + rr); yy++)
            for (let xx = Math.max(0, x - rr); xx <= Math.min(nx - 1, x + rr); xx++) votes[region[yy * nx + xx]]++;
          votes[0] = 0; let best = region[i];
          for (let k = 1; k < votes.length; k++) if (votes[k] > votes[best]) best = k;
          tmp[i] = best;
        }
        region.set(tmp);
      }
    }
    const regionKm2 = new Float64Array(REGIONS.length), regionLabels = [];
    {
      const lab2 = new Int32Array(n);
      for (let r = 1; r < REGIONS.length; r++) {
        const mask = new Uint8Array(n); let any = false;
        for (let i = 0; i < n; i++) if (region[i] === r) { mask[i] = 1; any = true; regionKm2[r] += area / 1e6; }
        if (!any) continue;
        const sizes = components(mask, nx, ny, lab2);
        const order = sizes.map((s, k) => [s, k]).sort((a, b) => b[0] - a[0]).slice(0, 3);
        const sx = new Float64Array(sizes.length), sy = new Float64Array(sizes.length);
        for (let i = 0; i < n; i++) { const l = lab2[i]; if (l >= 0) { sx[l] += i % nx; sy[l] += Math.floor(i / nx); } }
        order.forEach(([sz, k], idx) => {
          const km2 = sz * area / 1e6;
          if (idx === 0 ? km2 >= 1 : km2 >= 40) regionLabels.push({ region: r, name: REGIONS[r].name, x: (sx[k] / sz + 0.5) * cell, y: (sy[k] / sz + 0.5) * cell, km2 });
        });
      }
    }
    lap('climate and biomes');
    if (p.stopAfter === 'climate') return { temp, fog, wet, crest, slopeD, nearLake, biome, region, biomeKm2, regionKm2, regionLabels, tMin, tMax };

    // ---------- stage 4: the works of people, and caves ----------
    const W4 = works({ p, A, nx, ny, cell, n, area, H, H0, land, lake, rain, slopeD, biome, region, distLand, distSea, coast,
      coastNX, coastNY, coastEx, sea, Q, isRiver, lava, oldW, crest, wet, say, lap });
    if (W4.stop) return W4.stop;

    // ---------- metrics ----------
    const lbl = new Int32Array(n), lsz = components(land, nx, ny, lbl);
    let mainId = 0; lsz.forEach((s, k) => { if (s > lsz[mainId]) mainId = k; });
    let landCells = 0, hmax = -1e9, hsum = 0, peakX = 0, peakY = 0;
    const slopeBins = [0, 0, 0, 0]; let walk = 0, rainMin = 1e9, rainMax = 0;
    let pMin = 1e30, pMax = -1e30;
    let beach = 0, rocky = 0, cliff = 0;
    for (let i = 0; i < n; i++) {
      if (!land[i]) continue;
      landCells++; hsum += H[i];
      const x = i % nx, y = (i - x) / nx;
      if (H[i] > hmax) { hmax = H[i]; peakX = (x + 0.5) * cell; peakY = (y + 0.5) * cell; }
      const hx = (H[Math.min(i + 1, n - 1)] - H[Math.max(i - 1, 0)]) / (2 * cell), hy = (H[Math.min(i + nx, n - 1)] - H[Math.max(i - nx, 0)]) / (2 * cell);
      const deg = D.atan(D.hyp(hx, hy)) * 180 / Math.PI;
      slopeBins[deg < 5 ? 0 : deg < 15 ? 1 : deg < 30 ? 2 : 3]++;
      if (deg < 25) walk++;
      rainMin = Math.min(rainMin, rain[i]); rainMax = Math.max(rainMax, rain[i]);
      if (lbl[i] === mainId) { const pr = (x + 0.5) * cell * A.d[0] + (y + 0.5) * cell * A.d[1]; pMin = Math.min(pMin, pr); pMax = Math.max(pMax, pr); }
      if (coast[i] === 1) beach++; else if (coast[i] === 2) rocky++; else if (coast[i] === 3) cliff++;
    }
    const allIslets = lsz.filter((s, k) => k !== mainId).map(s => s * area / 1e6).sort((a, b) => b - a);
    const isletSizes = allIslets.filter(v => v >= 0.1), stacks = allIslets.length - isletSizes.length;
    let mouths = 0, maxQ = 0, longest = 0;
    for (const h0 of heads) longest = Math.max(longest, toSea[h0]);
    for (let i = 0; i < n; i++) if (isRiver[i]) { maxQ = Math.max(maxQ, Q[i]); if (rec[i] === -1) mouths++; }
    const coastCells = beach + rocky + cliff;
    const lengthKm = (pMax - pMin) / 1000;
    const metrics = {
      frameKm: [A.W / 1000, A.H / 1000], grid: [nx, ny], cell,
      chunks: D.round(landCells * area / (CHUNK * CHUNK)),
      landKm2: landCells * area / 1e6, mainKm2: lsz[mainId] * area / 1e6, landShare: landCells / n,
      islets: isletSizes.length, isletKm2: isletSizes, stacks,
      peakM: hmax, peakAt: [peakX, peakY], meanM: hsum / landCells,
      slope: slopeBins.map(v => v / landCells), walkable: walk / landCells,
      rainMin, rainMax,
      mouths, longestKm: longest / 1000, maxQ, streamKm: streamLen / 1000, falls: falls.filter(f => f.river).length, cascades: falls.filter(f => !f.river).length, tallestFall: falls.reduce((a, f) => Math.max(a, f.drop), 0),
      lakes: lakes.length, lakeKm2: lakes.reduce((a, L) => a + L.area, 0), deepestLake: lakes.reduce((a, L) => Math.max(a, L.depth), 0),
      coastShare: coastCells ? [beach / coastCells, rocky / coastCells, cliff / coastCells] : [0, 0, 0],
      lengthKm, walkHours: lengthKm * 1000 / 1.4 / 3600,
      paliKm: paliCells * cell / Math.max(cell, 420 * Math.sqrt(A.s)) * cell / 1000, paliDrop,
      seaCliffKm: seaCliffCells * cell / 1000, seaCliffTop, cutKm2,
      biomeKm2: Array.from(biomeKm2), regionKm2: Array.from(regionKm2), tMin, tMax
    };
    lap('metrics');
    say('Done');

    return {
      params: p, nx, ny, cell, W: A.W, H: A.H, s: A.s,
      height: Float32Array.from(H), height0: H0, rain: Float32Array.from(rain), flow: Float32Array.from(Q),
      lake, sea, coast, lava, erod, distLand, distSea, land, pali, cliff: cliffMask,
      temp, fog, wet, crest, slope: slopeD, biome, region, regionLabels, works: W4,
      rivers, falls, lakes, metrics, timing,
      anchors: {
        d: A.d, n: A.n,
        ridges: [{ name: A.old.name, xs: A.old.xs, ys: A.old.ys }, { name: A.young.name, xs: A.young.xs, ys: A.young.ys }],
        caldera: A.caldera, vent: A.vent, islets: A.islets
      },
      noise: { tile: A.nTile, fine: A.nTile2, cover: A.nCover }
    };
  }

  function transferables(w) {
    const list = [w.height.buffer, w.height0.buffer, w.rain.buffer, w.flow.buffer, w.lake.buffer, w.sea.buffer,
      w.coast.buffer, w.lava.buffer, w.erod.buffer, w.distLand.buffer, w.distSea.buffer, w.land.buffer, w.pali.buffer, w.cliff.buffer,
      w.temp.buffer, w.fog.buffer, w.wet.buffer, w.crest.buffer, w.slope.buffer, w.biome.buffer, w.region.buffer];
    for (const r of w.rivers) list.push(r.xs.buffer, r.ys.buffer, r.qs.buffer, r.hs.buffer);
    for (const r of w.works.roads) list.push(r.xs.buffer, r.ys.buffer, r.hs.buffer, r.flags.buffer);
    return list;
  }

  // ---------- chunk detail: one 64 x 64 m chunk at 1 m tiles ----------
  // Inputs are the island-scale grids and the river polylines, sampled at global coordinates,
  // so neighbouring chunks agree along their edges.
  const COVER = [
    { key: 'deep', name: 'Deep water', g: '≈', fg: '#4f86b8', bg: '#0f2a40' },
    { key: 'shallow', name: 'Shallow water', g: '~', fg: '#7fc0d8', bg: '#1f5068' },
    { key: 'river', name: 'River', g: '~', fg: '#a8dcf2', bg: '#2a6688' },
    { key: 'lake', name: 'Lake', g: '~', fg: '#8fcde6', bg: '#24607e' },
    { key: 'reef', name: 'Reef flat', g: '°', fg: '#f0b8a0', bg: '#2f6f78' },
    { key: 'beach', name: 'Beach sand', g: '·', fg: '#e8d7a6', bg: '#9c8a5c' },
    { key: 'grass', name: 'Grass', g: ".'", fg: '#a8c46c', bg: '#4b5e2c' },
    { key: 'scrub', name: 'Dry scrub', g: ',;', fg: '#c2ad6a', bg: '#5e5432' },
    { key: 'sparse', name: 'Sparse jungle', g: '"τ', fg: '#78b85a', bg: '#2f4d24' },
    { key: 'dense', name: 'Dense jungle', g: '&♣♠', fg: '#4fa04a', bg: '#1d3a1c' },
    { key: 'cloud', name: 'Cloud forest', g: 'Ψ♣', fg: '#8fc7a0', bg: '#264438' },
    { key: 'rock', name: 'Bare rock, cliff', g: '^∩', fg: '#b7b2a6', bg: '#55524c' },
    { key: 'lava', name: 'Young lava', g: '%', fg: '#8a7f78', bg: '#2a2624' },
    { key: 'mud', name: 'Mud bank', g: '∴', fg: '#a98e66', bg: '#4a3c2a' },
    { key: 'tallgrass', name: 'Tall grass', g: 'ⁿ"', fg: '#c9cf72', bg: '#55602c' },
    { key: 'fern', name: 'Ferns', g: 'ψ', fg: '#7fcf8a', bg: '#21402a' },
    { key: 'reeds', name: 'Reeds', g: '¦', fg: '#9fc4a0', bg: '#2f4a3c' },
    { key: 'boulder', name: 'Boulders', g: '●o', fg: '#c4beb2', bg: '#4d4a44' },
    { key: 'road', name: 'Road', g: '=', fg: '#d8cfb8', bg: '#5b5446' },
    { key: 'bridge', name: 'Bridge', g: '═', fg: '#e6d9b8', bg: '#6a5a40' },
    { key: 'runway', name: 'Runway', g: '·', fg: '#e8e6df', bg: '#55575a' },
    { key: 'yard', name: 'Compound yard', g: '∙', fg: '#b9b6ad', bg: '#4a4946' },
    { key: 'wall', name: 'Wall', g: '#', fg: '#ece6da', bg: '#6d6a64' },
    { key: 'floor', name: 'Floor', g: '_', fg: '#a7a294', bg: '#3c3a36' },
    { key: 'door', name: 'Door', g: '+', fg: '#f2c46a', bg: '#3c3a36' },
    { key: 'fence', name: 'Fence', g: '‡', fg: '#e0e4ea', bg: '#3d4a3c' },
    { key: 'pylon', name: 'Power pylon', g: '╫', fg: '#f0d36a', bg: '#3f3d34' },
    { key: 'ruin', name: 'Ruined wall', g: '▓', fg: '#b8a98a', bg: '#4e463a' },
    { key: 'rubble', name: 'Rubble', g: 'x', fg: '#a69a84', bg: '#3e382f' },
    { key: 'wreck', name: 'Wreckage', g: '▒', fg: '#c98a5a', bg: '#4a3326' },
    { key: 'cave', name: 'Cave opening', g: 'Ω', fg: '#9b8fb8', bg: '#121014' },
    { key: 'pier', name: 'Pier', g: '≡', fg: '#d9c39a', bg: '#5a4a34' },
    { key: 'rack', name: 'Racks and shelving', g: '▐', fg: '#9fb4c8', bg: '#3c3a36' },
    { key: 'console', name: 'Consoles', g: '◙', fg: '#8fe0c0', bg: '#3c3a36' },
    { key: 'furniture', name: 'Tables and bunks', g: 'π', fg: '#c8b48e', bg: '#3c3a36' },
    { key: 'tank', name: 'Tanks and machinery', g: 'Ø', fg: '#c9ccd2', bg: '#45474b' },
    { key: 'helipad', name: 'Helipad', g: '⊕', fg: '#f2d36b', bg: '#4a4946' },
    { key: 'bigwall', name: 'Reinforced wall', g: '█', fg: '#d8d4cc', bg: '#7a766e' },
    { key: 'rail', name: 'Tram rails', g: '╪', fg: '#d0c8b8', bg: '#4c4840' },
    { key: 'moat', name: 'Moat (engineered drop)', g: '∨', fg: '#8a7a64', bg: '#2a241c' },
    { key: 'mast', name: 'Mast or tower', g: '╬', fg: '#f0d36a', bg: '#3f3d34' },
    { key: 'elevator', name: 'Elevator', g: '⇕', fg: '#f2c46a', bg: '#2e2a24' }
  ];
  // Ground cover inside each biome, ordered from open to closed ground so patch noise walks
  // through it in sensible order (grass, then tall grass, then scrub, then trees).
  const MIX = {
    strand: [['beach', 0.8], ['grass', 0.08], ['scrub', 0.12]],
    marsh: [['shallow', 0.12], ['mud', 0.16], ['reeds', 0.44], ['tallgrass', 0.28]],
    dryscrub: [['boulder', 0.05], ['grass', 0.25], ['scrub', 0.52], ['sparse', 0.18]],
    grassland: [['grass', 0.5], ['tallgrass', 0.3], ['scrub', 0.1], ['sparse', 0.1]],
    mesic: [['grass', 0.2], ['scrub', 0.14], ['sparse', 0.42], ['dense', 0.24]],
    moist: [['sparse', 0.3], ['fern', 0.12], ['dense', 0.58]],
    wet: [['sparse', 0.08], ['fern', 0.22], ['dense', 0.7]],
    cloud: [['fern', 0.38], ['cloud', 0.62]],
    ridge: [['rock', 0.18], ['grass', 0.15], ['scrub', 0.42], ['fern', 0.25]],
    cliff: [['rock', 0.72], ['boulder', 0.14], ['fern', 0.14]],
    lava: [['lava', 0.86], ['scrub', 0.08], ['fern', 0.06]],
    lake: [['lake', 1]], ocean: [['shallow', 1]]
  };
  const CV = {}; COVER.forEach((c, k) => { CV[c.key] = k; });
  const MIXI = BIOMES.map(b => { const m = MIX[b.key], out = []; let acc = 0; for (const [k, w] of m) { acc += w; out.push([CV[k], acc]); } return out; });
  const MK = { road: 1, bridge: 2, runway: 3, yard: 4, wall: 5, floor: 6, door: 7, fence: 8, pylon: 9, ruin: 10, rubble: 11, wreck: 12, cave: 13, pier: 14,
    rack: 15, console: 16, furniture: 17, tank: 18, helipad: 19, bigwall: 20, rail: 21, moat: 22, mast: 23, mud: 24, shallow: 25, elevator: 26 };
  const MKC = []; for (const k in MK) MKC[MK[k]] = CV[k];
  function pickCover(bi, u) { const m = MIXI[bi]; for (let k = 0; k < m.length; k++) if (u < m[k][1]) return m[k][0]; return m[m.length - 1][0]; }

  function cr(p0, p1, p2, p3, t) { return p1 + 0.5 * t * (p2 - p0 + t * (2 * p0 - 5 * p1 + 4 * p2 - p3 + t * (3 * (p1 - p2) + p3 - p0))); }
  function bicubic(F, nx, ny, gx, gy) {
    const ix = Math.floor(gx), iy = Math.floor(gy), fx = gx - ix, fy = gy - iy, c = [0, 0, 0, 0];
    for (let m = -1; m <= 2; m++) {
      const row = clamp(iy + m, 0, ny - 1) * nx;
      c[m + 1] = cr(F[row + clamp(ix - 1, 0, nx - 1)], F[row + clamp(ix, 0, nx - 1)], F[row + clamp(ix + 1, 0, nx - 1)], F[row + clamp(ix + 2, 0, nx - 1)], fx);
    }
    return cr(c[0], c[1], c[2], c[3], fy);
  }
  function bilinear(F, nx, ny, gx, gy) {
    const ix = clamp(Math.floor(gx), 0, nx - 2), iy = clamp(Math.floor(gy), 0, ny - 2), fx = clamp(gx - ix, 0, 1), fy = clamp(gy - iy, 0, 1), i = iy * nx + ix;
    return (F[i] * (1 - fx) + F[i + 1] * fx) * (1 - fy) + (F[i + nx] * (1 - fx) + F[i + nx + 1] * fx) * fy;
  }

  const BIN = 256;
  function indexRivers(world) {
    const bins = new Map();
    world.rivers.forEach((r, ri) => {
      for (let k = 0; k + 1 < r.xs.length; k++) {
        const x0 = Math.min(r.xs[k], r.xs[k + 1]), x1 = Math.max(r.xs[k], r.xs[k + 1]);
        const y0 = Math.min(r.ys[k], r.ys[k + 1]), y1 = Math.max(r.ys[k], r.ys[k + 1]);
        for (let by = Math.floor(y0 / BIN); by <= Math.floor(y1 / BIN); by++)
          for (let bx = Math.floor(x0 / BIN); bx <= Math.floor(x1 / BIN); bx++) {
            const key = bx * 4096 + by; let L = bins.get(key); if (!L) bins.set(key, L = []); L.push(ri, k);
          }
      }
    });
    return bins;
  }
  function nearestRiver(world, bins, x, y, out) {
    out.d = 1e9;
    const bx = Math.floor(x / BIN), by = Math.floor(y / BIN);
    for (let yy = by - 1; yy <= by + 1; yy++) for (let xx = bx - 1; xx <= bx + 1; xx++) {
      const L = bins.get(xx * 4096 + yy); if (!L) continue;
      for (let m = 0; m < L.length; m += 2) {
        const r = world.rivers[L[m]], k = L[m + 1];
        const ax = r.xs[k], ay = r.ys[k], dx = r.xs[k + 1] - ax, dy = r.ys[k + 1] - ay, l2 = dx * dx + dy * dy || 1;
        let t = ((x - ax) * dx + (y - ay) * dy) / l2; t = t < 0 ? 0 : t > 1 ? 1 : t;
        const d = D.hyp(ax + dx * t - x, ay + dy * t - y);
        if (d < out.d) { out.d = d; out.q = r.qs[k] + (r.qs[k + 1] - r.qs[k]) * t; out.h = r.hs[k] + (r.hs[k + 1] - r.hs[k]) * t; }
      }
    }
    return out;
  }

  // Spatial index of everything people built (and caves, wrecks, ruins), in 256 m bins.
  function indexWorks(world) {
    const W = world.works, bins = new Map(), put = (x0, y0, x1, y1, item) => {
      for (let by = Math.floor(y0 / BIN); by <= Math.floor(y1 / BIN); by++)
        for (let bx = Math.floor(x0 / BIN); bx <= Math.floor(x1 / BIN); bx++) { const key = bx * 4096 + by; let L = bins.get(key); if (!L) bins.set(key, L = []); L.push(item); }
    };
    const seg = (ax, ay, bx, by, pad, item) => put(Math.min(ax, bx) - pad, Math.min(ay, by) - pad, Math.max(ax, bx) + pad, Math.max(ay, by) + pad, item);
    const ways = W.roads.map(r => ({ xs: r.xs, ys: r.ys, hs: r.hs, flags: r.flags, w: r.cls === 'main' ? 7 : 4 }));
    for (const t of W.sites) if (t.runway) ways.push({ xs: [t.runway.x1, t.runway.x2], ys: [t.runway.y1, t.runway.y2], hs: [t.runway.h, t.runway.h], flags: [0, 0], w: 45, runway: true });
    ways.forEach(r => { for (let k = 0; k + 1 < r.xs.length; k++) seg(r.xs[k], r.ys[k], r.xs[k + 1], r.ys[k + 1], r.w / 2 + 12, { t: 'way', r, k }); });
    for (const q of W.paddocks) for (let v = 0; v < q.poly.length; v += 2) {
      const a = v, b = (v + 2) % q.poly.length;
      seg(q.poly[a], q.poly[a + 1], q.poly[b], q.poly[b + 1], 2, { t: 'fence', ax: q.poly[a], ay: q.poly[a + 1], bx: q.poly[b], by: q.poly[b + 1], gate: q.gate });
    }
    for (const l of W.power) {
      const L = D.hyp(l.x2 - l.x1, l.y2 - l.y1), nP = Math.floor(L / 250);
      for (let k = 1; k < nP; k++) { const x = l.x1 + (l.x2 - l.x1) * k / nP, y = l.y1 + (l.y2 - l.y1) * k / nP; put(x - 3, y - 3, x + 3, y + 3, { t: 'pylon', x, y }); }
    }
    for (const q of W.paddocks) if (q.inner) for (let v = 0; v < q.inner.length; v += 2) {
      const a = v, b = (v + 2) % q.inner.length;
      seg(q.inner[a], q.inner[a + 1], q.inner[b], q.inner[b + 1], 2, { t: 'fence', ax: q.inner[a], ay: q.inner[a + 1], bx: q.inner[b], by: q.inner[b + 1], gate: q.gate, gap: 9 });
    }
    const T = W.transit;
    if (T && T.tram) for (let k = 0; k + 1 < T.tram.xs.length; k++) seg(T.tram.xs[k], T.tram.ys[k], T.tram.xs[k + 1], T.tram.ys[k + 1], 3, { t: 'rail', ax: T.tram.xs[k], ay: T.tram.ys[k], bx: T.tram.xs[k + 1], by: T.tram.ys[k + 1] });
    for (const t of W.sites) put(t.x - t.r - 40, t.y - t.r - 40, t.x + t.r + 40, t.y + t.r + 40, { t: 'site', s: t });
    // buildings and furniture are dense, so they get their own 32 m bins
    const pbins = new Map(), PB = 32, pput = (x0, y0, x1, y1, item) => {
      for (let by = Math.floor(y0 / PB); by <= Math.floor(y1 / PB); by++)
        for (let bx = Math.floor(x0 / PB); bx <= Math.floor(x1 / PB); bx++) { const key = bx * 8192 + by; let L = pbins.get(key); if (!L) pbins.set(key, L = []); L.push(item); }
    };
    for (const q of W.prims || []) {
      const it = { t: 'prim', p: q }, put = pput, seg = (ax, ay, bx, by, pad, item) => pput(Math.min(ax, bx) - pad, Math.min(ay, by) - pad, Math.max(ax, bx) + pad, Math.max(ay, by) + pad, item);
      if (q.t === 'circle') put(q.x - q.r - 1, q.y - q.r - 1, q.x + q.r + 1, q.y + q.r + 1, it);
      else if (q.t === 'tower') put(q.x - 2, q.y - 2, q.x + 2, q.y + 2, it);
      else if (q.t === 'line') seg(q.ax, q.ay, q.bx, q.by, q.w, it);
      else put(q.x0 - 1, q.y0 - 1, q.x1 + 1, q.y1 + 1, it);
    }
    bins.prims = pbins;
    for (const t of W.sites) if (t.kind === 'docks' && t.dir) { const L = t.r + 90; seg(t.x, t.y, t.x + t.dir[0] * L, t.y + t.dir[1] * L, 6, { t: 'pier', s: t, L }); }
    for (const q of W.ruins) put(q.x - q.r - 3, q.y - q.r - 3, q.x + q.r + 3, q.y + q.r + 3, { t: 'ruin', q });
    for (const q of W.wrecks) put(q.x - q.length, q.y - q.length, q.x + q.length, q.y + q.length, { t: 'wreck', q });
    for (const c of W.caves) {
      for (const m of c.mouths) put(m[0] - 5, m[1] - 5, m[0] + 5, m[1] + 5, { t: 'cave', x: m[0], y: m[1], r: 3.5 });
      for (const m of c.skylights) put(m[0] - 4, m[1] - 4, m[0] + 4, m[1] + 4, { t: 'cave', x: m[0], y: m[1], r: 2.5, sky: true });
    }
    return bins;
  }
  function segDist(x, y, ax, ay, bx, by, out) {
    const dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy || 1;
    let t = ((x - ax) * dx + (y - ay) * dy) / l2; t = t < 0 ? 0 : t > 1 ? 1 : t;
    out.t = t; return D.hyp(ax + dx * t - x, ay + dy * t - y);
  }

  function chunk(world, bins, ccx, ccy, size) {
    size = size || CHUNK;
    const { nx, ny, cell } = world, M = size + 2, x0 = ccx * CHUNK - 1, y0 = ccy * CHUNK - 1;
    const h = new Float32Array(M * M), water = new Float32Array(M * M).fill(NaN), riverD = new Float32Array(M * M).fill(1e9);
    const kind = new Int8Array(M * M); // 0 land, 1 sea, 2 river, 3 lake
    const nz = world.noise, rv = {}, so = {}, mark = new Uint8Array(M * M), lab = new Int16Array(M * M).fill(-1);
    const WB = world.works ? (world._wi || (world._wi = indexWorks(world))) : null;
    for (let j = 0; j < M; j++) for (let i = 0; i < M; i++) {
      const k = j * M + i, x = x0 + i + 0.5, y = y0 + j + 0.5, gx = x / cell - 0.5, gy = y / cell - 0.5;
      let z = bicubic(world.height, nx, ny, gx, gy);
      const det = 1.3 * fbm(nz.tile, x / 32, y / 32, 3) + 0.35 * noise(nz.fine, x / 5, y / 5);
      z += z > 0 ? det : det * 0.3;
      // sea cliffs: the coastline is a jagged line through the cliff cells; land behind it stands at the
      // cliff-top height a few metres in, the sea in front drops to a wave-cut bench
      const cm = bilinear(world.cliff, nx, ny, gx, gy);
      if (cm > 0.12) {
        const sdc = bilinear(world.distLand, nx, ny, gx, gy) - bilinear(world.distSea, nx, ny, gx, gy) + 20 * fbm((nz.fine ^ 0x3c3c) >>> 0, x / 28, y / 28, 3);
        const bx = clamp(Math.floor(gx), 0, nx - 2), by = clamp(Math.floor(gy), 0, ny - 2);
        let top = 0;
        for (let b = 0; b <= 1; b++) for (let a = 0; a <= 1; a++) { const c = (by + b) * nx + bx + a; if (world.land[c]) top = Math.max(top, world.height[c]); }
        const wgt = Math.min(1, cm * 1.6);
        const zc = sdc > 0 ? (z > top ? z : Math.max(0.6, top * smoothstep(0, 6, sdc) + det * 0.4)) : Math.min(z, -1.2 + sdc * 0.06);
        z = z + (zc - z) * wgt;
      }
      if (z < 0) { kind[k] = 1; water[k] = 0; }
      nearestRiver(world, bins, x, y, rv);
      if (rv.d < 60) {
        const w = riverWidth(rv.q), dep = riverDepth(rv.q), half = w / 2, bank = 2 + w * 0.6;
        if (rv.d < half) { z = Math.min(z, rv.h - dep); kind[k] = 2; water[k] = rv.h; }
        else if (rv.d < half + bank * 3) z = Math.min(z, rv.h + (rv.d - half) * 0.45);
        riverD[k] = rv.d - half;
      }
      // lakes: the nearest lake cells set a water level; terrain below it floods
      const ix = Math.floor(gx), iy = Math.floor(gy);
      let lv = NaN;
      for (let b = 0; b <= 1; b++) for (let a = 0; a <= 1; a++) {
        const cx2 = clamp(ix + a, 0, nx - 1), cy2 = clamp(iy + b, 0, ny - 1), L = world.lake[cy2 * nx + cx2];
        if (!isNaN(L) && !(L <= lv)) lv = L;
      }
      if (!isNaN(lv) && z < lv && kind[k] !== 1) { kind[k] = 3; water[k] = lv; }
      // ---- the works: what people built (and caves, wrecks, ruins) ----
      if (WB) {
        const L = WB.get(Math.floor(x / BIN) * 4096 + Math.floor(y / BIN));
        if (L) {
          let bestW = 1e9, wR = null, wK = 0, wT = 0;
          for (const it of L) {
            if (it.t === 'way') { const d = segDist(x, y, it.r.xs[it.k], it.r.ys[it.k], it.r.xs[it.k + 1], it.r.ys[it.k + 1], so); if (d < bestW) { bestW = d; wR = it.r; wK = it.k; wT = so.t; } }
          }
          if (wR) { // roads: cut and fill to the road surface, bridges over water, tunnels left underground
            const half = wR.w / 2, fl = wR.flags[wK] | wR.flags[wK + 1], hr = wR.hs[wK] + (wR.hs[wK + 1] - wR.hs[wK]) * wT;
            if (!(fl & 2) && bestW < half + 12) {
              if (kind[k] === 2 || kind[k] === 3 || (kind[k] === 1 && !wR.runway)) { if (bestW < half) mark[k] = MK.bridge; }
              else if (bestW < half) { z = hr; mark[k] = wR.runway ? MK.runway : MK.road; }
              else { const tt = smoothstep(0, 1, (bestW - half) / 12); z = hr + (z - hr) * tt; }
            }
          }
          for (const it of L) {
            if (it.t === 'site') {
              const t = it.s, dx = x - t.x, dy = y - t.y, dch = Math.max(Math.abs(dx), Math.abs(dy));
              if (dch < t.r + 30) {
                const wgt = dch <= t.r + 2 ? 1 : 1 - (dch - t.r - 2) / 28;
                if (kind[k] !== 1 || dch <= t.r) { z = z + (t.h - z) * wgt; if (dch <= t.r) { kind[k] = 0; water[k] = NaN; } }
                if (dch <= t.r + 0.5) {
                  const along = t.gate === 'n' || t.gate === 's' ? dx : dy;
                  const onSide = t.gate === 'n' ? dy < 0 : t.gate === 's' ? dy > 0 : t.gate === 'e' ? dx > 0 : dx < 0;
                  mark[k] = dch > t.r - 0.5 && !(onSide && Math.abs(along) < 4 && Math.abs(Math.abs(onSide && (t.gate === 'n' || t.gate === 's') ? dy : dx) - t.r) < 1) ? MK.fence : MK.yard;
                }
              }
            } else if (it.t === 'pier') {
              const t = it.s, d = segDist(x, y, t.x, t.y, t.x + t.dir[0] * it.L, t.y + t.dir[1] * it.L, so);
              if (d < 3 && so.t * it.L > t.r * 0.4) { if (kind[k] === 1) { kind[k] = 0; water[k] = NaN; } z = Math.max(z, 1.6); mark[k] = MK.pier; }
            }
          }
          const ix = Math.floor(x), iy = Math.floor(y);
          const LP = WB.prims.get(Math.floor(x / 32) * 8192 + Math.floor(y / 32)) || [];
          for (const it of L) if (it.t === 'rail') { if (segDist(x, y, it.ax, it.ay, it.bx, it.by, so) < 1.6 && kind[k] !== 1) mark[k] = MK.rail; }
          for (const it of LP) {
            if (it.t === 'rail') { if (segDist(x, y, it.ax, it.ay, it.bx, it.by, so) < 1.6 && kind[k] !== 1) mark[k] = MK.rail; continue; }
            if (it.t !== 'prim') continue;
            const q = it.p;
            if (q.t === 'bld') {
              if (ix < q.x0 || ix > q.x1 || iy < q.y0 || iy > q.y1) continue;
              kind[k] = 0; water[k] = NaN;
              let m = MK.floor, lb = q.label;
              if (ix === q.x0 || ix === q.x1 || iy === q.y0 || iy === q.y1) m = MK.wall;
              else for (const rm of q.rooms) if (ix >= rm.x0 && ix <= rm.x1 && iy >= rm.y0 && iy <= rm.y1) { lb = rm.label; if (ix === rm.x0 || ix === rm.x1 || iy === rm.y0 || iy === rm.y1) m = MK.wall; }
              for (const d of q.doors) if (d[0] === ix && d[1] === iy) m = MK.door;
              mark[k] = m; lab[k] = lb;
            } else if (q.t === 'fur') {
              if (ix >= q.x0 && ix <= q.x1 && iy >= q.y0 && iy <= q.y1 && mark[k] === MK.floor) { mark[k] = MK[q.cover]; }
            } else if (q.t === 'circle') {
              const d = D.hyp(x - q.x, y - q.y);
              if (d < q.r + 0.75) {
                if (q.ring) { if (Math.abs(d - q.r) < 0.75) mark[k] = q.door && y > q.y && Math.abs(x - q.x) < 1 ? MK.door : MK[q.cover]; else if (d < q.r) mark[k] = MK[q.fill]; }
                else if (d < q.r) mark[k] = MK[q.cover];
                if (d < q.r + 0.75) { lab[k] = q.label; if (kind[k] === 1 && q.cover !== 'shallow') { kind[k] = 0; water[k] = NaN; } }
              }
            } else if (q.t === 'rect') {
              if (ix >= q.x0 && ix <= q.x1 && iy >= q.y0 && iy <= q.y1) { mark[k] = q.fence && (ix === q.x0 || ix === q.x1 || iy === q.y0 || iy === q.y1) ? MK.fence : MK[q.cover]; lab[k] = q.label; }
            } else if (q.t === 'tower') {
              if (Math.abs(x - q.x) < 1.5 && Math.abs(y - q.y) < 1.5) { mark[k] = MK.mast; lab[k] = q.label; }
            } else if (q.t === 'line') {
              const d = segDist(x, y, q.ax, q.ay, q.bx, q.by, so), hw = q.w / 2;
              if (d < hw && kind[k] === 0) { const f = d / hw; z -= q.depth * (1 - f * f); mark[k] = MK[q.cover]; lab[k] = q.label; }
            } else if (q.t === 'pen') {
              if (ix < q.x0 || ix > q.x1 || iy < q.y0 || iy > q.y1) continue;
              const e = Math.min(ix - q.x0, q.x1 - ix, iy - q.y0, q.y1 - iy), mid = Math.abs(ix - (q.x0 + q.x1) / 2) < 1.5 && iy > (q.y0 + q.y1) / 2;
              if (e < 2) mark[k] = mid ? MK.door : MK.bigwall;
              else if (e === 5) mark[k] = mid ? 0 : MK.fence;
              else if (mark[k] === MK.yard) mark[k] = 0; // planted inside: the biome shows through
              lab[k] = q.label;
            }
          }
          for (const it of L) {
            if (it.t === 'fence') {
              if (segDist(x, y, it.ax, it.ay, it.bx, it.by, so) < 0.55 && !(it.gate && D.hyp(x - it.gate[0], y - it.gate[1]) < (it.gap || 2.5)) && kind[k] === 0 && mark[k] !== MK.road) mark[k] = MK.fence;
            } else if (it.t === 'pylon') {
              if (Math.abs(x - it.x) < 1.1 && Math.abs(y - it.y) < 1.1 && kind[k] === 0) mark[k] = MK.pylon;
            } else if (it.t === 'ruin') {
              const q = it.q, c = D.cos(q.rot), sn = D.sin(q.rot), u = (x - q.x) * c + (y - q.y) * sn, v = -(x - q.x) * sn + (y - q.y) * c;
              const gap = hash2(nz.cover, Math.floor(x), Math.floor(y)) / 4294967296;
              if (kind[k] !== 0) continue;
              if (q.kind === 'stones') {
                const rr = D.hyp(u, v), a = D.atan2(v, u), sa = D.round(a / (Math.PI / 8)) * (Math.PI / 8);
                if (D.hyp(u - D.cos(sa) * q.r, v - D.sin(sa) * q.r) < 0.9) mark[k] = MK.ruin;
                else if (rr < q.r * 0.25 && gap < 0.3) mark[k] = MK.rubble;
              } else if (q.kind === 'terraces') {
                if (D.hyp(u, v) < q.r && ((v % 6) + 6) % 6 < 0.8 && gap > 0.15) mark[k] = MK.ruin;
              } else if (q.kind === 'lighthouse') {
                const rr = D.hyp(u, v); if (rr < 3.6) mark[k] = rr > 2.6 ? (gap > 0.2 ? MK.ruin : MK.rubble) : MK.floor;
              } else {
                const m = Math.max(Math.abs(u), Math.abs(v));
                if (m < q.r + 0.4) mark[k] = m > q.r - 0.6 ? (gap > 0.3 ? MK.ruin : MK.rubble) : (gap < 0.25 ? MK.rubble : mark[k]);
              }
            } else if (it.t === 'wreck') {
              const q = it.q, c = D.cos(q.heading), sn = D.sin(q.heading), u = (x - q.x) * c + (y - q.y) * sn, v = -(x - q.x) * sn + (y - q.y) * c;
              const a = q.length / 2, b = Math.max(2, q.length / 7), e = (u / a) * (u / a) + (v / b) * (v / b);
              const gap = hash2(nz.fine, Math.floor(x), Math.floor(y)) / 4294967296;
              if ((e > 0.78 && e < 1.08 && gap > 0.18) || (e <= 0.78 && gap < 0.3) || (q.inland && Math.abs(u) < 1.2 && Math.abs(v) < q.length * 0.55 && gap > 0.25)) {
                if (kind[k] === 1 && z > -6) { z = Math.max(z, -0.4); } mark[k] = MK.wreck;
              }
            } else if (it.t === 'cave') {
              const d = D.hyp(x - it.x, y - it.y);
              if (d < it.r && kind[k] === 0) { mark[k] = MK.cave; if (it.sky) z -= 4 * (1 - d / it.r); }
            }
          }
        }
      }
      h[k] = z;
    }
    // slope and 2 m terraces on steep, rocky ground (2 m is the human climb limit)
    const slope = new Float32Array(M * M);
    for (let j = 1; j < M - 1; j++) for (let i = 1; i < M - 1; i++) {
      const k = j * M + i;
      slope[k] = D.hyp((h[k + 1] - h[k - 1]) / 2, (h[k + M] - h[k - M]) / 2);
    }
    const label = new Int16Array(size * size), outH = new Float32Array(size * size), cover = new Uint8Array(size * size), lvl = new Float32Array(size * size), shade = new Float32Array(size * size), tBiome = new Uint8Array(size * size);
    const seaT = world.params.seaTemp;
    for (let j = 0; j < size; j++) for (let i = 0; i < size; i++) {
      const k = (j + 1) * M + (i + 1), o2 = j * size + i, x = x0 + i + 1.5, y = y0 + j + 1.5, gx = x / cell - 0.5, gy = y / cell - 0.5;
      let z = h[k]; const sl = slope[k];
      const lavaV = bilinear(world.lava, nx, ny, gx, gy) + 0.2 * fbm(nz.cover, x / 40, y / 40, 2);
      const paliV = bilinear(world.pali, nx, ny, gx, gy);
      let ledge = -1; // on the pali: 1 = a ledge, 0 = a rock face
      if (kind[k] === 0 && paliV > 0.3 && sl > 0.6) { // the pali: 6 m rock steps with narrow ledges
        const S = 6, step = Math.floor(z / S) * S, f = (z - step) / S; z = step + (f < 0.3 ? 0 : (f - 0.3) / 0.7 * S);
        ledge = f < 0.3 ? 1 : 0;
      } else if (kind[k] === 0 && sl > 0.5 && lavaV < 0.55) { const step = Math.floor(z / 2) * 2, f = z - step; z = step + (f < 0.7 ? 0 : (f - 0.7) / 0.3 * 2); }
      outH[o2] = z; lvl[o2] = water[k];
      { // hillshade, light from the north-west; uses the margin so chunk edges match
        const sx = (h[k + 1] - h[k - 1]) / 2, sy = (h[k + M] - h[k - M]) / 2;
        shade[o2] = clamp(0.82 + (sx + sy) * 0.55 - (sx * sx + sy * sy) * 0.06, 0.45, 1.25);
      }
      const nC = fbm(nz.cover, x / 22, y / 22, 3), nC2 = noise(nz.cover ^ 0x5bd1, x / 7, y / 7);
      let c;
      if (kind[k] === 1) {
        const ci = clamp(Math.floor(x / cell), 0, nx - 1) + clamp(Math.floor(y / cell), 0, ny - 1) * nx;
        const dep = -z;
        c = world.sea[ci] === 3 && dep < 6 && nC > -0.1 ? CV.reef : dep < 2.5 ? CV.shallow : CV.deep;
      } else if (kind[k] === 2) c = CV.river;
      else if (kind[k] === 3) c = CV.lake;
      else {
        const rainV = bilinear(world.rain, nx, ny, gx, gy);
        const dc = bilinear(world.distLand, nx, ny, gx, gy);
        const bx = clamp(Math.floor(gx), 0, nx - 2), by = clamp(Math.floor(gy), 0, ny - 2);
        let beachy = false;
        for (let b = 0; b <= 1; b++) for (let a = 0; a <= 1; a++) if (world.coast[(by + b) * nx + bx + a] === 1) beachy = true;
        const jt = clamp(1.6 * fbm((nz.cover ^ 0x5151) >>> 0, x / 90, y / 90, 3), -1, 1);
        const wetV = bilinear(world.wet, nx, ny, gx, gy) + (riverD[k] < 25 ? 0.15 : 0);
        const bi = classify(Math.max(0, z), rainV, seaT - 6.5 * Math.max(0, z) / 1000, bilinear(world.fog, nx, ny, gx, gy), wetV,
          bilinear(world.slope, nx, ny, gx, gy), lavaV, dc, beachy, bilinear(world.crest, nx, ny, gx, gy), jt);
        tBiome[o2] = bi;
        const u = 0.5 + 0.5 * D.tanh(2.4 * (nC + 0.35 * nC2));
        if (ledge === 0) c = CV.rock;
        else if (ledge === 1) c = rainV > 2200 ? (nC2 > 0.3 ? CV.dense : CV.fern) : CV.scrub;
        else if (sl > 1.1) c = CV.rock;
        else if (riverD[k] < 3 + nC2 * 2 && sl < 0.2) c = CV.mud;
        else if (bi === B.strand) c = pickCover(bi, clamp(dc / 200 + 0.25 * nC, 0, 0.999)); // sand at the water, strand plants behind
        else {
          c = pickCover(bi, u);
          // gallery forest: rivers carry trees through dry country
          if (riverD[k] < 16 + 10 * nC2 && (bi === B.dryscrub || bi === B.grassland || bi === B.mesic)) c = nC > 0 ? CV.dense : CV.sparse;
          if (sl > 0.75 && c !== CV.dense && c !== CV.cloud) c = nC2 > 0.2 ? CV.boulder : CV.rock;
        }
      }
      if (kind[k] !== 0) tBiome[o2] = kind[k] === 3 ? B.lake : kind[k] === 2 ? B.lake : B.ocean;
      if (mark[k]) c = MKC[mark[k]];
      cover[o2] = c; label[o2] = lab[k];
    }
    return { size, x0: ccx * CHUNK, y0: ccy * CHUNK, height: outH, cover, water: lvl, shade, biome: tBiome, label };
  }

  return {
    DEFAULTS, COVER, BIOMES, REGIONS, FAC, CORPS, FAUNA, PADDOCKS, CHUNK, generate, classify, transferables, indexRivers, chunk, riverWidth, riverDepth, bicubic, bilinear, hash2,
    _internal: { sample, makeAnchors, noise, fbm, ridged, hashStr, mix, hash2, stream, makeRng, Heap, chamfer, boxBlur, components, chaikin, D, smax, smoothstep, clamp, flood, buildTree, makeTree }
  };
})();
if (typeof module !== 'undefined') module.exports = IslandGen;
