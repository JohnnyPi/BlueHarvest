# VA.0 — authority, ownership and initial integration contracts

**Review status: proposed engineering decisions are pending acceptance.** This file records what the three supplied Studio bibles direct and what current `main` actually supports; it does not claim a newly approved native schema.

## 1. Source hierarchy and immutable boundaries

- **Architecture Bible**: definition identity, typed relationships, transactions, workspace metadata, preservation, native ownership.
- **UX Bible**: visual grammar, navigation, layout, keyboard/focus, accessible alternatives, reader/writer behavior.
- **Implementation Plan**: order VA.0–VA.15 and gates G0–G6. The BlueHarvest Master Implementation Sequence continues to govern game-side dependencies.
- **C# Content library**: the actual pack/definition schema, ID acceptance, semantic reference validation, handlers, readiness and canonical fingerprint. Do not treat a TypeScript implementation as equivalent without native fixture conformance.
- **C# simulation**: the only authority for game facts, acquisition, inventory, events, world mutations, persistence and mission completion.

All work is local-first, without telemetry, mandatory accounts, CDN loading or cloud-hosted build services. Otherwild is presentation branding only; keep `.bhs`, `BlueHarvest.*`, `blueharvest.pack/v1`, `blueharvest.content/v1`, and existing qualified IDs unchanged. No invented story text or character canon.

## 2. Initial adapter support and visible capability labels

**These are different dimensions:** schema support, semantic editability, runtime capability, and authored readiness (`draft`/`ready`). Display each separately.

| Domain / kind | Current native schema? | Initial Studio treatment (planned) | Runtime support claim |
|---|---|---|---|
| Pack manifest | Yes, `blueharvest.pack/v1` | Parse/import; show dependencies, roots, replacements; preserve original | Loader can validate manifest; not a gameplay mechanic |
| Narrative: document/dialogue | Yes, `kind: narrative` | **First editable structured adapter**, after VA.2 transactional source repository; source preserved until parity | Readable authoring definitions; playable dialogue/acquisition not certified by loader alone |
| Item | Yes, `kind: item` | Reference index and read-only inspector initially; richer editor deferred | Supported item handlers present in `SimContent`; test actual action integration per domain |
| Recipe | Yes | Reference or opaque/read-only initially | Default `process.*` handlers marked not implemented at inspected head |
| Station | Yes | Reference or opaque/read-only initially | Station definitions alone do not prove processing behavior |
| Resource source | Yes | Reference or opaque/read-only initially | Default `resource.harvest` declared not implemented |
| Device | Yes | Reference or opaque/read-only initially | Default `device.install_and_activate` declared not implemented |
| NPC/characters, evidence, knowledge, history, placement, situations, missions | **No registered native content kind** in `ContentKinds.cs` | **Design-only** Studio storage with explicit status once phases arrive | None by declaring frontend data; native milestone/adapter required |
| `.bhs` scripting | Blueprint only in supplied scripting documents; no native kind registered here | Deferred to VA.14 and native compiler/runtime prerequisites | Never assert executable capability from a visual graph |
| `otherwild.studio.graph/v1` | **Proposed Studio-only workspace document** | Layout and view metadata only | Not a native content pack format |

**No design-only file** may be slipped into a native `content_roots` folder or have unsupported fields silently stripped to satisfy export. No `.ows` runtime format is authorized.

## 3. Stable identity and reference rules

- Pack ID is the namespace, lower-case dotted segments, max **40** characters. Definition ID is `namespace:local`, max **64** characters total, **case sensitive**. Local IDs are native-accepted kebab or catalog form, exactly as `IdRules.cs` defines.
- Native bare IDs resolve to the **current reading pack**. Legacy Narrative Studio item/device bare refs used its **selected `refPack`** (default `core`) and export as qualified refs; migration must preserve that distinction.
- Pack dependencies are explicit. Priority sorts independent packs but **does not** authorize replacing definitions; `replaces` must explicitly declare replacements.
- Definition identity is not a filename, title, node coordinate, array index, or runtime instance ID. Display-title edits do not change content IDs.
- Graph IDs, node occurrences, edge occurrences and semantic relation keys are separate. A connection is gameplay-semantic only when a typed owning field/relation adapter explicitly supports it.
- Document `author` is free text, not guaranteed `NPC` identity. Source binding `npc`/`site`/`world_object`/`broadcast` reference forms are validated but not currently resolved through native registries.

## 4. Editing, graph and persistence invariants (AB proposals)

1. **One authoritative source repository**, with source text/bytes, revision, parsed projection and diagnostics. No parallel disconnected graph database.
2. Use pure command/transaction logic under UI state; apply semantic connection and source-field mutation atomically. Undo/redo applies to one complete transaction, not individual node gestures.
3. Preserve untouched original files exactly. For edited supported YAML, require native-semantic equivalence, safe scalar formatting and explicit unknown-field handling. Invalid source buffers are recoverable; stale last-valid projections cannot be silently exported as updated data.
4. Unsupported files stay opaque, readable and unmodified. A structured edit refuses if it cannot preserve unsupported fields.
5. Graph classes: **relationship** (no execution), **dependency** (explicit logical meaning), **execution** (native dialogue or bounded scripts). Connection validation is domain/port-specific; visual proximity means nothing.
6. Workspace graphs and layout metadata remain outside native pack roots and do not affect game fingerprint. Generated graphs are projections of stored content, not duplicate authority.
7. A native diagnostic/report must identify source mode, pack lock/fingerprint, target version, source revision and report freshness. A simulated/design-only preview is never labeled native validation.
8. Import/export is staged and recoverable. No destructive import without review. Do not rely on IndexedDB as the sole backup. Avoid cross-tab clobbering through revision/lease checks.

## 5. VA.1 UX contract (no app implemented yet)

**Application structure:** project/status toolbar; workspace navigation; explorer; central tabbed editor; inspector; collapsible problems/output area; status line. Warm field-journal palette from UX §4, locally bundled/system fonts and icons; no unnecessary ASCII styling of normal forms.

**Navigation:** Project and Fiction remain usable without graph; identity-based tabs, explicit back/close, preserve selection/scroll/viewport and unfinished edits. Responsive layout: wide three-region, intermediate docked, compact one-primary-surface with Explorer/Editor/Inspector switching and future list alternatives. Breakpoints in UX are proposed values, not validated responsive thresholds.

**Accessibility:** keyboard-operable docks/tabs, focus restoration, visible state/error text without color reliance, at least one accessible non-graph path, reduced motion, screen-reader landmarks, no global shortcuts while typing. External hosts prohibited at runtime; browser offline proof required.

**Future navigation:** show disabled, explicitly labeled future workspaces or omit them; no decorative clickable controls without behavior. The synthetic fixture must be labeled **fixture**, not a loaded real game project.

## 6. Decisions/deferrals for review

- Adopt **VA-D02–VA-D07, VA-D09–VA-D10** as engineering baselines *subject to review*, while already locked VA-D01/08/11–13 remain inherited. Do not call unreviewed proposals canon.
- Use **narrative-only editable native scope** at VA.2; items/devices available for read-only reference resolution. This is a bounded rollout, not permanent denial of other editors.
- Keep history, knowledge, placement, situations and script authoring **segregated as design-only** until approved native owners/schemas are implemented. Native adaptations require separate milestones and tests.
- Keep the old `tools/narrative-studio` operational through G3 and author acceptance. No change to its destructive fixture generator as part of VA.0.
- Native bridge is deferred; when built, it executes a fixed allowlist only. No shell-execution or arbitrary path APIs.
- Open cross-project creative/history policies remain open; Studio authors structures and blank slots, not unsolicited lore.

## 7. G0 acceptance evidence still required

A reviewed baseline with current SHA, local toolchain and full native test log, explicit adapter support matrix, initial fixture inventory and agreed engineering defaults. Until then, **G0 is not banked; VA.1 is the next proposed implementation phase, not a completed release**.
