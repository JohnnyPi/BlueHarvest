# Island lab reference generator

`gen.js` is the island lab's world generator: the reference that the C# port in
`src/BlueHarvest.Sim/World/Generation/` must match bit for bit.

The golden scripts print the expected values used by the port's tests:

```
node tools/island-lab/golden.js         # stage 1: GenerationCoreTests
node tools/island-lab/golden-shape.js   # stage 2: IslandShapeTests (paste the printed table into Golden)
node tools/island-lab/golden-climate.js # stage 3: IslandClimateTests
node tools/island-lab/golden-sites.js   # stage 4a: IslandWorksTests (with the site program: ruins, ley routes, outstations, states, ADM)
node tools/island-lab/golden-roads.js   # stage 4b: IslandRoadsTests
node tools/island-lab/golden-transit.js # stage 4c: IslandTransitTests
node tools/island-lab/golden-wild.js    # stage 4d: IslandWildTests
node tools/island-lab/golden-chunks.js  # stage 5: IslandChunkTests (1 m tiles)
node tools/island-lab/golden-rooms.js   # step 25a-1: RoomIndexTests (every named room, its place and rules)
node tools/island-lab/golden-knowledge.js # step 25b-1: MapKnowledgeTests (the island-scale view rule on two scripted walks)
node tools/island-lab/golden-areas.js     # step 25b-2: FacilityAreasTests (facility areas and their ten park-map sheets)
```

`generate({ stopAfter: 'shape' | 'climate' | 'sites' | 'roads' | 'transit' | 'works' })` returns the generator's internal state at the
end of stage 2, stage 3, stage 4a (after the compounds are built) or stage 4b (after the roads), which is what the golden scripts hash. The test islands are listed
in each script's CASES and in `TestIslands.cs`; keep the two in step.

If `gen.js` changes on purpose, rerun `golden.js` and update the expected values in the tests.

## Seed checks and the site program (P0a, P4a)

```
node tools/island-lab/checks.js                       # self-test: the four test islands against IslandStatsTests' and IslandReachTests' pins
node tools/island-lab/checks.js --print               # the rows to pin there (and in checks.js) after a deliberate change
node tools/island-lab/validate.js 1 200 75 --out reports/lab-75   # `island validate 1 200 75`, with the runner's seeds.csv columns
node tools/island-lab/compare-validate.js <C# seeds.csv> <lab seeds.csv>   # every column but seconds
node tools/island-lab/sites-report.js 1 200           # program 2 over 200 seeds at 100%: reports/sites-<time>/report.md
```

On Windows, `powershell -ExecutionPolicy Bypass -File tools\island-lab\run-checks.ps1` runs all of it in order (the C# tests,
`checks.js`, the golden hashes against the tests, C# and lab `island validate` at 75% and 100% compared column by column, and the
site report) and writes `reports\checks-<time>\summary.md`. `-Count 20` makes a quick run; `-SkipTests`, `-SkipValidate` and
`-SkipSites` leave parts out.

`checks.js` ports `IslandStats.Measure`, `IslandWorks.Reachability` and `IslandChecks`; it needs a world made with
`generate({ ..., keepInternals: true })`, which keeps the double-precision grids the C# measures read (nothing generated changes).

**Program 2 is the default and what the C# generator is (P4c, `IslandGen.Version` 2).** `generate()` is the Crosswake Island
site program (master plan P4a), at 100% unless a scale is given: the Court
of Unfinished Measures with Central Administration's footprint reserved beneath it, the six sectors, the seven authored ruins,
five paddocks and the raptor pen, 2–8 outstations, ADM's infrastructure without a nuclear plant, four ley lines through the
Court with a survey post on each, the six powers as owners, and lesser sites scaled with land area. Every golden script and
C# test reads it. `works.program2` holds what program 2 measured: terrain roles, ruins, lines, outstations, seeded states, the
ADM footprint, and every placement failure with its reason (`stopAfter: 'sites'` returns it as `program2`).

`generate({ program: 1 })` is the old program (14 facility kinds and the corporations, at 75% unless a scale is given),
kept bit for bit so the golden values from before P4c can still be reproduced; nothing in C# matches it any more.

Program 2 also turns on the two P4b anchors (`anchors2`, on by default with program 2, off for program 1): a **reef flat**
off the most sheltered low shore, 0.6–1.5 m deep out to 700 m so it can be waded at the lowest tide (shape stage, sea depths),
and a **floodplain** along the longest lowland reach of a river of 1 m³/s or more, whose ground grows forest whatever the rain
(climate stage: `classify` takes a riparian floor, `world.riparian`, which the 1 m tiles read too). `world.reefFlat` and
`world.floodplain` say where they went.
The generator only uses + − × ÷, `Math.sqrt` and `Math.floor` for anything that reaches the output
(see the `D` block near the top), which is what makes the results identical in Node and .NET.
