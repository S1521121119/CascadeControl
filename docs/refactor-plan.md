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

## Roadmap and status

- [x] **Plan A, Stage 1.** Add characterization tests for `CascadeTreeImageView` in `CascadeControlTests/Common/TreeDrawerTests.cs` covering `View` attach, replace, and `Dispose` cleanup, node mouse click and double-click, expander click isolation, and `TreeContextMenu` visibility for plain and checkable views.
- [ ] **Plan A, Stage 2.** Remove dead and duplicate code. Delete unused `CascadeView1_CheckedChanged` and ineffective `UncheckedDisplay` assignment in `CascadeControlSelector/TreeSelector.cs`, and consolidate `DEFAULT_PATH_SEPARATOR` to a single source.
- [ ] **Plan A, Stage 3.** Add `ICascadeView.cs` and `ICascadeCheckableView.cs` under `CascadeControl/Views/`, register both in `CascadeControl/CascadeControl.csproj` with XML docs, implement `ICascadeView` on `CascadeView`, and implement `ICascadeCheckableView` on `CascadeCheckboxView`.
- [ ] **Plan A, Stage 4.** Change `CascadeTreeImageView.View` from `CascadeView` to `ICascadeView` with `[TypeConverter(typeof(ReferenceConverter))]`, replace all `CascadeCheckboxView` casts with `ICascadeCheckableView`, subscribe to `Disposed` through `IComponent`, verify WinForms Designer converter behavior, and add pure C# fake-view tests.
- [ ] **Plan B2 Architecture Gate.** Run `/pstack:architect` to design the shared state object, control bindings for all three views, and migration steps. Stop for user confirmation before editing code.
- [ ] **Plan B2 Implementation.** Execute the confirmed Plan B2 migration stages.
- [ ] **Post-B2 Event Unification.** Unify `NodeActivated` and `NodeMouseClick` event arguments across all three views, then update `CascadeControlSelector`, `CascadeControlViewer`, tests, and `README.md`.

## Verification commands

Run these commands from the repository root `D:\github\CascadeControl`.

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" CascadeControl.sln /p:Configuration=Debug /nologo /v:minimal
& "C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe" CascadeControlTests\bin\Debug\CascadeControlTests.dll
```

## Stage log

- Baseline (2026-11-20): Debug build succeeded (4 projects, 0 errors); `vstest.console.exe` passed 422 of 422 tests in 13.37 seconds.
- Plan A, Stage 1 (2026-11-20): Added 4 characterization tests in `CascadeControlTests/Common/TreeDrawerTests.cs`; Debug build succeeded (0 errors, 0 new warnings); `vstest.console.exe` passed 426 of 426 tests in 12.99 seconds. Waiting for user confirmation before Stage 2.
