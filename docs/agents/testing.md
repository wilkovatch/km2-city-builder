# Testing and visual verification

Read the [repository instructions](../../AGENTS.md) first. Paths and command examples below are relative to this repository root unless another working directory is specified. Builder CLI and self-test commands run from the builder root.

Also read [Development](development.md) for executable selection, CLI flags, Python setup and settings backups. For focused MM2 checks, read the relevant topics from the [core index](../../Files/cores/MidtownMadness2/AGENTS.md#read-before-working).

## Self-tests (the test suite)

All self-tests write a log **next to** the given folder (not inside it) and exit 1 on failures. Use folders in a scratch directory and delete a previous run's folder first (`city.new` refuses an existing city).

### Core-owned API and undo suites

`selftest api <new folder> [--core <core>] [--focus <focus>]` and `selftest undo <folder> [--core <core>] [--mode <mode>]` compile and run the selected core's developer tests inside Godot. They require a builder source checkout, the .NET SDK and that core's `tests/SelfTests.csproj`. A normal builder build does not compile any core test sources.

An existing city's `preferences.json` selects its core. Otherwise the only installed core is the default; with several cores, pass `--core`. Modes and focuses are supplied by the core, and unknown values fail instead of running the full suite. `--focus` overrides the legacy `KM2_SELFTEST_FOCUS`, `KM2_SELFTEST_INCREMENTAL` and `KM2_SELFTEST_MESH_SELECTION` switches.

The runner builds incrementally under `.godot/selftests/<core>`, loads only after a successful build, restores `settings.json` in `finally`, writes `<suite>_selftest.log` next to the scratch folder and returns 1 for test/build failures or 2 for invalid selectors. Do not run suites concurrently: they share the editor, settings and input devices. Use windowed Godot for rendered, picking and input checks. Existing-city modes must receive disposable copies.

Mouse-driven checks need desktop access. If Windows `GetCursorPos` fails with error 5 inside a sandbox, Godot can return invalid mouse coordinates and every warped click/drag fails. Verify the denial and rerun those modes with desktop access before treating the failures as editor regressions. A startup signal 11 before any test output also needs a logged rerun (`--log-file <scratch>/godot.log`).

Core authoring and the contract are in [Core Testing](../../wiki/Core-Testing.md). For MM2 scenarios, baselines, fixture pitfalls and visual checks, read the core's [test instructions](../../Files/cores/MidtownMadness2/docs/agents/testing.md).

### Runner regression checks

After changing the loader or contract, run `python Tests/test_core_runner.py --godot "<console executable>"`. This uses a temporary synthetic core and checks cold compilation, source recompilation, invalid selectors, saved-core selection, missing projects/SDK, stale-binary rejection and settings restoration. It writes logs under `.godot/selftests/runner-checks-*` and removes its temporary core. Keep the SDK in the environment and do not run this alongside another suite.

### Other

- `-- screenshots <copy of a city>` (`Scripts/UI/GuideScreenshots.cs`) regenerates the quickstart guide images and **overwrites the tracked `wiki/images`**. Revert them if they were only for a visual check.
- Bugs that only show in the normal GUI startup (LastCity auto-load) can't be seen by the self-tests, because CLI loads reset the core manager. Launch the GUI normally and grep the log.
- Visual check of a change: `-- render <city> --out x.png --camera top|custom ...` (windowed). Thin lines follow the editor camera distance, so set it with `view.camera.set` before `view.render`.
