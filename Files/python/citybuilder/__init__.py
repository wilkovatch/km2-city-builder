"""Client of the scripting API of the city builder, for the Python scripts run from its Scripts menu.

The program runs the script with its standard input and output connected to the API: each request is a line of JSON
written to the standard output, each response (and progress event) a line read from the standard input. On import the
standard output is replaced by the standard error, so print() never mixes with the requests: what is printed goes to
the log of the program.

    import citybuilder as cb
    from citybuilder import api

    roads = api.elements.list(kind="road")           # same as cb.call("elements.list", kind="road")
    values = cb.inputs()                              # the values of the input panel
    cb.progress(0.5, "Halfway")

The methods and their parameters are listed on the Scripting API page of the wiki (or with api.api.methods()).
"""

import contextlib
import json
import os
import sys

__all__ = ["ApiError", "api", "call", "batch", "transaction", "inputs", "selection", "progress", "log", "alert"]

if os.environ.get("KM2CB_SCRIPT") != "1":
    raise RuntimeError("this script must be run by the city builder (from its Scripts menu, or with --api-script)")

_requests = sys.stdout
_responses = sys.stdin
sys.stdout = sys.stderr

_next_id = 0

#called with (value, text) for the progress events of the long commands (loading, generation, export...)
on_progress = None


class ApiError(Exception):
    """An error returned by a command: code is one of the error codes of the Scripting API page of the wiki."""

    def __init__(self, code, message, details=None):
        super().__init__(message)
        self.code = code
        self.message = message
        self.details = details

    def __str__(self):
        return self.message


def call(method, params=None, **kwargs):
    """Runs a command and returns its result, raises ApiError if it fails.

    The parameters can be given as keywords or as a dictionary (for names that are Python keywords)."""
    global _next_id
    merged = dict(params or {})
    merged.update(kwargs)
    _next_id += 1
    request_id = _next_id
    _requests.write(json.dumps({"id": request_id, "method": method, "params": merged}, separators=(",", ":")) + "\n")
    _requests.flush()
    while True:
        line = _responses.readline()
        if not line:
            raise ConnectionError("the city builder has closed the connection")
        line = line.strip()
        if not line:
            continue
        message = json.loads(line)
        if "event" in message:
            if message.get("event") == "progress" and on_progress is not None:
                on_progress(message.get("value"), message.get("text"))
            continue
        if message.get("id") != request_id:
            continue
        if "error" in message:
            error = message["error"] or {}
            raise ApiError(error.get("code"), error.get("message"), error.get("details"))
        return message.get("result")


def batch(commands, atomic=True):
    """Runs several commands as a single undo step, returns the list of their results.

    commands: (method, params) pairs or {"method": ..., "params": ...} dictionaries. With atomic, if one fails the
    ones before it are undone (the ApiError has details["index"], the index of the failed command)."""
    items = []
    for command in commands:
        if isinstance(command, dict):
            items.append({"method": command["method"], "params": command.get("params") or {}})
        else:
            method, params = command
            items.append({"method": method, "params": params or {}})
    return call("batch", commands=items, atomic=atomic)


@contextlib.contextmanager
def transaction(label="script"):
    """Makes the commands of the block a single undo step, undone if the block raises an exception.

    Only for the scripts with "transaction": false in their script.json (otherwise the whole run is already one)."""
    call("history.begin", label=label)
    try:
        yield
    except BaseException:
        call("history.rollback")
        raise
    call("history.commit")


def inputs():
    """The values of the input panel of the script, by parameter name (enums as indices, arrays as lists of dicts)."""
    return call("script.inputs")


def selection():
    """The ids of the elements selected or being edited when the script was started."""
    return call("script.selection")


def progress(value, text=None):
    """Shows the progress of the script, value from 0 to 1 (text: None keeps the last one)."""
    call("script.progress", value=value, text=text)


def log(text, level="info"):
    """Writes a line in the log of the program, level info, warning or error."""
    call("script.log", text=str(text), level=level)


def alert(text, title=None):
    """Shows a message when the script ends."""
    call("script.alert", text=str(text), title=title)


class _Namespace:
    """api.roads.create(points=...) calls the method "roads.create"."""

    def __init__(self, name):
        self._name = name

    def __getattr__(self, attr):
        if attr.startswith("__"):
            raise AttributeError(attr)
        return _Namespace(attr if not self._name else self._name + "." + attr)

    def __call__(self, params=None, **kwargs):
        return call(self._name, params, **kwargs)

    def __repr__(self):
        return "<API method " + self._name + ">" if self._name else "<API>"


api = _Namespace("")
