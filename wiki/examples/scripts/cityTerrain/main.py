"""Creates terrain patches between the roads, like Create > Whole city terrain, using only the commands of the API.

Each block closed by roads gets a patch following its anchors; the patch around the outside of the road network is
replaced by a ring of patches, `distance` wide and split every `segmentLength` meters."""

import citybuilder as cb
from citybuilder import api, ApiError
from citybuilder.geometry import add, sub, mul, distance as dist, normalize, is_point_inside_polygon, outer_polygon


def key(anchor):
    return None if anchor is None else "%s:%s" % (anchor["of"], anchor["index"])


def point_of(terrain_point, position):
    return {"terrainPoint": terrain_point} if terrain_point is not None else {"position": position}


def create_patch(targets):
    """A patch through the targets (each followed from the previous anchor), its id and the point given by each target."""
    try:
        res = api.patches.create(perimeter=targets, followAnchors=True, details=True)
    except ApiError as e:
        cb.log("patch not created: " + str(e), "warning")
        return -1, [None] * len(targets)
    return res["id"], res["points"]


def points_between(p0, p1, step):
    """The points every step meters from p0 towards p1, stopping at 2 steps from p1."""
    res = []
    direction = normalize(sub(p1, p0))
    p = p0
    while dist(p, p1) > 2 * step:
        p = add(p, mul(direction, step))
        res.append(p)
    return res


def generate(distance, segment_length, fusion_distance, smooth, internal_distance, texture):
    all_anchors = api.anchors.list()
    anchor_refs = {key(a): {"of": a["of"], "index": a["index"]} for a in all_anchors}
    positions = {key(a): a["position"] for a in all_anchors}
    anchors = [key(a) for a in api.anchors.list(unusedBy="terrainPatch")]
    anchor_set = set(anchors)
    done_anchors = [key(a) for a in all_anchors if key(a) not in anchor_set]
    done_set = set(done_anchors)
    total = max(1, len(anchors))
    tmp_patches = []
    patches = []

    #a patch around each block, closed following the anchors
    while anchors:
        cb.progress(0.8 * (1 - len(anchors) / total))
        anchor = anchors[0]
        try:
            res = api.patches.create(perimeter=[{"anchor": anchor_refs[anchor]}], followAnchors=True, autoClose=True, details=True)
        except ApiError as e:
            if e.code not in ("no_loop_found", "autoclose_failed"):
                cb.log("patch on the anchor " + anchor + " not created: " + str(e), "warning")
            res = None
        if res is not None:
            to_delete = False
            for point in (key(a) for a in res["anchors"]):
                if point in anchor_set:
                    anchors.remove(point)
                    anchor_set.discard(point)
                    done_anchors.append(point)
                    done_set.add(point)
                elif point in done_set:
                    to_delete = True
                    break
            if not to_delete:
                patches.append(res["id"])
                tmp_patches.append(res["id"])
            else:
                api.patches.delete(patch=res["id"])
        if anchor in anchor_set:
            anchors.remove(anchor)
            anchor_set.discard(anchor)

    #the patches with anchors inside (around the outside of the roads) are replaced by a ring of patches
    deleted = set()
    for patch_id in tmp_patches:
        perimeter = api.elements.get(ref=patch_id, derived=True)["derived"]["perimeter"]
        points = [p["position"] for p in perimeter]
        comps = [p["id"] for p in perimeter]
        comp_anchors = [key(p.get("anchor")) for p in perimeter]
        min_x = min(p[0] for p in points)
        max_x = max(p[0] for p in points)
        min_z = min(p[2] for p in points)
        max_z = max(p[2] for p in points)
        for done in done_anchors:
            pos = positions[done]
            if pos[0] < min_x or pos[0] > max_x or pos[2] < min_z or pos[2] > max_z:
                continue
            if done in comp_anchors or not is_point_inside_polygon(pos, points):
                continue

            #outer vertices
            orig_count = len(points)
            outer_points = outer_polygon(points, distance)
            for p in reversed(outer_points):
                comps.append(api.patches.addPerimeterPoint(patch=patch_id, point={"position": p}, followAnchors=True))
                points.append(p)

            #last patch
            targets = [point_of(comps[0], points[0]), point_of(comps[orig_count - 1], points[orig_count - 1])]
            initial_mid_indices = []
            if internal_distance > 0:
                for p in points_between(points[orig_count - 1], points[orig_count], internal_distance):
                    initial_mid_indices.append(len(targets))
                    targets.append({"position": p})
            op2_index = len(targets)
            targets.append(point_of(comps[orig_count], points[orig_count]))
            ip2_index = len(targets)
            targets.append(point_of(comps[-1], points[-1]))
            final_mid_indices = []
            if internal_distance > 0:
                for p in points_between(points[-1], points[0], internal_distance):
                    final_mid_indices.append(len(targets))
                    targets.append({"position": p})
            patch2_id, patch2_points = create_patch(targets)
            if patch2_id >= 0:
                patches.append(patch2_id)
            initial_mid_points = [patch2_points[i] for i in initial_mid_indices]
            op2 = patch2_points[op2_index]

            #split in multiple patches and remove excess vertices
            cur_pos = 0.0
            start_j = 0
            last_outer_point = patch2_points[ip2_index]
            last_mid_points = [patch2_points[i] for i in final_mid_indices]
            last_mid_points.reverse()
            last_inserted = [float("inf")] * 3
            for j in range(1, orig_count):
                cur_pos += dist(points[j], points[j - 1])
                if cur_pos > segment_length or j == orig_count - 1:
                    sub_outer = outer_points[start_j:j + 1]
                    sub_outer.reverse()
                    targets3 = []
                    for k in range(start_j, j + 1):
                        if comp_anchors[k] is not None:
                            targets3.append({"anchor": anchor_refs[comp_anchors[k]]})
                        else:
                            targets3.append({"position": points[k]})
                    new_last_mid_indices = []
                    if internal_distance > 0:
                        if j == orig_count - 1:
                            for mp in initial_mid_points:
                                targets3.append({"terrainPoint": mp})
                        else:
                            for p in points_between(points[j], sub_outer[0], internal_distance):
                                new_last_mid_indices.append(len(targets3))
                                targets3.append({"position": p})
                    first_outer_index = -1
                    for k in range(0, j - start_j):
                        existing = op2 if (j == orig_count - 1 and k == 0) else None
                        if k == 0 or (dist(last_inserted, sub_outer[k]) > fusion_distance
                                      and dist(sub_outer[j - start_j], sub_outer[k]) > fusion_distance):
                            if first_outer_index < 0:
                                first_outer_index = len(targets3)
                            targets3.append(point_of(existing, sub_outer[k]))
                            last_inserted = sub_outer[k]
                    targets3.append(point_of(last_outer_point, sub_outer[-1]))
                    if internal_distance > 0:
                        last_mid_points.reverse()
                        for mp in last_mid_points:
                            targets3.append({"terrainPoint": mp})
                    patch3_id, patch3_points = create_patch(targets3)
                    if patch3_id >= 0:
                        patches.append(patch3_id)
                    last_outer_point = patch3_points[first_outer_index] if first_outer_index >= 0 else None
                    if internal_distance > 0:
                        last_mid_points = [patch3_points[i] for i in new_last_mid_indices]
                    cur_pos = 0.0
                    start_j = j
            api.patches.delete(patch=patch_id)
            deleted.add(patch_id)
            break

    cb.progress(0.9)
    created = []
    for patch_id in patches:
        if patch_id in deleted:
            continue
        if internal_distance > 0:
            try:
                api.patches.generateInternalPoints(patch=patch_id, distance=internal_distance)
            except ApiError:
                pass
        api.state.setBuiltin(ref=patch_id, name="smooth", value=smooth)
        if texture:
            api.state.setBuiltin(ref=patch_id, name="texture", value=texture)
        created.append(patch_id)
    return created


def main():
    values = cb.inputs()
    created = generate(values.get("distance", 5.0), values.get("segmentLength", 100.0), values.get("fusionDistance", 0.0),
                       values.get("smoothing", 0), values.get("internalDistance", 0.0), values.get("texture", ""))
    cb.progress(1.0)
    cb.log("%d terrain patches created" % len(created))


main()
