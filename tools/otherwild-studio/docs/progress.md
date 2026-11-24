# Otherwild Studio — delivery ledger

## VA.0 / G0 — initial research and contract capture

- **Date:** 2026-11-24
- **Base:** `main` @ `59dbaa7bcdd3adbf781f338b65174cfc41f3575c`
- **Scope:** read-only GitHub rebaseline of relevant native content, legacy narrative, fixture and runner seams; design-contract review package.
- **Output:** `README.md`, `docs/baseline.md`, `docs/contracts.md`, `docs/progress.md`, and Windows baseline script.
- **Non-goals:** React scaffold, native code edits, new content kinds, renderer, graph engine, source migration, fixture-generator execution, and game-side mechanics.
- **Evidence:** GitHub connector read of `main` tree (627 entries, not truncated), core paths/algorithms and native test assertions; companion AB/UX/Plan source documents.
- **Native tests:** **not run** here — `dotnet` not installed in execution environment.
- **Frontend tests:** **not run** — app does not exist and npm registry inaccessible (`EAI_AGAIN`).
- **Result:** Documentation and targeted inventory produced; **G0 PENDING** local verification and review.
- **Risk:** GitHub connector could read `main` but branch creation returned `403 Resource not accessible by integration`; this handoff is provided as local, reviewable files, **not committed or merged**.

## Recommended local next steps

1. Create local branch from current `main` and apply the supplied new-files patch. Confirm the base SHA first; if `main` has moved, recheck changed native files.
2. Run `scripts/collect-baseline.ps1` in PowerShell (optionally `-IncludeIsland`) and keep the resulting report outside Git. Investigate unexpected failures without changing native code in the Studio branch.
3. Review engineering proposals in `docs/contracts.md` and distinguish accepted vs still open decisions.
4. Lock Node/npm/TS/React/Vite/lint/test tooling and license versions for the first app scaffold. Verify a non-watch test command and real install lockfile on the developer machine.
5. Start only **VA.1-A** after the G0 checklist is accepted: production scaffold and pinned toolchain, then **VA.1-B** shell, **C** tabs/fixture state, **D** keyboard/offline support, **E** browser smoke/build gate.

## VA.1 acceptance evidence to bank when completed

- `npm ci`, `npm run typecheck`, `npm run lint`, `npm run test`, `npm run build`, `npm run test:e2e` with exact counts and versions.
- Browser shell navigation, accessible landmarks, focus/selection return, compact mode, and synthetic fixture visibility.
- Blocked-external-network proof: app loads and functions while all non-local requests are denied.
- No changes to the C# simulation/native content schema or the legacy editor.

**Never report a task passed just because a source fixture specifies its expected result.**
