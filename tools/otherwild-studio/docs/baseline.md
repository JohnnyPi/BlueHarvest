# VA.0 — read-only source rebaseline

**Date:** 2026-11-24  
**Target:** `JohnnyPi/BlueHarvest`, **branch `main`**, commit `59dbaa7bcdd3adbf781f338b65174cfc41f3575c`  
**Method:** Read-only GitHub connector tree and targeted file inspection; no repository code edits or fixture rewrite.  
**Status:** Source inventory complete for initial Studio scope. Native build, xUnit, console validation and Godot smoke **not run** in this environment. Local verification and design approval are outstanding.

## Branch and topology

- The repository metadata identifies **`master` as default**, but **`main` is the working branch**. Always pass `main` explicitly; do not inherit the default branch.
- `main` head was confirmed by the branch API at the SHA above. Recursive tree contained **627 entries** and reported `truncated: false`.
- `BlueHarvest.sln`, `src/BlueHarvest.Content/`, `src/BlueHarvest.Sim/`, `src/BlueHarvest.Sim.Tests/`, `src/BlueHarvest.Runner/`, `godot/`, `tools/island-lab/` and `tools/narrative-studio/` exist.
- No `tools/otherwild-studio/` existed in that tree. This baseline package proposes that new location; it is not evidence of an existing app.

## Native source ownership and reviewed contracts

| Existing path | Verified role | Immediate Studio implication |
|---|---|---|
| `src/BlueHarvest.Content/Ids/IdRules.cs` and `Ids/ContentId.cs` | Case-sensitive qualified IDs, 64-character total limit; namespace limit 40; exact regexes; bare IDs resolve against current reading pack. | Mirror for *fast* UI checks, then test against native. Do not lowercase IDs. |
| `Definitions/ContentKinds.cs` | Exactly six accepted native `kind` values: `item`, `recipe`, `station`, `resource_source`, `device`, `narrative`. | No NPC, evidence, historical event, placement, situation, or script kind emitted into native packs. |
| `Definitions/NarrativeDefinition.cs` | `form: document|dialogue`, binding, facts, ordered nodes and choices. | First structured authoring adapter: narrative. |
| `Schema/DefinitionReader.cs`, `Schema/FieldReader.cs` | Typed field validation and graph validation, including draft/ready differences and unrecognized-field diagnostics. | Native parser is authoritative; UI predictions are not certification. |
| `Schema/ManifestReader.cs`, `Loading/PackManifest.cs` | `blueharvest.pack/v1`, dependency/version/roots/priority/explicit `replaces`. | Keep all pack manifest semantics; no priority-based implicit replacement. |
| `Yaml/StrictYamlReader.cs`, `Yaml/YamlLimits.cs` | Strict UTF-8, single YAML document, duplicate key and explicit tag rejection; limits 1 MiB, depth 32, node weight 100,000. | Preserve bytes and invalid buffers; never deserialize YAML as code. |
| `Loading/ContentLoader.cs`, `Registry/ContentRegistry.cs` | Deterministic, ordered pack and reference loading; authoring vs runtime modes; immutable registry and fingerprint. | Keep reference lookup separate from graph visual layout and runtime simulation. |
| `Handlers/HandlerRegistry.cs` | Default handlers `process.transform`, `process.repair`, `resource.harvest`, `device.install_and_activate` declared **unimplemented**. | A declared handler is not necessarily executable. |
| `src/BlueHarvest.Sim/ContentIntegration/SimContent.cs` | Adds implemented item handler contracts; `LoadForPlay` currently calls **authoring mode**, with item readiness checked by the session. | Do not present `LoadForPlay` success as universal runtime readiness. |
| `src/BlueHarvest.Content/Diagnostics/ContentDiagnostic.cs` | Code, severity, file, 1-based line/column, definition ID, message. | Preserve all fields and mode/fingerprint when displaying diagnostics. |
| `tools/narrative-studio/README.md` | Existing browser editor v1.1 with ID, item/device qualification, `location`→`site` migration. | Migration source; keep legacy editor working through parity gate. |
| `tools/narrative-studio/make-test-fixture.js` | Deletes/recreates the native narrative fixture directory. | **Do not run in a normal baseline pass**; use isolated output for future comparisons. |

### Exact native narrative constraints relevant to VA.2/VA.7/VA.9

- `kind: narrative` uses `title`, not item-style `name` or `description`; `status` distinguishes `draft` and `ready`.
- Document presentations supported by the native reader are `note`, `journal`, `terminal`, `system_message`, `email`, `audio_log`, `radio`, `inscription`, and `sign`; dialogue is a separate presentation/form.
- `source.kind` can be `unbound`, `npc`, `item`, `world_object`, `device`, `site`, `broadcast`. Only item/device refs are resolved as registered identities. `document.author` is free text, not a resolved NPC ID.
- Dialogue has ordered nodes and choices, `start_node`, `to`, and `__end__`; valid cycles are not prohibited globally. An ending node's outgoing choices are preserved and validated but do not count for reachability.
- `requires_facts` and `reveals_facts` are authored metadata; they do not directly mutate the simulation.

## Fixture inventory and negative cases

**Existing positive fixture**: `src/BlueHarvest.Sim.Tests/Content/Fixtures/packs/` contains `core` and `narrative` packs. `FixturePackTests.cs` asserts **14 definitions** (native authoring mode), fixed content/pack identity, multiline dialogue and terminal content, and **four not-runtime-ready definitions** (`blueharvest.narrative:NAR-TXT-0001`, `core:DEV-ADM-001`, `core:RCP-022`, `core:SRC-005`). These are **source assertions, not a freshly passing test run**.

**Existing negative/conformance coverage**: `NarrativeTests.cs`, `StrictYamlTests.cs`, `ContentIdTests.cs` cover invalid starts/targets, duplicates, unreachable nodes, draft/ready behavior, malicious/ambiguous YAML, newline and BOM normalization, precise diagnostics, case sensitivity, and length limits.

**VA.0 fixture additions to prepare in VA.2/VA.7** (not yet constructed): a copy of the v1.1 JSON backup; explicit v1.0 `location` import; opaque unknown file; invalid YAML buffer; colliding content IDs; Menagerie **structure-only** Kit and graph models. Never invent narrative prose while making fixtures.

## Console and baseline test commands

From the repository root, on the **developer's Windows machine**:

```powershell
git status --short
git branch --show-current
git rev-parse HEAD
dotnet --info
dotnet build BlueHarvest.sln
dotnet test BlueHarvest.sln
dotnet run --project src/BlueHarvest.Runner -- content validate --path src/BlueHarvest.Sim.Tests/Content/Fixtures/packs
dotnet run --project src/BlueHarvest.Runner -- island validate 1 3
```

The runner's `content validate --runtime` option is real, but the fixture deliberately includes unsupported definitions. Its nonzero result must be recorded as an **expected readiness failure**, not a broken authoring fixture. `island validate` writes a timestamped report under `reports/` (ignored by Git). See `scripts/collect-baseline.ps1` for log collection. Do not run the destructive legacy fixture generator.

## Environment / evidence limits

- In this execution environment: **Node v22.16.0**, **npm v10.9.2** present; **`dotnet` absent**; npm registry DNS attempt returned `EAI_AGAIN`. This describes the temporary execution environment, **not** the user's development PC.
- Native build, xUnit, Runner, Godot and browser tests: **not executed**. No test count or pack fingerprint measured afresh.
- npm dependency versions, package license compatibility, Playwright browser provisioning, and UI performance are **not yet verified**. Do not create an unverified `package-lock.json` or claim offline install from this environment.
- Live repository state may advance after this SHA; repeat tree/targeted file reads before applying patches to a new head.

## VA.0 exit checklist

- [x] Branch/SHA/tree and core owner paths verified via GitHub.
- [x] Core native ID, schema, YAML, manifest, loader, handler, diagnostics and narrative fixture contracts inspected.
- [x] Source-derived support/deferral matrix documented in `contracts.md`.
- [ ] Developer-local `dotnet build`, xUnit, `content validate`, and island smoke logs captured.
- [ ] Toolchain/version/license choices for VA.1 installed and pinned.
- [ ] VA-D02–07/09–10 and UX defaults reviewed without silently promoting proposals to canon.
- [ ] G0 baseline and deferrals accepted for progression to VA.1.
