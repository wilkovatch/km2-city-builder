"""Creates building lines around the blocks between the roads, like Create > Whole city buildings, using only the
commands of the API.

The blocks are taken from the anchor with the longest segment first: a line starts in the middle of that segment and
is closed following the anchors around the block, split into buildings with the building presets."""

import sys

import citybuilder as cb
from citybuilder import api, ApiError


def key(anchor):
    return None if anchor is None else "%s:%s" % (anchor["of"], anchor["index"])


def anchor_ref(anchor):
    return {"of": anchor["of"], "index": anchor["index"]}


def textures_of_patches_on(anchors):
    """For each anchor, the textures of the terrain patches with a perimeter point on it."""
    patch_ids = set(e["id"] for e in api.elements.list(kind="terrainPatch"))
    textures = {}
    res = {}
    for a in anchors:
        found = []
        for user in a.get("usedBy") or []:
            element = user["element"]
            if user.get("segment") or element not in patch_ids:
                continue
            if element not in textures:
                textures[element] = api.state.getParam(ref=element, name="texture") or ""
            found.append(textures[element])
        res[key(a)] = found
    return res


def generate(line_preset, building_presets, min_length, max_length, subdivide, null_only):
    anchors = api.anchors.list(unusedBy="buildingLine")
    all_anchors = api.anchors.list()
    anchor_set = set(key(a) for a in anchors)
    done_set = set(key(a) for a in all_anchors if key(a) not in anchor_set)
    textures = textures_of_patches_on(anchors) if null_only else None
    #the length of the segment from each anchor to the next one (they don't change while the lines are added)
    lengths = {}
    for a in anchors:
        segment = api.anchors.segment(anchor=anchor_ref(a))
        if segment is not None:
            lengths[key(a)] = segment["length"]
    total = max(1, len(anchors))
    lines = []

    def remove(k):
        anchors[:] = [x for x in anchors if key(x) != k]
        anchor_set.discard(k)

    while anchors:
        cb.progress(1 - len(anchors) / total)
        #the anchor with the longest segment
        anchor = None
        max_dist = 0.0
        for a in anchors:
            d = lengths.get(key(a))
            if d is not None and d > max_dist:
                max_dist = d
                anchor = a
        if anchor is None:
            break
        loop = api.anchors.loop(anchor=anchor_ref(anchor))
        if len(loop) > 1:
            if textures is not None and "" not in textures.get(key(anchor), []):
                #only the blocks of invisible terrain patches
                for point in loop:
                    remove(key(point))
            else:
                try:
                    res = api.lines.create(points=[{"anchorLink": anchor_ref(anchor)}], preset=line_preset, followAnchors=True,
                                           autoClose={"presets": building_presets, "minLength": min_length,
                                                      "maxLength": max_length, "subdivide": subdivide},
                                           details=True)
                except ApiError:
                    res = None
                if res is not None:
                    to_delete = False
                    for point in (key(a) for a in res["anchors"]):
                        if point in anchor_set:
                            remove(point)
                            done_set.add(point)
                        elif point in done_set:
                            to_delete = True
                            break
                    if not to_delete:
                        lines.append(res["id"])
                    else:
                        api.lines.delete(line=res["id"])
        remove(key(anchor))
    return lines


def main():
    values = cb.inputs()
    presets = [e["preset"] for e in values.get("buildingPresets", []) if e.get("preset")]
    if not presets:
        sys.exit("SCRIPT_CITY_BUILDINGS_NO_PRESETS")
    lines = generate(values["linePreset"], presets, values.get("minLength", 15.0), values.get("maxLength", 15.0),
                     values.get("subdivide", True), values.get("nullOnly", False))
    cb.progress(1.0)
    cb.log("%d building lines created" % len(lines))


main()
