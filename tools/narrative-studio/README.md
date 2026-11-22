# Narrative Studio

`BlueHarvest_Narrative_Studio.html` is the offline editor for notes, terminal messages, dialogues and other
narrative entries. Open it in a browser; it needs no network. It exports `blueharvest.pack/v1` packs that
`BlueHarvest.Content` loads (`dotnet run --project src/BlueHarvest.Runner -- content validate --path <unzipped pack>`).

**v1.1 (22 Nov 2026)**, from v1.0 (`Island Notes/BlueHarvest_Narrative_Studio_v1_0.html` in the project):

- **IDs follow the content library's rules** (`src/BlueHarvest.Content/Ids/IdRules.cs`), so a pack that exports
  also passes `content validate`'s ID checks:
  - entry IDs are catalog IDs (`MEN-01-N01`) or kebab-case, at most 64 characters with the pack ID;
  - pack IDs are lower case, dot-separated segments, at most 40 characters;
  - fact IDs (`FACT-MEN-001`), facility codes (2 to 4 capitals), node and reply IDs, tags and references are
    checked too.

  The examples use `EXM-…` IDs; v1.0's `EXAMPLE-…` ones didn't fit.
- **Item and device references name their pack.** A bare ref (`DEV-ADM-001`) comes from the pack in
  "Items and devices come from pack" (default `core`) and is exported as `core:DEV-ADM-001`. Every pack the
  exported entries refer to is listed under `dependencies`, which the loader requires for any cross-pack reference.
- **`site` is the one name for a site binding.** v1.0's menu said `location`; entries saved or imported with
  `location` become `site`, and the loader no longer accepts `location`.
- An ending node's replies no longer count toward reachability, as in the loader.

Saved drafts in the browser carry over (the storage key is unchanged).

`make-test-fixture.js` rebuilds the C# tests' Studio fixture (`src/BlueHarvest.Sim.Tests/Content/Fixtures/packs/narrative`)
with the Studio's own code: `node tools/narrative-studio/make-test-fixture.js`. Run it after changing the Studio's
export, then run the tests.
