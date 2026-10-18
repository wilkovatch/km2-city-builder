# Settings, Paths and Python

This page covers the classes that manage configuration files, file lookup, translated strings, installed cores and the Python processes. Source files are in `Scripts/IO/Settings`, `Scripts/IO/Import/PathHelper.cs`, `Scripts/UI/StringManager.cs` and `Scripts/PythonManager.cs`.

## Configuration files

| File | Scope | Managed by |
|---|---|---|
| `<base>/settings.json` | Whole program (created from `defaultSettings.json` in builds) | `SettingsManager` |
| `<city>/preferences.json` | One city | `PreferencesManager` |
| `<base>/Files/cores/<core>/settings.json` | One core | `CoreManager` |
| `<base>/coreRepos.json` | List of known core repositories | `CoreManager` |

### Keys

**`settings.json`**

| Key | Type | Meaning |
|---|---|---|
| `pythonPath` | string | Folder (or executable) of the Python interpreter. Empty: the embedded interpreter in `<base>/python`. |
| `pythonPort` | int | Port of the local Python server (default 5005). |
| `pythonDebug` | bool | Show the console windows of the Python processes (on Windows; elsewhere the server output goes to the program's standard output). |
| `extraFolders` | string[] | Asset folders searched by every city (see [`PathHelper`](#pathhelper)). |
| `LastCity` | string | Path of the last opened `city.json.gz`, reopened at startup. |
| `Language` | string | String table language (default `en-US`, no UI to change it yet). |

**`preferences.json`** (one per city)

| Key | Type | Meaning |
|---|---|---|
| `core` | string | Folder name of the core the city uses. |
| `coreVersion`, `coreFeatureVersion` | int | Core version the city was last saved with (see [Core Structure](Core-Structure#versions)). |
| `extraFolders` | string[] | Asset folders searched by this city only. |
| `overlayTex`, `OverlayActive`, `TransparentOverlay` | | Ground overlay image and its display state. |
| `curTerrainTexture` | string | Last texture used for terrain patches. |
| `renderingMode`, `shadowsEnabled`, `propsCullingDistance` | | Graphics options from the project settings panel. |

## `GenericSettingsManager`

Wraps one JSON file as a `Dictionary<string, object>`. `Get<T>(key, default)` converts JSON numbers to `int`/`float` as needed and returns the default when the key is missing or has the wrong type; `GetList<T>` does the same for arrays. `Set` and `SetList` save the file immediately.

## `SettingsManager`, `PreferencesManager`, `CoreManager`

Static facades over a lazily created `GenericSettingsManager`:

* **`SettingsManager`**: `<base>/settings.json`.
* **`PreferencesManager`**: `<workingDirectory>/preferences.json`. `workingDirectory` is the folder of the open city (empty when no city is open). `Load(full)` rereads the file and, when `full` is true, also reloads the string tables, the texture formats, restarts the Python server for the city's core and creates the core's common folder. `Unload` forgets the instance when a city is closed.
* **`CoreManager`**: reads the settings of the *current* core (`Get`, `GetList`, for example `customMeshFormats`) and manages installed cores:
  * `GetCores(includeUninstalled)` returns the display names, the folder names and a `CoreInfo` for each core (name, folder, repository, installed, stock, version). It lists the folders in `Files/cores` (display name from the core's `CORE_NAME` string, version from its `settings.json` plus the git commit) and, if requested, the repositories in `coreRepos.json` that are not installed yet.
  * `InstallCore` clones a repository (with submodules) into `Files/cores/<folder>` using LibGit2Sharp, asking for a user name and token if the remote requires authentication. `AddCore` installs from a URL typed by the user, into a folder named after the repository (`https://host/user/MyCore.git` goes to `Files/cores/MyCore`). It fails, without changing anything, if that folder already exists or the name is not a valid folder name.
  * `CheckUpdates` fetches the remote, `CheckLatestCommit` reports whether the local clone is behind and whether it has local changes, `UpdateCore` merges the tracked branch and resets the submodules to the recorded commits. A core with local changes is never updated automatically.
  * `UninstallCore` deletes the folder (clearing read-only attributes set by git first).
  * `GetCoreVersion` / `GetCoreFeatureVersion` read `coreVersion` / `coreFeatureVersion` of any installed core.

## `PathHelper`

File lookup for the assets used by a city.

| Method | Description |
|---|---|
| `BasePath()` | The project folder in the editor, the executable's folder in a build. |
| `FilesPath(basePath)` | `<base>/Files/cores/<current core>/`, or `<base>/Files/` when `basePath` is true or no core is selected. |
| `LocalFilesPath()` | The city folder (or `<base>/Files/` when no city is open). |
| `CoreAvailable()` | Whether the open city has a core. |
| `CreateCommonFolder()` | Creates `Files/common/<core>/` from the core's `baseCommon` folder the first time a city of that core is loaded. |
| `FindInFolders(name)` | Resolves a relative asset path: the city folder first, then every extra folder (global ones from `settings.json`, then the city's ones from `preferences.json`). An extra folder starting with `.` is relative to `Files/common` (for example `.\MidtownMadness2\` is `Files/common/MidtownMadness2/`; backslashes are treated as separators on every system). On case-sensitive file systems a file whose name differs only in case is accepted too (see `ResolveFile`). |
| `ResolveFile(path)` | Returns the existing file whose path differs only in case from the given one, or the path unchanged. The games read their assets on case-insensitive file systems, so names inside meshes and cities do not always match the case of the files. Used by the mesh, texture, material and OBJ/MTL lookups; it does nothing on Windows. Directory listings are cached and refreshed when a directory changes (`ClearCaseCache` clears them). |
| `GetFileList(...)` | Lists all files with the given extensions in the city folder and the extra folders (the first occurrence of a relative path wins), filtered by a case-insensitive regex. Used by the texture and mesh selectors. |
| `GetExtension`, `DirectoryCopy` | Helpers. |

Because asset paths are stored relative to these folders, a city keeps working when moved to another computer, as long as the same assets are available in one of the searched folders.

## `StringManager`

Translated UI strings. Two tables are loaded for the current language: the base one (`Files/strings/<lang>.json`) and the core's one (`Files/cores/<core>/strings/<lang>.json`). `Get(key)` looks the key up in the core table first, then in the base table; a missing key is logged and the key itself is returned, so missing translations are visible in the UI. An empty string value returns `null` (used to mean "no tooltip"). `Get(key, folder)` reads a key from another core's table (used to show core names and template names). `Reload` is called when a city is loaded, because the core table depends on the city's core.

Every `label`, `placeholder`, `tooltip` and `enumLabels` entry in the core definitions is a key looked up through `StringManager`. See [Parameters and UI](Parameters-and-UI#strings).

## `PythonManager`

Runs the Python interpreter for three purposes: one-shot scripts (migrations, exporters), a long-lived local server that reads game-specific mesh and texture formats, and installing Python and its modules.

### Interpreter selection and installation

* Only the interpreter configured in `pythonPath`, or the embedded one in `<base>/python` when that setting is empty, is used. The system-wide Python is never used implicitly.
* `CheckPython` verifies that the interpreter exists and that the base modules listed in `pythonInstaller/imports.txt` can be imported. If not, it offers to install them: on Windows by running `pythonInstaller/run_installer.bat` (which downloads the embedded Python and runs `pip` on `pythonInstaller/requirements.txt`); on Linux by downloading a portable CPython 3.10 build (python-build-standalone, the same minor version as on Windows) into `<base>/python` (it is extracted with the system `tar`) and installing the requirements in it. Either way the same package versions are used on every platform.
* `CheckPythonForCore` additionally checks the modules in the core's `pythonInfo/imports.txt` and offers to install the core's `pythonInfo/requirements.txt` with `pip`. It runs before a city is loaded.
* `IsPythonReady` and `IsReadyForCore` are the same checks without the prompts, and `InstallCoreRequirementsAsync` installs the core's requirements without a console window (used when a script loads a city).

### Scripts

`RunScript(script, absolute, args, useShellExecute)` and `RunScriptAsync(...)` run a script (a name relative to `Files/python`, or an absolute path) and return its exit code and output. With `useShellExecute` the script gets its own visible console window (used by exporters, which may wait for a key press); otherwise its output is captured. All output goes to `<base>/pythonLog.txt`. On failure the error message is the last line of standard error without the exception type: for `Exception: MIGRATION_ERROR_MISSING_MIGRATIONS` it is `MIGRATION_ERROR_MISSING_MIGRATIONS`, so scripts can report errors as string keys.

`RunScriptTask(...)` is the awaitable version of `RunScriptAsync`. Without a console window it sends a few empty lines to the standard input of the script and closes it, so that the scripts waiting for a key press at the end (as the exporters do in their console) end instead of failing.

### Mesh and texture server

`StartServer` runs `Files/python/server.py <port> <program pid> <core>/server` if the current core declares custom mesh or texture formats. The server imports the core's `mesh_formats` and `texture_formats` modules and shuts itself down when the program exits. The server is restarted whenever a city is loaded, so that it always serves the modules of the current core.

`SendRequest(message)` sends `mesh|<path>` or `texture|<path>` over TCP and returns the response payload (the 4-byte length prefix is removed). It connects on demand, waits up to one second for the server, and retries once on error. The payload formats are described in [Core Python Integration](Core-Python-Integration#custom-mesh-and-texture-formats).
