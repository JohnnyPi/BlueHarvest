# Island lab reference generator

`gen.js` is the island lab's world generator: the reference that the C# port in
`src/BlueHarvest.Sim/World/Generation/` must match bit for bit.

The golden scripts print the expected values used by the port's tests:

```
node tools/island-lab/golden.js         # stage 1: GenerationCoreTests
node tools/island-lab/golden-shape.js   # stage 2: IslandShapeTests (paste the printed table into Golden)
```

`generate({ stopAfter: 'shape' })` returns the generator's internal state at the end of stage 2,
which is what `golden-shape.js` hashes.

If `gen.js` changes on purpose, rerun `golden.js` and update the expected values in the tests.
The generator only uses + − × ÷, `Math.sqrt` and `Math.floor` for anything that reaches the output
(see the `D` block near the top), which is what makes the results identical in Node and .NET.
