# CascadeControl view refactoring plan

Read this document at the start of every session before changing code. Update the status checklist and verification log after each completed stage.

## Locked decisions

1. The final architecture target is Plan B2, where a shared state model decouples `CascadeView`, `CascadeCheckboxView`, and `CascadeTreeImageView` into peer views.
2. Execute Plan A stages 1 through 4 first as the foundation. Build the solution, pass all unit tests, update this file, and stop for user confirmation after every stage.
3. The repository has no external downstream callers. Breaking changes to `EventArgs` types are acceptable, and event unification runs as a separate phase after Plan B2.
4. Keep shared interfaces limited to navigation and check state. Exclude editing and undo members (`ReadOnly`, `History`, `TreeEdited`) from the interfaces for now.
5. After Plan A stage 4 passes, run `/pstack:architect` to design Plan B2. Output only the state object types, signatures, control binding model, and migration steps. Do not write Plan B2 implementation code until the user confirms the design.

## Current architecture baseline

- `CascadeControl/Views/CascadeView.cs` (with five partial files) inherits `Panel` and owns `MenuTree`, `CascadeNavigator`, multi-selection, editing, and `MenuHistory`.
- `CascadeControl/Views/CascadeCheckboxView.cs` inherits `CascadeView` and adds `CheckSet`, `CheckedChanged`, and `GetCheckBoxBounds`, while overriding `SetTree`.
- `CascadeControl/Views/CascadeTreeImageView.cs` inherits `ScrollableControl`, holds `CascadeView View`, reads `Tree` and `SelectedNode`, invokes `TrySelectNode`, `StepBack`, and `StepForward`, subscribes to `SelectedPathChanged` and `Disposed`, and downcasts to `CascadeCheckboxView` at four sites.
- `CascadeControlSelector/TreeSelector.cs` and `CascadeControlViewer/TreeViewer.cs` bind `treeImageView1.View = cascadeView1` in WinForms Designer files.
- `CascadeControl/CascadeControl.csproj` targets `.NET Framework 4.8` in legacy MSBuild format. New `.cs` files require manual `<Compile Include>` entries and XML documentation comments because `DocumentationFile` is enabled at warning level 4.
- `EventHandler<TEventArgs>` is invariant in C#. Unifying `NodeActivated` or `NodeMouseClick` across all three controls requires matching `EventArgs` types and is deferred until after Plan B2.

## Plan A target interfaces

Keep `ICascadeView` and `ICascadeCheckableView` free of `System.Windows.Forms` dependencies, matching `CascadeControl/Common/ICascadeNode.cs`.

```csharp
namespace CascadeControl.Views
{
	public interface ICascadeView
	{
		MenuTree Tree { get; }
		ICascadeNode SelectedNode { get; }
		IReadOnlyList<ICascadeNode> SelectedPath { get; }
		string SelectedPathText { get; }
		bool IsLeafSelected { get; }
		string PathSeparator { get; set; }

		void SetTree(MenuTree tree);
		void Clear();
		bool TrySelectNode(MenuNode node);
		bool TrySelectPath(IEnumerable<ICascadeNode> path);
		bool StepBack();
		bool StepForward();

		event EventHandler<CascadeNodeEventArgs> SelectedPathChanged;
		event EventHandler<CascadeNodeEventArgs> NodeActivated;
	}

	public interface ICascadeCheckableView : ICascadeView
	{
		MenuCheckSet CheckSet { get; set; }
		event EventHandler<MenuCheckChangedEventArgs> CheckedChanged;
	}
}
```

## Plan B2 architecture (confirmed)

### Design shapes compared

Two shapes were evaluated against three criteria: one owner per piece of state, one way to do each task, and minimal caller burden.

- **Shape B2-A (recommended), `CascadeState` and `CascadeCheckableState : CascadeState`.** `CascadeState` owns `Tree`, `TopLevelNodes`, `CascadeNavigator`, and `PathSeparator`. `CascadeCheckableState` extends `CascadeState` and owns `MenuCheckSet` lifecycle, replacing `CheckSet` atomically inside `SetTree` before `SelectedPathChanged` fires. Syncing any two controls takes one assignment (`treeImageView1.State = cascadeView1.State`), and `CascadeTreeImageView` detects check support via `State is CascadeCheckableState` even before `SetTree` runs.
- **Shape B2-B (rejected), `CascadeState` with separate `MenuCheckSet` binding.** `CascadeState` owns only tree and navigation, while `CascadeCheckboxView` and `CascadeTreeImageView` bind `MenuCheckSet` separately. This keeps `CascadeState` smaller, but callers must bind two objects and re-sync `imageView.CheckSet` after every `SetTree` call, which splits ownership of the `Tree` and `CheckSet` swap.

### Target state types and signatures (Shape B2-A)

Keep both types in `CascadeControl/Views/` with zero `System.Windows.Forms` dependency.

```csharp
namespace CascadeControl.Views
{
	internal enum CascadeStateInvalidation
	{
		Reset,
		Refreshed,
		SelectionChanged,
		TrailCleared,
	}

	public class CascadeState
	{
		public MenuTree Tree { get; }
		public IReadOnlyList<ICascadeNode> TopLevelNodes { get; }
		public ICascadeNode SelectedNode { get; }
		public IReadOnlyList<ICascadeNode> SelectedPath { get; }
		public string SelectedPathText { get; }
		public bool IsLeafSelected { get; }
		public int ActiveColumnIndex { get; }
		public string PathSeparator { get; set; }

		public virtual void SetTree(MenuTree tree);
		public virtual void SetTopLevelNodes(IEnumerable<ICascadeNode> topLevelNodes);
		public void Refresh(IEnumerable<ICascadeNode> selectPath = null);
		public void Clear();

		public bool TrySelectNode(MenuNode node);
		public bool TrySelectPath(IEnumerable<ICascadeNode> path);
		public bool StepBack();
		public bool StepForward();
		public void ClearTrail();

		public event EventHandler<CascadeNodeEventArgs> SelectedPathChanged;

		// Internal hooks for CascadeView column management.
		internal object TopLevelKey { get; }
		internal IReadOnlyList<ICascadeNode> PathWithTrail { get; }
		internal int SelectedDepth { get; }
		internal ICascadeNode GetTrailNodeAt(int columnIndex);
		internal bool SelectNodeAt(int columnIndex, ICascadeNode node);
		internal bool ClearSelection();
		internal event Action<CascadeStateInvalidation> Invalidated;
	}

	public class CascadeCheckableState : CascadeState
	{
		public MenuCheckSet CheckSet { get; set; }

		public override void SetTree(MenuTree tree);
		public override void SetTopLevelNodes(IEnumerable<ICascadeNode> topLevelNodes);

		public event EventHandler<MenuCheckChangedEventArgs> CheckedChanged;
		public event EventHandler CheckSetChanged;
	}
}
```

### How the three controls bind to state

- **`CascadeView`** initializes `_state = CreateDefaultState()` (returns `new CascadeState()`) and exposes `public CascadeState State { get; set; }` marked `[Browsable(false)]` and `[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]`. Setting `null` throws `ArgumentNullException`. All existing data and navigation members (`Tree`, `SelectedNode`, `SelectedPath`, `SelectedPathText`, `IsLeafSelected`, `ActiveColumnIndex`, `PathSeparator`, `SetTree`, `SetTopLevelNodes`, `RefreshColumns`, `Clear`, `TrySelectNode`, `TrySelectPath`, `StepBack`, `StepForward`, `ClearTrail`, `SelectedPathChanged`) delegate to `_state`. Column UI state (`_lists`, layout, colors, multi-select `_marks`, inline edit `_edit`, `ReadOnly`, `History`, context menus) remains local to `CascadeView`. On `Invalidated(Reset)` or `State` swap, `CascadeView` clears `History`, multi-selection, and `_anchorNode` and calls `SyncColumns()`. On `Invalidated(Refreshed)`, `CascadeView` clears multi-selection and column tags without clearing `History`. On `Dispose`, `CascadeView` detaches from `_state` without disposing it.
- **`CascadeCheckboxView`** overrides `CreateDefaultState()` to return `new CascadeCheckableState()` and overrides `OnStateChanged(oldState, newState)` to detach and attach `CheckedChanged` and `CheckSetChanged`. `CheckSet` delegates to `(State as CascadeCheckableState)?.CheckSet`; assigning a non-null `MenuCheckSet` while `State` is a plain `CascadeState` throws `InvalidOperationException`. When `CheckSetChanged` fires or a `CascadeCheckableState` attaches, `CascadeCheckboxView` wires `CheckSet.History = History`, clears `History`, and calls `InvalidateNodes()`. `OnTreeEdited` keeps calling `CheckSet?.UncheckRemoved(e.Nodes)` inside `CascadeView`'s `History.BeginGroup()` so cut or delete and uncheck remain a single undo step.
- **`CascadeTreeImageView`** initializes `_state = new CascadeState()`, exposes `public CascadeState State { get; set; }` (non-null), and removes the legacy `ICascadeView View` property so each view has one binding mechanism. It adds standalone navigation members delegating to `_state` (`Tree`, `SelectedNode`, `SelectedPath`, `SelectedPathText`, `IsLeafSelected`, `PathSeparator`, `SetTree`, `Clear`, `TrySelectNode`, `TrySelectPath`, `StepBack`, `StepForward`, `SelectedPathChanged`), allowing `CascadeTreeImageView` to run standalone or synced via `treeImageView1.State = cascadeView1.State`. It redraws automatically on `_state.SelectedPathChanged`, `checkableState.CheckedChanged`, and `checkableState.CheckSetChanged`, and shows the unchecked-display menu item whenever `_state is CascadeCheckableState`. Diagram-local state (`_image`, `DrawScope`, `UncheckedDisplay`, `SizeMode`, `_expandOverrides`, `TreeContextMenu`) stays local to `CascadeTreeImageView`.

### Confirmed B2 design decisions

1. **Delete `CascadeTreeImageView.View` in Plan B2 Stage 3.** Keep `CascadeState` a pure POCO without `Component` inheritance, and wire `treeImageView1.State = cascadeView1.State;` in `TreeSelector.cs` and `TreeViewer.cs` constructors.
2. **Keep `ICascadeView` unchanged during Plan B2.** Add standalone navigation APIs to `CascadeTreeImageView` in B2, and add `: ICascadeView` to `CascadeTreeImageView` in Post-B2 Event Unification when `NodeActivated` switches to `CascadeNodeEventArgs`.
3. **Share `PathSeparator` on `CascadeState`.** Synced views share the same separator, so `TreeViewer` (`cascadeView1.PathSeparator = "\\"`) applies `"\\"` to `treeImageView1` as well.
4. **Emit `CascadeStateInvalidation.SelectionChanged` before `SelectedPathChanged`.** Accepted small refinement to the saved sketch so every bound `CascadeView` synchronizes its columns before any external `SelectedPathChanged` handler runs.

## Roadmap and status

- [x] **Plan A, Stage 1.** Add characterization tests for `CascadeTreeImageView` in `CascadeControlTests/Common/TreeDrawerTests.cs` covering `View` attach, replace, and `Dispose` cleanup, node mouse click and double-click, expander click isolation, and `TreeContextMenu` visibility for plain and checkable views.
- [x] **Plan A, Stage 2.** Remove dead and duplicate code. Delete unused `CascadeView1_CheckedChanged` and ineffective `UncheckedDisplay` assignment in `CascadeControlSelector/TreeSelector.cs`, and consolidate `DEFAULT_PATH_SEPARATOR` to a single source.
- [x] **Plan A, Stage 3.** Add `ICascadeView.cs` and `ICascadeCheckableView.cs` under `CascadeControl/Views/`, register both in `CascadeControl/CascadeControl.csproj` with XML docs, implement `ICascadeView` on `CascadeView`, and implement `ICascadeCheckableView` on `CascadeCheckboxView`.
- [x] **Plan A, Stage 4.** Change `CascadeTreeImageView.View` from `CascadeView` to `ICascadeView` with `[TypeConverter(typeof(ReferenceConverter))]`, replace all `CascadeCheckboxView` casts with `ICascadeCheckableView`, subscribe to `Disposed` through `IComponent`, verify WinForms Designer converter behavior, and add pure C# fake-view tests.
- [x] **Plan B2 Architecture Gate.** Confirmed Shape B2-A (`CascadeState` and `CascadeCheckableState`), control binding rules, and the three design decisions above.
- [x] **Plan B2, Stage 1.** Add `CascadeState.cs` and `CascadeCheckableState.cs` under `CascadeControl/Views/`, register in `CascadeControl.csproj` with XML docs, and add pure non-UI tests in `CascadeControlTests/Views/CascadeStateTests.cs`.
- [x] **Plan B2, Stage 2.** Rewire `CascadeView` and `CascadeCheckboxView` to delegate data, navigation, and `CheckSet` ownership to `CascadeState` and `CascadeCheckableState`, keeping `History` and undo timeline behavior intact, and add multi-view sync tests.
- [x] **Plan B2, Stage 3.** Rewire `CascadeTreeImageView` to bind `CascadeState` with standalone navigation APIs, delete legacy `CascadeTreeImageView.View`, migrate `TreeSelector` and `TreeViewer` (and their Designer files) to `treeImageView1.State = cascadeView1.State`, remove redundant `treeImageView1.Redraw()` calls on `TreeEdited`, and replace `View`-coupling tests with standalone and shared-state tests.
- [x] **Plan B2, Stage 4.** Update `README.md` for `CascadeState`, `CascadeCheckableState`, and standalone `CascadeTreeImageView` usage, then run final verification.
- [ ] **Post-B2 Event Unification.** Unify `NodeActivated` and `NodeMouseClick` event arguments across all three views, add `: ICascadeView` to `CascadeTreeImageView`, and update `CascadeControlSelector`, `CascadeControlViewer`, tests, and `README.md`.

## Verification commands

Run these commands from the repository root `D:\github\CascadeControl`.

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" CascadeControl.sln /p:Configuration=Debug /nologo /v:minimal
& "C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe" CascadeControlTests\bin\Debug\CascadeControlTests.dll
```

## Stage log

- Baseline (2026-11-20): Debug build succeeded (4 projects, 0 errors); `vstest.console.exe` passed 422 of 422 tests in 13.37 seconds.
- Plan A, Stage 1 (2026-11-20): Added 4 characterization tests in `CascadeControlTests/Common/TreeDrawerTests.cs`; Debug build succeeded (0 errors, 0 new warnings); `vstest.console.exe` passed 426 of 426 tests in 12.99 seconds.
- Plan A, Stage 2 (2026-11-20): Removed unused `CascadeView1_CheckedChanged` and ineffective `UncheckedDisplay` assignment from `CascadeControlSelector/TreeSelector.cs`, and consolidated `DEFAULT_PATH_SEPARATOR` onto `CascadeNavigator`; Debug build succeeded (0 errors, 0 new warnings); `vstest.console.exe` passed 426 of 426 tests in 13.60 seconds.
- Plan A, Stage 3 (2026-11-20): Added `ICascadeView` and `ICascadeCheckableView` in `CascadeControl/Views/`, registered both in `CascadeControl/CascadeControl.csproj`, implemented them on `CascadeView` and `CascadeCheckboxView`, and added `InterfaceContracts_NavigateAndCheckThroughInterfaces` in `CascadeCheckboxViewTests.cs`; Debug build succeeded (0 errors, 0 new warnings); `vstest.console.exe` passed 427 of 427 tests in 13.67 seconds.
- Plan A, Stage 4 (2026-11-20): Switched `CascadeTreeImageView.View` to `ICascadeView` with `[TypeConverter(typeof(ReferenceConverter))]`, replaced all `CascadeCheckboxView` casts with `ICascadeCheckableView`, subscribed to `Disposed` via `IComponent`, and added 3 tests in `TreeDrawerTests.cs` covering pure C# `ICascadeView`, pure C# `ICascadeCheckableView`, and WinForms `ReferenceConverter`; Debug build succeeded (0 errors, 0 new warnings); `vstest.console.exe` passed 430 of 430 tests in 14.25 seconds.
- Plan B2 Architecture Gate (2026-11-20): Confirmed Shape B2-A (`CascadeState` and `CascadeCheckableState`), pure POCO `State` binding, deferred `CascadeTreeImageView : ICascadeView` until Post-B2, and shared `PathSeparator`.
- Plan B2, Stage 1 (2026-11-20): Added `CascadeState.cs` and `CascadeCheckableState.cs` in `CascadeControl/Views/`, registered both in `CascadeControl.csproj`, and added 6 pure non-UI tests in `CascadeControlTests/Views/CascadeStateTests.cs`; Debug build succeeded (0 errors, 0 new warnings); `vstest.console.exe` passed 436 of 436 tests in 13.11 seconds.
- Plan B2, Stage 2 (2026-11-20): Rewired `CascadeView` (and partials) and `CascadeCheckboxView` to delegate tree, navigation, and `CheckSet` ownership to `CascadeState` and `CascadeCheckableState` via `State` property, and added 2 multi-view sync and guard tests in `CascadeCheckboxViewTests.cs`; Debug build succeeded (0 errors, 0 new warnings); `vstest.console.exe` passed 438 of 438 tests in 13.85 seconds.
- Plan B2, Stage 3 (2026-11-20): Rewired `CascadeTreeImageView` to bind `CascadeState` with standalone navigation APIs, deleted legacy `View` property, migrated `TreeSelector` and `TreeViewer` (and Designer files) to `treeImageView1.State = cascadeView1.State`, removed redundant `treeImageView1.Redraw()` calls on `TreeEdited`, and replaced legacy `View` tests in `TreeDrawerTests.cs` with standalone and shared-state tests; Debug build succeeded (0 errors, 0 new warnings); `vstest.console.exe` passed 437 of 437 tests in 12.97 seconds.
- Plan B2, Stage 4 (2026-11-20): Updated `README.md` for `CascadeState`, `CascadeCheckableState`, `ICascadeView`, `ICascadeCheckableView`, and standalone or shared-state `CascadeTreeImageView` usage, and verified zero stale `CascadeTreeImageView.View` references; Debug build succeeded (0 errors, 0 new warnings); `vstest.console.exe` passed 437 of 437 tests in 13.18 seconds. Plan B2 complete; waiting for user confirmation before Post-B2 Event Unification.
