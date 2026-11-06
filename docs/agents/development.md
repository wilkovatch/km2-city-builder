# Building, running and exporting

Read the [repository instructions](../../AGENTS.md) first. Paths and command examples below are relative to this repository root unless another working directory is specified. Builder CLI and self-test commands run from the builder root.

Before running tests or checking rendered output, also read [Testing](testing.md). For game-specific exporters, read that core's entry file and exporter topic.

## Building and running

- Godot binary: Godot 4.7.1 .NET (`Godot_v4.7.1-stable_mono_win64_console.exe` on Windows; use the `_console` exe for command-line work). The install path is in `CLAUDE.local.md`.
- Build: `dotnet build "km2 City Builder.csproj"`. The build output is localized in Italian ("Compilazione completata"), so grep for `error CS` instead of "Build succeeded".
- **New `.cs` files need their `.cs.uid`** (tracked in git). Generate it with `<godot> --headless --path . --import`.
- Command line: `<godot> [--headless] --path . --quiet -- <command> ...` (see `wiki/Command-Line.md`).
  - Any command after `--` skips the Python check and the LastCity auto-load (`Cli.CommandLine.IsCliSession`).
  - `--path` makes Godot chdir into the project, so relative paths resolve against the project folder. Clean up leftovers.
  - With `--headless --quiet`, stdout carries only the command output (`GameEngine.Print` goes to stderr in CLI sessions).
  - Bool options are bare flags (`--wireframe`, not `--wireframe true`).
  - `render`, `mcp`, `screenshots` and windowed self-tests **need a window** (no renderer with `--headless`: `view.render` fails with `no_renderer`). Keep `--quiet`. `CliRunner.HideWindow` moves the window off-screen.
  - Git Bash quirk: piping the Godot console exe into a native Windows program (`| python ...`) gives it an empty stdin. Use `| cat`, a file, or `cmd /c "... | python ..."`.
  - PowerShell 5.1 strips inner double quotes of native args: escape as `\"` or use `@file`.
- Headless startup logs a pre-existing `ArgumentException` from `DebugLogPopup.UpdatePosition` (zero-size viewport). It is not a failure.
- The bundled Python needs the core requirements (e.g. Pillow for the hudmap). CLI sessions skip the Python check, so install them by hand: `python/python.exe -m pip install -r Files/cores/<Core>/pythonInfo/requirements.txt`.
- Back up `settings.json` before tests that may touch `LastCity` or other settings.

## Exporters

Exporters are Python scripts in the cores; changes to them usually touch the core repo too (see the core's `AGENTS.md`). `-- export-json <city> --out x.json` dumps the city in the exporter input format, to run a core exporter directly without the editor.

- Without a console, `PythonManager.RunScriptTask` feeds newlines on stdin (exporters may end with `input()`).
- A Python traceback goes to stderr, which neither `pythonLog.txt` nor the API `output` captures.
- Reported errors fail any export. Validation (`city.validate`, the `validate` CLI command) is a dry export driven by the exporter `settings.json` `validation.flags`.
