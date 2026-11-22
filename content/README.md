# Content

The game's authored content, as YAML packs that `src/BlueHarvest.Content` loads (P1). Reference:
`claude/p1a-content-library.md` in the project; contracts: `claude/contracts.md`.

- `packs/core/` is the game's own pack. Its IDs are the `core` namespace.
- `tooling/crosswalk.csv` is the offline record of catalog IDs that became existing `core:` kinds (contracts §1):
  `old,new,reason`. Nothing reads it at run time. The tests check it against the definitions' `source_ids`.

Check the packs:

```powershell
dotnet run --project .\src\BlueHarvest.Runner -- content validate            # finds content/packs
dotnet run --project .\src\BlueHarvest.Runner -- content validate --runtime  # fails until P5's handlers exist
```

The Godot project's build copies `packs/` to `godot/Content/packs/` (git-ignored), and the game loads that copy
through `res://`. Run from the editor, the game also loads this folder and checks the fingerprint is the same;
the front menu shows the result. An export preset must list `*.yaml` under "Filters to export non-resource files".
Narrative entries can be written with the Narrative Studio (`tools/narrative-studio/`).
