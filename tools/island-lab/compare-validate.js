'use strict';
// Compares two `island validate` seeds.csv files column by column (every column but seconds): the C# runner's
// (reports/island-<time>/seeds.csv) against the lab's (validate.js --out). Same seeds and scale on both sides.
//   node tools/island-lab/compare-validate.js <csharp seeds.csv> <lab seeds.csv>
const fs = require('fs');
const [a, b] = process.argv.slice(2);
if (!a || !b) { console.log('usage: node tools/island-lab/compare-validate.js <csharp seeds.csv> <lab seeds.csv>'); process.exit(2); }
const read = f => { const L = fs.readFileSync(f, 'utf8').replace(/\r/g, '').split('\n').filter(Boolean); const head = L[0].split(','); return { head, rows: new Map(L.slice(1).map(l => { const v = l.split(','); return [v[0], v]; })) }; };
const A = read(a), B = read(b);
const cols = A.head.filter(c => c !== 'seconds' && B.head.includes(c)), onlyA = A.head.filter(c => !B.head.includes(c)), onlyB = B.head.filter(c => !A.head.includes(c));
let seeds = 0, diffs = 0;
for (const [seed, ra] of A.rows) {
  const rb = B.rows.get(seed); if (!rb) { console.log(`seed ${seed}: not in ${b}`); diffs++; continue; }
  seeds++;
  for (const c of cols) { const va = ra[A.head.indexOf(c)], vb = rb[B.head.indexOf(c)]; if (va !== vb) { diffs++; if (diffs <= 40) console.log(`seed ${seed} ${c}: ${va} vs ${vb}`); } }
}
if (onlyA.length || onlyB.length) console.log(`columns only in the first: ${onlyA.join(' ') || 'none'}; only in the second: ${onlyB.join(' ') || 'none'}`);
console.log(diffs ? `${diffs} differences over ${seeds} seeds and ${cols.length} columns.` : `Identical: ${seeds} seeds, ${cols.length} columns.`);
process.exit(diffs ? 1 : 0);
