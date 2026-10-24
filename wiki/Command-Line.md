# Command Line

The program can be run from a terminal, a batch file or another program to create, inspect, edit, generate and export cities without the editor. The commands are built on the [Scripting API](Scripting-API): every API method can be called, either directly or through the task commands below.

```
km2-city-builder.console.exe --headless --quiet -- <command> [arguments] [options]
```

- The arguments before `--` belong to the engine: `--headless` runs the program without a window, `--quiet` keeps the messages of the engine out of the standard output. Everything after `--` is the command.
- On Windows use the `.console.exe` executable, which shows the output in the terminal.
- To run the project from the Godot editor binary instead of an exported build: `Godot_v4.7.1-stable_mono_win64_console.exe --path <project folder> --headless --quiet -- <command> ...`. With `--path` the working directory is the project folder, so the relative paths given to the commands are relative to it.
- `help` lists the commands, `help <command>` (or `--help` after a command) shows the arguments and options of one.

When a command is given, the last city is not loaded at startup and nothing is asked with popups: the command loads the city it works on, and the program quits when it is done. Without a command the program starts as usual.

## Commands

| Command | What it does |
|---|---|
| `help [command]` | The commands, or the arguments and options of one |
| `version` | The version of the program and of the API protocol (`api.version`) |
| `methods [prefix] [--json]` | The API methods (optionally the ones starting with the prefix), with their parameters |
| `call <method> [name=value ...]` | Runs an API method and prints its result |
| `run <file or ->` | Runs the JSON commands of a file (`-`: of the standard input), one per line, and prints the responses |
| `serve` | Reads JSON commands from the standard input and writes the responses to the standard output, until the end of the input |
| `cores` | The installed cores (`cores.list`) |
| `templates <core>` | The city templates of a core (`cores.templates`) |
| `new <folder> --core <core> --template <template>` | Creates a city from a template (`city.new`) |
| `info <city>` | The city (`city.info`) and the exporters of its core with their flags (`exporters.list`) |
| `export <city> --exporter <format> --out <path>` | Exports the city with an exporter of its core (`city.export`) |
| `export-json <city> --out <file>` | Writes the export JSON of the city (`city.exportJson`) |
| `generate terrain <city>` | Creates terrain patches between the roads (`city.generateTerrain`) |
| `generate buildings <city> --line-preset <preset> --building-presets <a,b,...>` | Creates building lines around the blocks (`city.generateBuildings`) |
| `script <.py file or script folder>` | Runs a Python script (see [Python Scripts](Python-Scripts#command-line)) and exits with its exit code |
| `selftest api <new folder>` | Self-test of the scripting API (development, see [Scripting API](Scripting-API#self-test)) |
| `selftest undo <folder> [--mode <mode>]` | Self-test of undo and redo (development). Modes: `new` (default) and `split` create a city in the folder, `props`, `ui` and `load` work on a copy of an existing city |
| `screenshots <city folder>` | Takes the screenshots of the quickstart guide on a copy of a city (development, needs the window) |

`<city>` is the folder of a city (the one containing `city.json.gz`).

### Parameters as options

The options of `new`, `export`, `export-json` and `generate` are the parameters of their API method, written in lowercase with dashes: `terrainSize` is `--terrain-size`, `heightmapResolution` is `--heightmap-resolution`. `help <command>` lists them with their types and defaults, and new API parameters show up there by themselves. The boolean parameters are flags: `--null-only` sets `nullOnly` to true, `--subdivide=false` sets `subdivide` to false. `export` takes the output path as `--out` (the `path` of `city.export`), and its flags as `--flag id=true` or `--flag id=false`, repeated for each flag (`info` lists the flags of the exporters).

### Common options

| Option | Commands | Effect |
|---|---|---|
| `--city <folder>` | `call`, `run`, `serve`, `script` | Loads the city first (`city.load`) |
| `--migrate` | the commands loading a city | Migrates a city made with another version of its core (its files are changed); without it, such a city is not loaded |
| `--install-python-modules` | the commands loading a city | Installs the Python modules the core needs if missing |
| `--save` | `call`, `run`, `generate` | Saves the city if the command succeeds (otherwise the changes are lost when the program quits) |
| `--no-history` | `call`, `run`, `serve`, `generate` | Doesn't record the changes as undo steps (`api.configure` with `history: false`): faster for large changes |
| `--output <file>` | the commands printing a result | Writes the result to a file instead of the standard output |
| `--pretty` | the commands printing a result | Indents the JSON |
| `--progress` | the commands printing a result | Writes the progress of the long operations (loading, generation, export) to the standard error (`run` and `serve` write the progress events with the responses) |

## Calling API methods

`call` takes the method and its parameters as `name=value` arguments (quoted here as in cmd, see below):

```
call roads.create "points=[{\"position\":[0,0,0],\"onGround\":true},{\"position\":[200,0,0],\"onGround\":true}]" --city C:/cities/test --save
call elements.list kind=road --city C:/cities/test
call elements.rename ref=12 "name=Main street" --city C:/cities/test --save
```

- The string parameters take the text as it is (`name=123` is the name `"123"`).
- The other parameters read the text as JSON: numbers, `true`/`false`, arrays and objects. A text that isn't valid JSON is a string, so `ref=12` is the element with the id 12 and `ref=Main street` the element with that name (`ref={"name":"12"}` for an element named `12`).
- `name=@file.json` reads the value from a file.
- `--params '{...}'` or `--params @file.json` gives all the parameters as a JSON object; the `name=value` arguments override them.

The quoting of the JSON values depends on the shell. In cmd and in Windows PowerShell 5.1 the double quotes inside an argument must be escaped as `\"`: `"points=[{\"position\":[0,0,0]}]"` (cmd), `'points=[{\"position\":[0,0,0]}]'` (PowerShell 5.1). In PowerShell 7 and in bash single quotes are enough: `'points=[{"position":[0,0,0]}]'`. A file given with `@` avoids the quoting in every shell.

The parameters are checked before anything runs: an unknown method or parameter is a command line error. `methods` lists the methods and their parameters; the [method reference](Scripting-API#method-reference) describes them.

Each `call` is a separate run of the program, which loads the city again. For several commands use `run` or `serve`.

## Command files

`run` takes a file with a JSON command per line, in the format of the [protocol](Scripting-API#protocol). Empty lines and lines starting with `#` are skipped, and a command without `id` gets the number of its line:

```
# commands.jsonl
{"method": "roads.create", "params": {"points": [{"position": [0, 0, 0], "onGround": true}, {"position": [200, 0, 0], "onGround": true}]}}
{"method": "city.generateTerrain", "params": {"distance": 5}}
{"method": "city.save"}
```

```
run commands.jsonl --city C:/cities/test
```

The responses (and the progress events of the long commands) are printed one per line. `run` stops at the first error, with exit code 1; with `--keep-going` it runs the remaining commands, and still exits with 1. `--save` saves the city at the end if no command failed.

## Driving the program from another program

`serve` reads JSON commands from its standard input and writes the responses and events to its standard output, one per line, in the same protocol the Python scripts use. Any language that can start a process and read its output can use the whole API. The commands can be sent right away: they wait until the program has started. They run one at a time, in order. When the standard input is closed, the queued commands are completed and the program quits.

A client in Node.js:

```js
const { spawn } = require("child_process");
const readline = require("readline");

const cb = spawn("km2-city-builder.console.exe", ["--headless", "--quiet", "--", "serve"]);
const pending = new Map();
let nextId = 0;

readline.createInterface({ input: cb.stdout }).on("line", line => {
    const msg = JSON.parse(line);
    if (msg.event) return console.error(`${Math.round(msg.value * 100)}% ${msg.text}`);
    const { resolve, reject } = pending.get(msg.id);
    pending.delete(msg.id);
    msg.error ? reject(new Error(msg.error.code + ": " + msg.error.message)) : resolve(msg.result);
});

function call(method, params = {}) {
    const id = ++nextId;
    cb.stdin.write(JSON.stringify({ id, method, params }) + "\n");
    return new Promise((resolve, reject) => pending.set(id, { resolve, reject }));
}

(async () => {
    await call("city.load", { folder: "C:/cities/test" });
    console.log(await call("elements.list", { kind: "road" }));
    cb.stdin.end();
})();
```

## Output and exit codes

- The standard output only has the output of the command: the result of `call` and of the task commands as JSON, the responses of `run` and `serve` one per line, the text of `help` and `methods`. It can be read by another program as it is.
- The messages of the program and the errors go to the standard error. A failed command prints `error [code]: message`, with the [error code](Scripting-API#errors) of the API.
- Exit codes: `0` done, `1` failed (an API error, or a self-test with failures), `2` invalid command line (unknown command or option, missing argument). `script` exits with the exit code of the script.

## Examples

Export a city with the PSDL exporter of the Midtown Madness 2 core, without the BAI:

```
km2-city-builder.console.exe --headless --quiet -- export C:/cities/test --exporter psdl --out C:/out/test --flag bai=false --progress
```

The same, packed in a single `C:/out/test.ar` (the `ar` format of the exporter sets its `packAr` flag):

```
km2-city-builder.console.exe --headless --quiet -- export C:/cities/test --exporter ar --out C:/out/test --flag bai=false --progress
```

Create a city, generate its terrain and buildings, and save it:

```
km2-city-builder.console.exe --headless --quiet -- new C:/cities/new --core MidtownMadness2 --template SF --terrain-size 2048
km2-city-builder.console.exe --headless --quiet -- run roads.jsonl --city C:/cities/new --save
km2-city-builder.console.exe --headless --quiet -- generate terrain C:/cities/new --distance 5 --save
km2-city-builder.console.exe --headless --quiet -- generate buildings C:/cities/new --line-preset Default --building-presets Default --save
```

## Internals

The classes are in `Scripts/Cli`:

| Class | Purpose |
|---|---|
| `CommandLine` | Parses the arguments after `--` (`OS.GetCmdlineUserArgs`) once, against the command table, into an `Invocation`: command, positional arguments, options, `name=value` pairs, or the error. `IsCliSession` tells the startup (`CityGroundHelper`) not to load the last city nor check Python, and `GameEngine.Print` to write to the standard error. It also converts the texts to the parameter values (`ToValue`). |
| `CliCommands` | The command table: arguments, options, the API method whose parameters are options, and the code of each command. Generates the help from the table and from the method metadata of `CommandDispatcher`. |
| `CliRunner` | The node added by `CityBuilderMenuBar.ManualStart` in a command line session: it waits for the program to start, runs the command and quits with its exit code (the self-tests and the screenshots quit by themselves). `CliContext` runs the API methods through `CommandDispatcher.Execute`, loads the city (`--city`, `--migrate`...) and saves it (`--save`). |
| `CliOutput` | Writes the results to the standard output (or to `--output`) and the errors and progress to the standard error, directly to the streams of the process, so that `--quiet` doesn't hide them. |

`serve` reads the standard input on a thread and queues each line to `CommandDispatcher` with `Enqueue`, like `ScriptRunner` does with the output of the Python scripts.
