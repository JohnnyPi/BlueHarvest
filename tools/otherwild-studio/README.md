# Otherwild Studio

> Reality is optional. Survival is not.

**Phase: VA.0 — repository rebaseline and contract lock (review pending).**

Otherwild Studio is the planned offline-first React/TypeScript/Vite authoring application for *Otherwild: The Impossible Eden*. It will live at `tools/otherwild-studio/`, alongside the existing `tools/narrative-studio/`. This folder currently contains **planning, audit, and reproducibility artifacts only**. There is **no running React app**, no graph editor, and no installed native bridge yet.

## Start here

- [`docs/baseline.md`](docs/baseline.md) — observed `main` source inventory, CLI contracts, environment limitations, and baseline commands.
- [`docs/contracts.md`](docs/contracts.md) — authority boundaries, first supported adapter, identity, content and graph rules, proposed decisions for review.
- [`docs/progress.md`](docs/progress.md) — VA.0 evidence, pending gates, and the bounded VA.1 sequence.
- [`scripts/collect-baseline.ps1`](scripts/collect-baseline.ps1) — optional Windows PowerShell collection of *actual local* build, test, and content validation results. Does not rewrite fixture packs.

## Source authority

The three companions supplied for this milestone are:

1. `Otherwild_Studio_Architecture_Bible_v1_0.md` — identity, semantic graphs, edit and storage boundaries.
2. `Otherwild_Studio_UX_Design_Bible_v1_0.md` — layout, navigation, accessibility and authoring experience.
3. `Otherwild_Studio_Implementation_Plan_v1_0.md` — VA.0–VA.15 dependencies and gates.

The native `BlueHarvest.Content` code remains the authority for pack parsing, IDs, schema validation, handler declarations and fingerprints. The Studio is **not** allowed to invent executable native kinds, effects, or save formats.

## Technical branding rule

Otherwild is the public product name. `JohnnyPi/BlueHarvest`, `BlueHarvest.*` C# identifiers, `blueharvest.pack/v1`, `blueharvest.content/v1`, and `.bhs` stay unchanged. `Otherwild Script` and `.ows` are not supported runtime formats.

## Next milestone

Once the baseline is run and reviewed, VA.1 builds a real React/TypeScript/Vite shell with tested explorer, tabs, inspector, problems, status, responsive behavior, local-only asset loading and a synthetic fixture. Commit a verified `package-lock.json` and pinned dependencies *with* the app. Do not mark VA.1 done based on an uninstalled scaffold.
