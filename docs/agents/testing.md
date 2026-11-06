# Testing and visual verification

Read the [repository instructions](../../AGENTS.md) first. Paths and command examples below are relative to this repository root unless another working directory is specified. Builder CLI and self-test commands run from the builder root.

Also read [Development](development.md) for executable selection, CLI flags, Python setup and settings backups. For focused MM2 checks, read the relevant topics from the [core index](../../Files/cores/MidtownMadness2/AGENTS.md#read-before-working).

## Self-tests (the test suite)

All self-tests write a log **next to** the given folder (not inside it) and exit 1 on failures. Use folders in a scratch directory and delete a previous run's folder first (`city.new` refuses an existing city).

### `selftest api <new folder>` (`Scripts/Api/ApiSelfTest.cs`)

Creates an MM2 SF city and drives it only through JSON API commands. It covers elements, anchors, terrain, undo/redo, transactions, generators, Python/Lua scripting, type inheritance, merge/split, seamless intersections, decals, export layout/hudmap, `.ar` packing, rendering, validation, MCP and custom elements. Writes `api_selftest.log`.

```
<godot console exe> --path . --resolution 1152x648 -- selftest api "<scratch>/api/city"     # windowed (also saves screenshots)
<godot console exe> --headless --path . -- selftest api "<scratch>/api/city"               # headless (skips the view checks)
```

**Baseline (2026-11-03): 1353 OK windowed, 1 failure, ~115 s windowed** (headless skips the view checks). The failure is `normal export emits one native line and every individual placer prop`, only in the full run. Some sections (`races`, `trains`, `boats`) run only with `KM2_SELFTEST_FOCUS`. Known and accepted: the full `city.export` reports an `export_failed` error on "Intersection 1" (PSDL perimeter infinite loop at the 3-road origin intersection). The test checks that the error names it and that PSDL/INST/BAI are still written.

- `InheritedTypes` temporarily creates `Files/cores/MidtownMadness2/elements/roads/zzInheritTest`. If a run is killed, delete that folder by hand.
- A run with failures has exited with 132 (SIGILL) during Godot's shutdown finalizers. Clean runs exit 0.
- A run that stops without an error (no log, the window idle) is an API reply that never comes: `SettleProjectedSplines` (awaited by the dispatcher after each command while projected splines exist) waits until an update rebuilds no road, intersection or patch, so an element reporting a change at every update stalls it (e.g. a border mesh state never flagged unchanged). Run without `--quiet` to see the `[api-selftest]` lines up to the stuck step.
- Empty ObjectState arrays vanish on JSON re-read, so compare `[]` and missing as equal.
- `city.exportJson` ids are indices in the manager lists, not API ids. Ids change after a reload, so re-list (or compare by name).
- An API `Call` flushes and resets the elements' editing state, and a change closes the open editors (as before an undo). To test a GUI action with undo, run the GUI steps first, then `city.flush`, then take the snapshots (`CheckGuiUndo` in `BaiConnections`), not `CheckUndo` around them.
- The custom element panel gets its element from the click mode (`GetPlacer()` is `terrainClick.modifier as CustomElementPlacer`): a temporary click mode (e.g. `ReferencePicker`) must restore the previous one before changing the element. While the point transform panel (M) is open the modifier is null; `CityBuilderMenuBar.ControlPointOwnerCustomElement` gives the element.
- Every menu entry and panel button runs inside `UndoManager.Record` (`IsBusy` is true), so a `RunLong` API call made synchronously from a GUI action fails with "another operation is running". GUI code must yield a frame first, and tests of GUI actions should go through `UndoManager.Record` like the menu does.
- Panel checks: call `panel.Update()`, not `placer.onFrame` (which returns early when nothing changed).
- Read core settings like `allowYaw`/`allowRoll` from the type instead of hardcoding them; the maintainer toggles them locally.

### `selftest undo <folder> [--mode new|props|ui|split|load|drag|dropdown]` (`Scripts/History/UndoSelfTest.cs`)

Writes `undo_selftest.log` and restores `LastCity`.

| Mode | City | Notes / baseline |
|---|---|---|
| `new` (default) | creates one | **35 pre-existing failures** (split/merge steps of straight Hermite and low-poly roads), not a regression |
| `split` | creates one | 0 failures, ~50 s. **Must run windowed.** `SplitModeTest` is flaky on window geometry, so rerun once before blaming a change |
| `drag` | creates one | numeric field middle-drag, 0 failures, ~15 s. Captures the real mouse, so a moving mouse can fail the Shift check; rerun once |
| `dropdown` | creates one | dropdown keys/wheel, 0 failures, ~10 s |
| `height` | creates one | placement height (Shift + mouse movement), hidden terrain placement and snapping toggle, 30 OK, 0 failures, ~20 s. **Must run windowed.** Moves the real mouse, so rerun once before blaming a change |
| `guide` | creates one | the guides (`UndoSelfTestGuide.cs`): Markdown parser, **all the guides of the installed cores** (chapters, links, images), refused links, panel navigation, move/resize/minimize, input. 142 OK, 0 failures, ~10 s. **Must run windowed.** Moves the real mouse. Saves frames in `guide_fixtures` next to the log: look at them after a change to the renderer, the checks can't see the layout |
| `ui`, `load`, `props` | **a copy** of an existing city | the old test city is gone; use a copy of the api self-test city (`<scratch>/api/city`) or ask the maintainer for one |

Load/ui modes catch bugs that the scratch city misses. Reproduce user-reported bugs on a copy of a real city.

Simulating input in self-tests:

- `Input.ActionPress` does **not** work for clicks (the await continuations run after `_Process`), and `InputEventAction` counts as consumed by the UI. Use `WarpMouse` + `Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = Left, Pressed = true, Position/GlobalPosition = mouse })`, then release.
- Terrain modifiers need `Input.ActionPress("Select")` to apply.
- Panel buttons: reflection on `EditorPanelElements.Button`'s private `button`, then `EmitSignal(Pressed)` (goes through the undo recording). Reflection doesn't find a base class's private fields through the derived type, so use `typeof(Base).GetField`.
- Panels may be rebuilt when an element is selected: read panel fields after `SelectObject`.
- A test that deletes elements must call `M.FlagAsChanged()` like the editor does.
- Compare positions with a tolerance (projection drift).
- Editor key commands are polled globally (`GameEngine.Input`). Block them while a popup is open (`UIDetection.PopupOpen()`).

### Other

- `-- screenshots <copy of a city>` (`Scripts/UI/GuideScreenshots.cs`) regenerates the quickstart guide images and **overwrites the tracked `wiki/images`**. Revert them if they were only for a visual check.
- Bugs that only show in the normal GUI startup (LastCity auto-load) can't be seen by the self-tests, because CLI loads reset the core manager. Launch the GUI normally and grep the log.
- Visual check of a change: `-- render <city> --out x.png --camera top|custom ...` (windowed). Thin lines follow the editor camera distance, so set it with `view.camera.set` before `view.render`.
