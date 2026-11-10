# Island lab reference generator

`gen.js` is the island lab's world generator: the reference that the C# port in
`src/BlueHarvest.Sim/World/Generation/` must match bit for bit.

The golden scripts print the expected values used by the port's tests:

```
node tools/island-lab/golden.js         # stage 1: GenerationCoreTests
node tools/island-lab/golden-shape.js   # stage 2: IslandShapeTests (paste the printed table into Golden)
node tools/island-lab/golden-climate.js # stage 3: IslandClimateTests
node tools/island-lab/golden-sites.js   # stage 4a: IslandWorksTests
node tools/island-lab/golden-roads.js   # stage 4b: IslandRoadsTests
node tools/island-lab/golden-transit.js # stage 4c: IslandTransitTests
node tools/island-lab/golden-wild.js    # stage 4d: IslandWildTests
```

`generate({ stopAfter: 'shape' | 'climate' | 'sites' | 'roads' | 'transit' | 'works' })` returns the generator's internal state at the
end of stage 2, stage 3, stage 4a (after the compounds are built) or stage 4b (after the roads), which is what the golden scripts hash. The test islands are listed
in each script's CASES and in `TestIslands.cs`; keep the two in step.

If `gen.js` changes on purpose, rerun `golden.js` and update the expected values in the tests.
The generator only uses + − × ÷, `Math.sqrt` and `Math.floor` for anything that reaches the output
(see the `D` block near the top), which is what makes the results identical in Node and .NET.
