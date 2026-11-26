# Feature: CascadeDiagramView ShowIndex & MindMap View menu toggle

- [x] 1. Trace the affected subsystem.
- [x] 2. `architect` for the shape. (Picked Shape A2: `CascadeDiagramView.ShowIndex` default `false` as single source of truth; `TreeDrawer.Draw`/`Render`/`RenderImage`/`RenderImageWithPlaceholder` gain `bool showIndex = false`; `MindMap.MainForm` `msMenu -> mnuView` adds `mnuViewShowIndex` synced with `diagramView1.ShowIndex`.)
- [x] 3. Write the throughput checkpoint as four todo items:
  - [x] **Blocking first steps.** `CascadeControl` API (`TreeDrawer` + `CascadeDiagramView.ShowIndex`) must be defined before `MindMap` binds `mnuViewShowIndex` to `diagramView1.ShowIndex` and before `tools/public-api.txt` is dumped.
  - [x] **Independent workstreams.** n/a: single tightly coupled feature touching 6 small files across `CascadeControl`, `MindMap`, and `CascadeControlTests` that share the `ShowIndex` property contract.
  - [x] **Shared mutable state.** Single writer in an isolated `Workspace: "share"` delegate worktree; parent did not edit files while the delegate ran.
  - [x] **Smallest safe decomposition.** One implementation delegate in an isolated shared worktree is smallest and safest because `TreeDrawer`, `CascadeDiagramView`, `MindMap.MainForm`, and `TreeDrawerTests` all compile and verify together against the same `ShowIndex` contract.
- [x] 4. Delegate code-writing to a subagent in its own worktree (`Workspace: "share"`).
- [x] 5. Verify on the matching surface (Debug & Release MSBuild, `vstest.console.exe`, `tools/dump-public-api.ps1` diff, WinForms `MindMap.MainForm` runtime probe).
- [x] 6. Rebase into small, ordered commits (applied `/deslop` and `/no-comments` review; deleted 2 stale private comments in `TreeDrawer.Arrange`; amended/committed cleanly).
- [x] 7. If the design is contested, `interrogate` before shipping. skip: user explicitly selected Option 1 with `msMenu -> View -> ShowIndex`.
- [x] 8. Run **Opening a PR**. skip: working directly on local `main` branch; user has not requested a PR.
