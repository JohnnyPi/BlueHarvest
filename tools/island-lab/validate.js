'use strict';
// `island validate` in the lab: generates count seeds from first at a scale, measures each one, walks the
// routes between its key sites and runs the seed checks (checks.js, a port of IslandStats, Reachability
// and IslandChecks). Matches BlueHarvest.Runner's `island validate [first] [count] [scale]`.
//   node tools/island-lab/validate.js [first seed] [count] [scale] [--out folder] [--workers n]
// Prints the runner's console summary; with --out, writes seeds.csv there.
const path = require('path');
const { Worker, isMainThread, parentPort, workerData } = require('worker_threads');

if (!isMainThread) {
  const G = require('./gen.js'), C = require('./checks.js');
  parentPort.on('message', ({ seed, scale, extra }) => {
    const t = process.hrtime.bigint();
    try {
      const w = G.generate(Object.assign({ seed, scale, keepInternals: true }, extra || {}));
      const genSec = Number(process.hrtime.bigint() - t) / 1e9;
      const S = C.measure(w), R = C.reach(w, C.ANCHOR_KINDS), V = C.validate({ S, R });
      const row = C.statsRow(S);
      parentPort.postMessage({ seed, seconds: Number(process.hrtime.bigint() - t) / 1e9, genSec, keys: row.keys, vals: row.vals, reach: C.reachRow(R),
        S: { landFraction: S.landFraction, landKm2: S.landKm2, lowlandShare: S.lowlandShare, beachShare: S.beachShare, walkableConnectedShare: S.walkableConnectedShare, islets: S.islets,
          facilitiesShort: S.facilitiesShort, paddocksShort: S.paddocksShort, highestM: S.highestM, roadKm: S.roadKm, tunnels: S.tunnels, rivers: S.rivers },
        R: { fullyConnected: R.fullyConnected, tunnelGaps: R.tunnelGaps, deepGaps: R.deepGaps, groundGaps: R.groundGaps, slowestMinutes: R.slowestMinutes },
        V: { passed: V.passed, failures: V.failures, warnings: V.warnings } });
    } catch (e) {
      parentPort.postMessage({ seed, seconds: Number(process.hrtime.bigint() - t) / 1e9, error: e.stack || String(e) });
    }
  });
  return;
}

const args = process.argv.slice(2), opt = {};
const pos = [];
for (let k = 0; k < args.length; k++) { if (args[k].startsWith('--')) opt[args[k].slice(2)] = args[++k]; else pos.push(args[k]); }
const first = pos[0] && pos[0] !== '-' ? Number(pos[0]) : 1, count = pos[1] && pos[1] !== '-' ? Number(pos[1]) : 200, scale = pos[2] && pos[2] !== '-' ? Number(pos[2]) : 75;
const workers = Number(opt.workers || require('os').cpus().length), extra = opt.params ? JSON.parse(opt.params) : {};
run().catch(e => { console.error(e); process.exit(1); });

async function run() {
  console.log(`Island validation (lab): ${count} seeds from ${first} at ${scale}% scale, ${workers} at a time`);
  const t0 = Date.now(), results = [], queue = [];
  for (let i = 0; i < count; i++) queue.push((first + i) >>> 0);
  await Promise.all(Array.from({ length: Math.min(workers, count) }, () => new Promise(resolve => {
    const wk = new Worker(__filename, { workerData: {} });
    const next = () => { if (!queue.length) { wk.terminate(); resolve(); return; } wk.postMessage({ seed: queue.shift(), scale, extra }); };
    wk.on('message', m => { results.push(m); if (results.length % 10 === 0 || results.length === count) console.log(`  ${String(results.length).padStart(4)}/${count}  (${Math.round((Date.now() - t0) / 1000)}s)`); next(); });
    wk.on('error', e => { console.error(e); resolve(); });
    next();
  })));
  results.sort((a, b) => a.seed - b.seed);
  const ok = results.filter(r => !r.error), sec = (Date.now() - t0) / 1000;
  const med = a => { const s = a.slice().sort((x, y) => x - y), n = s.length; return n ? (n % 2 ? s[(n - 1) / 2] : (s[n / 2 - 1] + s[n / 2]) / 2) : 0; };
  const spread = (name, get, d) => { const v = ok.map(get); console.log(`  ${name.padEnd(40)} ${Math.min(...v).toFixed(d).padStart(9)} ${med(v).toFixed(d).padStart(9)} ${Math.max(...v).toFixed(d).padStart(9)}`); };
  console.log(`\n${ok.length} of ${results.length} islands generated.`);
  console.log(`  ${'Measure'.padEnd(40)} ${'min'.padStart(9)} ${'median'.padStart(9)} ${'max'.padStart(9)}`);
  spread('Land, % of the frame', r => 100 * r.S.landFraction, 1);
  spread('Land, km2', r => r.S.landKm2, 0);
  spread('Lowland (below 150 m), % of dry land', r => 100 * r.S.lowlandShare, 1);
  spread('Walkable and connected, % of dry land', r => 100 * r.S.walkableConnectedShare, 1);
  spread("Beach, % of the main island's coast", r => 100 * r.S.beachShare, 1);
  spread('Islets (0.1 km2 and up)', r => r.S.islets, 0);
  spread('Highest point, m', r => r.S.highestM, 0);
  spread('Rivers', r => r.S.rivers, 0);
  spread('Roads, km', r => r.S.roadKm, 0);
  spread('Road tunnels', r => r.S.tunnels, 0);
  spread('Slowest key-site walk, hours', r => r.R.slowestMinutes / 60, 1);
  spread('Generation, s (lab, Node)', r => r.genSec, 2);
  spread('Generation + checks, s (lab, Node)', r => r.seconds, 2);
  console.log(`Seeds short of a facility: ${ok.filter(r => r.S.facilitiesShort > 0).length}; of a paddock: ${ok.filter(r => r.S.paddocksShort > 0).length}; under 15% beach: ${ok.filter(r => r.S.beachShare < 0.15).length}; failed to generate: ${results.length - ok.length}.`);
  console.log(`Key-site routes: every pair walkable in ${ok.filter(r => r.R.fullyConnected).length} of ${ok.length} seeds; seeds with a pair needing a tunnel: ${ok.filter(r => r.R.tunnelGaps > 0).length}; crossing deep water: ${ok.filter(r => r.R.deepGaps > 0).length}; with no ground or no route: ${ok.filter(r => r.R.groundGaps > 0).length}.`);
  const counts = pick => { const m = new Map(); ok.forEach(r => pick(r.V).forEach(x => m.set(x.key, (m.get(x.key) || 0) + 1))); return m.size ? [...m].map(([k, v]) => `${k} ${v}`).join(', ') : 'none'; };
  console.log(`Checks: ${ok.filter(r => r.V.passed).length} of ${ok.length} seeds pass; failing: ${counts(v => v.failures)}; warnings: ${counts(v => v.warnings)}.`);
  const failed = ok.filter(r => !r.V.passed);
  if (failed.length) console.log('Failing seeds: ' + failed.map(r => `${r.seed} (${r.V.failures.map(f => f.reason).join('; ')})`).join(' | '));
  results.filter(r => r.error).forEach(r => console.log(`Seed ${r.seed} threw: ${r.error.split('\n')[0]}`));
  console.log(`\nDone in ${sec.toFixed(0)}s (${(sec / count).toFixed(2)} s per seed with ${workers} workers).`);
  if (opt.out) {
    const fs = require('fs'); fs.mkdirSync(opt.out, { recursive: true });
    const keys = ok.length ? ok[0].keys : [];
    // the same columns as BlueHarvest.Runner's seeds.csv (IslandReport.Csv), so the two can be compared (compare-validate.js)
    const lines = ['seed,seconds,' + keys.join(',') + ',' + require('./checks.js').REACH_KEYS.join(',') + ',passed,failed_checks,warned_checks,error'];
    for (const r of results) lines.push(r.error ? `${r.seed},${r.seconds.toFixed(2)},${','.repeat(keys.length + 9 + 2)},${r.error.split('\n')[0].replace(/,/g, ';')}`
      : `${r.seed},${r.seconds.toFixed(2)},${r.vals.join(',')},${r.reach.join(',')},${r.V.passed ? 1 : 0},${r.V.failures.map(f => f.key).join(';')},${r.V.warnings.map(f => f.key).join(';')},`);
    fs.writeFileSync(path.join(opt.out, 'seeds.csv'), lines.join('\n') + '\n');
    console.log(`Seeds: ${path.join(opt.out, 'seeds.csv')}`);
  }
}
