"""Shows how many elements of each kind the city has, and which elements were selected when the script was started.

A script without inputs: a single .py file in a scripts folder, run directly from the Scripts menu."""

import citybuilder as cb
from citybuilder import api

KINDS = [("road", "roads"), ("intersection", "intersections"), ("terrainPatch", "terrain patches"),
         ("buildingLine", "building lines"), ("meshInstance", "mesh instances")]

info = api.city.info()
lines = [info["folder"], ""]
for kind, label in KINDS:
    lines.append("%d %s" % (len(api.elements.list(kind=kind)), label))
selected = cb.selection()
if selected:
    names = []
    for element in api.elements.list():
        if element["id"] in selected:
            names.append("%s (%s)" % (element["name"] or element["id"], element["kind"]))
    lines += ["", "Selected: " + ", ".join(names)]
print("\n".join(lines))
cb.alert("\n".join(lines), "City summary")
