# Core Testing

API and undo tests are developer tools. Each core owns the scenarios, fixture setup and expectations for its game. The builder supplies the loader, the test contract and optional shared support. The implementation and documentation were written with Claude Code and other AI agents.

## Running tests

Build the builder and run Godot from its source checkout. The core must be a full checkout (a plain `git clone`): installs from the core manager leave out `tests/` (see `developerPaths` in [Core Structure](Core-Structure#settingsjson)).

```text
<godot console executable> --path . -- selftest api <new scratch folder> --core <core> --focus <focus>
<godot console executable> --path . -- selftest undo <scratch folder> --core <core> --mode <mode>
```

An existing city's `preferences.json` selects its core; an explicit conflicting `--core` is rejected. Otherwise the only installed core is the default. With several installed cores, choose one explicitly. Existing-city modes need disposable copies, because scenarios can edit and save them.

Modes and focuses belong to the provider. `--focus` overrides `KM2_SELFTEST_FOCUS`; an explicitly empty focus selects the normal suite even with legacy selectors set. The older incremental and mesh-selection environment switches remain available when neither selector is set. Benchmarks are explicit focused runs, outside the normal suite. Run windowed for renderer, picking and input checks; headless scenarios must report their skipped checks.

On each invocation the runner calls `dotnet build`, which recompiles changed test sources incrementally. It references the currently running builder and its existing dependencies, without rebuilding or copying the builder. Output and intermediate files go under `.godot/selftests/<core>`. A build failure reports the compiler diagnostics and never loads an older DLL. The .NET SDK and builder sources are required; normal startup and release builds do not build or load tests.

The runner restores the original `settings.json` bytes even after a provider exception, writes `<suite>_selftest.log` next to the supplied folder, and returns 0 for success, 1 for test/build failures or 2 for invalid selectors. Do not run suites concurrently: they share settings, the editor and real input devices.

## Adding a provider

Create `tests/.gdignore` and `tests/SelfTests.csproj` in the core:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <AssemblyName>CoreSelfTests</AssemblyName>
  </PropertyGroup>
  <Import Project="$(BuilderTestSupport)" />
</Project>
```

`BuilderTestSupport` and `BuilderAssemblyDirectory` are supplied by the runner. The shared props reference the running builder's managed assemblies and compile the optional shared `CoreTestSuite` helper into the test assembly. New cores use the same assembly identity, `CoreSelfTests`; only one selected core is loaded in a test process. The builder grants this generic developer assembly internal access so integration checks can exercise editor diagnostics without widening the public API. Reflection remains useful for private history diagnostics.

Implement exactly one public, parameterless `CoreSelfTests.ICoreTestProvider`. Its `suites` property returns `CoreTestSuiteInfo` records with `name` (`api` or `undo`), supported `modes`, `focuses` and `existingCityModes`. An undo suite includes `new`, the default mode. Its `Task RunAsync(CoreTestContext context)` executes the requested suite on the Godot main thread.

The context supplies `builder`, `core`, `coreFolder`, the absolute scratch `folder`, `suite`, `mode` and `focus`, plus `Log`, `Frames` and a failure count. Increment `context.failures` for failed assertions; unexpected exceptions are caught by the host. Return a task all the way through the scenario rather than starting an `async void` operation. The runner owns final logs, settings restoration and exit codes, so providers must not quit Godot themselves.

Scenarios are ordinary C# classes, with no Godot script registration required. They can derive from the shared `CoreTestSuite` for logging, frame/signal and viewport helpers. The builder explicitly excludes `Files/cores/**/*.cs` and `Tests/**/*.cs` from its assembly; `.gdignore` excludes developer sources and projects from Godot resources. No generated test DLLs belong in either repository.

## Checking the runner

`python Tests/test_core_runner.py --godot "<console executable>"` creates a temporary synthetic core and checks cold compilation, source recompilation without rebuilding the builder, selector validation, existing-city core selection, missing projects/SDK, settings restoration and rejection of stale binaries after a failed build. It removes the temporary core and keeps logs under `.godot/selftests/runner-checks-*`.
