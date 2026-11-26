# Feature Todo: CascadeDiagramView vertical centering (Option A, default)

Goal: Keep CascadeDiagramView left-aligned while vertically centering the rendered diagram bounding box within `ClientSize.Height` as the default behavior, and keep MindMap zoom anchoring and tests aligned with the single transform source of truth.

## Playbook Steps (Feature)

- [x] 1. Trace the affected subsystem.
- [x] 2. `architect` for the shape.
  - Shape 1 (chosen): Keep `GetTransform(out float scale, out PointF offset)` in `CascadeDiagramView.Rendering.cs` as the single source of truth (`VerticalCenterOffset` using integer-floored centering to keep 1:1 bitmap crisp), and change `ToImagePoint(Point clientLocation)` from `private` to `protected` so `MindMapDiagramView.SetZoomIndex` computes cursor-anchored zoom from the real offset without duplicating centering math.
  - Shape 2 (rejected): Duplicate centering calculation inside `MindMapDiagramView.SetZoomIndex` using `Image.Height` and `ClientSize.Height`.
- [x] 3. Write the throughput checkpoint as four todo items:
  - **Blocking first steps.** Confirm baseline build and MSTest suite pass, and finalize the Shape 1 contract (`GetTransform` + `protected ToImagePoint`).
  - **Independent workstreams.** `n/a: single tightly coupled feature touching CascadeDiagramView.Rendering.cs, MindMapDiagramView.cs, and CascadeControlTests\Views\Diagram\*.cs under one .sln build/test gate.`
  - **Shared mutable state.** Delegate runs in its own isolated git worktree (`Workspace: "share"`), keeping the main checkout untouched until review and merge.
  - **Smallest safe decomposition.** One implementation subagent (`TypeName: "self"`) in an isolated git worktree delivering 2 ordered verifiable commits (1: core transform + MindMap zoom anchor; 2: update affected test + add vertical-centering behavior tests), followed by independent `comment-sicko` review, `deslop`, and full verification on main checkout.
- [x] 4. Delegate code-writing to a subagent in its own isolated git worktree (commits merged to main; independent `comment-sicko` review + `deslop` cleanup applied).
- [x] 5. Verify on the matching surface (MSBuild 5/5 projects succeeded + full MSTest suite 603/603 passed, 0 failed, 0 skipped on main checkout).
- [x] 6. Rebase into small, ordered commits (2 clean commits on main: production transform + MindMap anchor, then tests).
- [x] 7. If the design is contested, `interrogate` before shipping. `skip: single uncontested Option A design confirmed by user; Shape 1 keeps transform in one place.`
- [x] 8. Run **Opening a PR**. `skip: user requested local implementation on current branch, not opening a remote PR.`
