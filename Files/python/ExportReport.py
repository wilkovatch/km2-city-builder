"""The warnings and errors of an exporter of a core, read back by the city builder.

The program shows them after an export, and a validation (city.validate, Validate city in the File menu) is an export to
a temporary folder that only collects them. The export JSON says where to write them:
"report": {"file": <path>, "validation": <bool>}. Without it (an exporter run by hand) the messages are only printed,
as "Warning: ..." and "Error: ...".

    import ExportReport as report

    report.init(data)                      # data: the decoded export JSON
    report.warning("too many lanes, the outer ones are not exported", element="Road 3", kind="road")
    try:
        write_block(...)
    except Exception as e:
        report.exception("could not write the block", element="Intersection 1", kind="intersection")
    ...
    sys.exit(1 if report.has_errors() else 0)

element and kind identify the element as the scripting API does ({"name": ..., "kind": ...}: road, intersection,
terrainPatch, buildingLine, meshInstance); position is [x, y, z] in the program coordinates.
"""

import io
import json
import traceback

LEVELS = ("error", "warning", "info")

_file = None
_validation = False
_counts = {level: 0 for level in LEVELS}


def init(data):
    """Reads where to write the report from the export JSON (a dictionary), and empties the file."""
    global _file, _validation
    info = data.get("report") if isinstance(data, dict) else None
    if not info:
        return
    _file = info.get("file")
    _validation = bool(info.get("validation", False))
    if _file:
        with io.open(_file, "w", encoding="utf-8"):
            pass


def is_validation():
    """Whether the export only validates the city (its files go to a temporary folder, deleted afterwards)."""
    return _validation


def report(level, message, element=None, kind=None, code=None, position=None, details=None):
    if level not in LEVELS:
        raise ValueError("the level is one of " + ", ".join(LEVELS))
    message = str(message)
    if level == "error":
        print("Error: " + message)
    elif level == "warning":
        print("Warning: " + message)
    else:
        print(message)
    _counts[level] += 1
    if not _file:
        return
    entry = {"level": level, "message": message}
    if element is not None:
        entry["element"] = {"name": str(element)}
        if kind is not None:
            entry["element"]["kind"] = kind
    if code is not None:
        entry["code"] = code
    if position is not None:
        entry["position"] = [float(v) for v in position]
    if details is not None:
        entry["details"] = details
    with io.open(_file, "a", encoding="utf-8") as f:
        f.write(json.dumps(entry, ensure_ascii=False) + "\n")


def error(message, **kwargs):
    report("error", message, **kwargs)


def warning(message, **kwargs):
    report("warning", message, **kwargs)


def info(message, **kwargs):
    report("info", message, **kwargs)


def exception(message=None, **kwargs):
    """Reports the exception being handled as an error, with its traceback in the details."""
    text = traceback.format_exc()
    last = text.strip().splitlines()[-1] if text.strip() else "unknown error"
    report("error", (message + ": " + last) if message else last, details=text, **kwargs)


def has_errors():
    return _counts["error"] > 0


def counts():
    return dict(_counts)
