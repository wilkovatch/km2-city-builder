# Validation

A validation checks a city with the exporter of its core, before exporting it for real: it is an export to a temporary folder (deleted afterwards), with flags chosen by the core, that only collects the warnings and errors the exporter reports. The checks are the ones of the exporter itself, so the program has no rules of its own to keep in sync with it.

## Using it

- In the editor: *File → Validate city*. The *Validation* panel lists the errors (`[E]`, red) and warnings (`[W]`); selecting one highlights its element (when the message names one) and shows the whole message below the list. *Zoom to it* moves the camera to its position (or to its element), *Edit element* opens its editor, *Run again* validates again after a fix. The lines an item draws over the city (its `overlay`, e.g. the route of an opponent) are shown while the panel is open, the ones of the selected item highlighted. The same panel opens after an export that reported something (*Export report*), and for the results of a [check](Core-Checks) of the core (e.g. the opponents of a race).
- From a script or an agent: `city.validate` (see [Scripting API](Scripting-API#city)), optionally with `exporter` to use only the exporter of that format. The result:

```json
{
  "ok": false, "errors": 1, "warnings": 0, "exporters": ["psdl"], "durationMs": 2760,
  "items": [
    {
      "level": "error",
      "message": "Block 14 not exported: Exception: Perimeter error: infinite loop on block 14 (object Intersection 1)",
      "element": {"name": "Intersection 1"},
      "details": "Traceback (most recent call last): ...",
      "exporter": "psdl"
    }
  ]
}
```

`element` is a reference of the scripting API (`{"name", "kind"}`, `kind` when the exporter knows it): it can be passed as it is to `elements.get`, `view.render` (`highlight`, `elements`) or `view.focus`.

- From the [Command Line](Command-Line): `validate <city>` prints the result and exits with code 1 if there are errors.

## For core authors

An exporter validates cities when its `settings.json` has a `validation` section, with the flags of the dry export (the others keep their default):

```json
"validation": {
    "flags": { "psdl": true, "inst": true, "bai": true, "pathset": true, "hudmap": false, "packAr": false }
}
```

Leave out what is slow and can't fail (the MM2 core skips the hudmap images). The exporter receives the same arguments as for an export, with an output path in a temporary folder; `ExportReport.is_validation()` tells it apart, should it want to check more or write less.

The exporter reports the problems with `Files/python/ExportReport.py` (see [Core Python Integration](Core-Python-Integration#warnings-and-errors)). An error stops a validation only if the exporter stops: to report every problem at once, go on after an error, leaving out the element that failed, and fail at the end (`sys.exit(1)` when `ExportReport.has_errors()`).

### The Midtown Madness 2 core

The MM2 exporter reports every element and file on its own, as described in [docs/exporter.md](https://github.com/wilkovatch/km2cb-mm2-core/blob/main/docs/exporter.md#errors) of its repository.

## Internals

| Class | Purpose |
|---|---|
| `CityValidator` (`IO/Export/CityValidator.cs`) | Finds the exporters with a `validation` section (`GetValidators`) and runs each one as a dry export (`ValidateAsync`), deleting its temporary folder. |
| `CityExporter.ExportWithExporterAsync` | Passes the report file in the export JSON (`report`, `IO.ExportedCity.ExportReportInfo`), reads it back into `ExportResult.items`, and fails the export if it has errors. |
| `EditorPanels.ValidationEditorPanel` | The panel of *File → Validate city*, of the export report and of the [checks](Core-Checks), with the overlays of the items. |
