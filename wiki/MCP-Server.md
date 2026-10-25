# MCP Server

The program can be driven by AI agents (Claude Code, Claude Desktop, or any client of the [Model Context Protocol](https://modelcontextprotocol.io)): they create and edit cities through the [Scripting API](Scripting-API), look at them with offscreen renders and check them with the [validation](Validation) of the core.

There are two ways to connect:

- **To the editor (HTTP)**: the agent works on the city open in the editor, while the user watches and edits it too. Its changes are undo steps like the user's.
- **To a program of its own (stdio)**: the client starts the program with the `mcp` command and talks to it through its standard input and output.

## In the editor

In *Settings → Preferences → MCP*, check *Enable the MCP server*. The server listens on `http://127.0.0.1:<port>/mcp` (port 7810 by default), only for local connections, and every request must have the token of the panel (`Authorization: Bearer <token>`; *New token* makes another one, the clients then need the new one). The panel shows whether the server is running and the command adding it to Claude Code, to copy into a terminal:

```
claude mcp add --transport http km2-city-builder http://127.0.0.1:7810/mcp --header "Authorization: Bearer <token>"
```

The log of the program says when a client connects.

## Standalone

The client starts the program itself. Renders need a window, so don't use `--headless` (the window is kept small and outside of the screens); `--quiet` keeps the messages of the engine out of the standard output, where only the protocol goes:

```
claude mcp add km2-city-builder -- "<program folder>/km2-city-builder.console.exe" --quiet -- mcp
```

With the Godot editor binary: `Godot_v4.7.1-stable_mono_win64_console.exe --path <project folder> --quiet -- mcp`. `--city <folder>` loads a city first; otherwise the agent creates or loads one (`city.new`, `city.load`).

## Tools

| Tool | Description |
|---|---|
| `api_call` | Runs any method of the scripting API: `{"method": "roads.create", "params": {...}}` |
| `api_methods` | The methods with their parameters (optionally only the ones starting with a prefix) |
| `api_batch` | Several methods as a single undo step (atomic by default) |
| `script_run` | Runs a Python script (`code`, or `path` of a `.py` file or script folder) with the `citybuilder` module, as the Scripts menu does (see [Python Scripts](Python-Scripts)): its changes are one undo step, undone if it fails. Returns what it printed. For changes too many for single calls |
| `city_info`, `city_validate`, `elements_list`, `elements_get`, `view_focus` | The API methods of the same name |
| `view_render` | `view.render` (see [Images](Scripting-API#images)): the image is returned as an image, with the mapping of its pixels and the positions of the labels as text |
| `view_screenshot` | The window of the editor as the user sees it |

An API error is a tool error with its code and message (`error [not_found]: ...`), so the agent can fix the call. The pages of this wiki are resources (`km2cb://wiki/<page>`), and the server gives the agents instructions on how to start.

## Internals

The classes are in `Scripts/Mcp`:

| Class | Purpose |
|---|---|
| `McpServer` | The protocol (JSON-RPC 2.0): `initialize`, `tools/list`, `tools/call`, `resources/list`, `resources/read`, `ping`. The tools of the API methods are made from their metadata (`ApiMethod`, the types of the parameters). Calls the API through `CommandDispatcher.Enqueue`, so it can be used from any thread; `script_run` runs on the main thread with `ApiHost.RunOnMainThread` (the dispatcher runs the commands of the script meanwhile). Progress notifications are sent on stdio when the client asks for them. |
| `McpHttp` | The HTTP transport of the editor: a minimal HTTP/1.1 server on a `TcpListener` bound to 127.0.0.1 (`HttpListener` would need a URL reservation on Windows), `POST /mcp` with JSON responses only (no event streams, no sessions). Checks the token and refuses requests from other origins (`Origin` header: DNS rebinding). Started by `ApplySettings` at startup and when the settings change. |

The stdio transport is the `mcp` command of `Cli.CliCommands`: one message at a time, in order.

The settings are `mcpServerEnabled`, `mcpServerPort` and `mcpServerToken`, in the settings of the program.
