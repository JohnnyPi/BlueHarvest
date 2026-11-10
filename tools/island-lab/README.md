# Island lab reference generator

`gen.js` is the island lab's world generator: the reference that the C# port in
`src/BlueHarvest.Sim/World/Generation/` must match bit for bit.

`golden.js` prints the golden values used by the port's tests (`GenerationCoreTests` and later stages):

```
node tools/island-lab/golden.js
```

If `gen.js` changes on purpose, rerun `golden.js` and update the expected values in the tests.
The generator only uses + − × ÷, `Math.sqrt` and `Math.floor` for anything that reaches the output
(see the `D` block near the top), which is what makes the results identical in Node and .NET.
