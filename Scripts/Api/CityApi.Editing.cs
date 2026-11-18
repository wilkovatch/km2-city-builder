using System.Collections.Generic;
using Godot;

namespace Api {
    //Editing several elements together, and reshaping terrain patches and building lines (see Operations.GroupTransform,
    //Operations.Properties, Operations.TerrainPatches and Operations.BuildingLines)
    public partial class CityApi {
        List<object> ResolveGroup(List<ElementRef> elements) {
            if (elements == null || elements.Count == 0) throw ApiException.InvalidParam("missing elements");
            var res = new List<object>();
            foreach (var r in elements) {
                var element = ResolveElement(r);
                if (!Operations.GroupTransform.Accepts(element)) throw ApiException.InvalidOperation("element " + r + " (" + KindOf(element) + ") can't be transformed");
                if (!res.Contains(element)) res.Add(element);
            }
            return res;
        }

        [ApiMethod("elements.transform", "Moves, rotates and scales several elements of any kind together, as the multiple selection of the editor: every position is scaled (scale: a vector, on the world axes), then rotated (rotation: degrees around the vertical axis, positive to the right) around the pivot (by default the center of what moves), then moved by the offset. What follows something else stays with it: the points on the anchors of roads and intersections, on a segment or on another point, and the end of a road on an intersection that is not among the elements; an intersection moves the ends of its roads. The mesh instances and the points of the custom elements also get the rotation, and the mesh instances a uniform scale. Returns {moved: the number of points and meshes moved, pivot}")]
        public object TransformElements(List<ElementRef> elements, Vector3? offset = null, float rotation = 0, Vector3? scale = null, Vector3? pivot = null) {
            var factor = scale ?? Vector3.One;
            if (factor.X == 0 || factor.Y == 0 || factor.Z == 0) throw ApiException.InvalidValue("the scale can't be zero");
            return ApplyChange("transform", () => {
                var group = ResolveGroup(elements);
                var session = Operations.GroupTransform.Begin(M, group, pivot);
                Operations.GroupTransform.Apply(session, offset ?? Vector3.Zero, Operations.GroupTransform.Yaw(rotation), factor);
                M.worldChanged = true;
                return new { moved = session.Count, pivot = session.pivot };
            });
        }

        [ApiMethod("elements.copyProperties", "Gives the elements (to) the properties of another element of the same kind (from), as the property eyedropper of the editor: what a preset of the element would hold, without saving one. Roads, intersections, terrain patches, building lines, buildings ({line, building}), polygonal buildings, custom elements and mesh instances (the last two only between elements of the same type). The points, the instance specific values (e.g. the values of the spline of a road), the references and the container of each element are kept. Returns the number of elements changed")]
        public int CopyProperties(ElementRef from, List<ElementRef> to) {
            if (to == null || to.Count == 0) throw ApiException.InvalidParam("missing elements");
            return ApplyChange("copy properties", () => {
                var source = Resolve(from);
                if (!Operations.Properties.Supports(source)) throw ApiException.InvalidOperation("the properties of a " + KindOf(source) + " can't be copied");
                var count = 0;
                foreach (var r in to) {
                    var target = Resolve(r);
                    switch (Operations.Properties.Copy(source, target)) {
                        case Operations.Properties.CopyResult.Copied: count++; break;
                        case Operations.Properties.CopyResult.Same: break;
                        case Operations.Properties.CopyResult.DifferentType:
                            throw ApiException.InvalidOperation("element " + r + " is of another type than the source");
                        default:
                            throw ApiException.WrongKind(KindOf(source), KindOf(target));
                    }
                }
                return count;
            });
        }

        [ApiMethod("patches.insertPerimeterPoint", "Inserts a free point in the perimeter of a terrain patch, in the segment closest to the position (or before the perimeter point at the index), as a click on a segment of the patch being edited. Returns the terrain point id")]
        public int InsertPatchPerimeterPoint(ElementRef patch, Vector3 position, int? index = null, bool onGround = false) {
            return ApplyChange("insert perimeter point", () => {
                var p = EditPatch(patch);
                var point = Operations.TerrainPatches.InsertPerimeterPoint(p, onGround ? GeometryHelper.ProjectPoint(position) : position, index);
                if (point == null) throw ApiException.InvalidOperation(index.HasValue ? "the index is out of range" : "the terrain patch has less than two perimeter points");
                return History.ElementRegistry.GetId(point);
            });
        }

        [ApiMethod("patches.split", "Splits a terrain patch along the line between two points of its perimeter (terrain point ids, not next to each other): the patch keeps the perimeter from the second point to the first one, a new patch with the same properties gets the rest. Both patches share the two points, and each internal point goes to the patch it is in. Returns the id of the new patch")]
        public int SplitPatch(ElementRef patch, int from, int to) {
            return ApplyChange("split terrain patch", () => {
                var p = EditPatch(patch);
                var a = ResolveTerrainPoint(from);
                var b = ResolveTerrainPoint(to);
                var error = Operations.TerrainPatches.GetSplitError(p, a, b);
                if (error != null) throw ApiException.InvalidOperation(error);
                return IdOf(Operations.TerrainPatches.Split(M, p, a, b));
            });
        }

        [ApiMethod("patches.merge", "Merges a terrain patch (other) into another one (patch), which keeps its properties: the two patches must share one continuous side (two or more perimeter points that are the same points, on the same anchors or at the same positions), which is removed. The internal points of both are kept, the other patch is deleted")]
        public void MergePatches(ElementRef patch, ElementRef other) {
            ApplyChange("merge terrain patches", () => {
                var p = EditPatch(patch);
                var o = EditPatch(other);
                var error = Operations.TerrainPatches.GetMergeError(p, o);
                if (error != null) throw ApiException.InvalidOperation(error);
                Operations.TerrainPatches.Merge(M, p, o);
            });
        }

        [ApiMethod("lines.insertPoint", "Inserts a free point in a building line, in the segment closest to the position (or before the point at the index), as a click on a segment of the line being edited (dividing: it splits two buildings). Returns the terrain point id")]
        public int InsertLinePoint(ElementRef line, Vector3 position, int? index = null, bool dividing = false, bool onGround = false) {
            return ApplyChange("insert building line point", () => {
                var l = EditLine(line);
                var point = Operations.BuildingLines.InsertPoint(l, onGround ? GeometryHelper.ProjectPoint(position) : position, dividing, index);
                if (point == null) throw ApiException.InvalidOperation(index.HasValue ? "the index is out of range" : "the building line has less than two points");
                return History.ElementRegistry.GetId(point);
            });
        }

        [ApiMethod("lines.split", "Splits a building line at one of its points (a terrain point id, not an end; not for looping lines): the line keeps the points up to it, a new line with the same properties gets the ones from it. Both lines share the point, and the buildings keep their properties. Returns the id of the new line")]
        public int SplitLine(ElementRef line, int point) {
            return ApplyChange("split building line", () => {
                var l = EditLine(line);
                var p = ResolveTerrainPoint(point);
                var error = Operations.BuildingLines.GetSplitError(l, p);
                if (error != null) throw ApiException.InvalidOperation(error);
                //the buildings must match the points (a line just created gets them at the next update)
                M.worldChanged = true;
                M.ProcessUpdate();
                return IdOf(Operations.BuildingLines.Split(M, l, p));
            });
        }

        [ApiMethod("lines.merge", "Joins a building line (other) to another one of the same type (line), which keeps its properties: the ends of the two lines closest to each other are joined, into one point if they are at the same position, otherwise with a new building between them. The buildings keep their properties, the other line is deleted (not for looping lines)")]
        public void MergeLines(ElementRef line, ElementRef other) {
            ApplyChange("join building lines", () => {
                var l = EditLine(line);
                var o = EditLine(other);
                var error = Operations.BuildingLines.GetMergeError(l, o);
                if (error != null) throw ApiException.InvalidOperation(error);
                M.worldChanged = true;
                M.ProcessUpdate();
                Operations.BuildingLines.Merge(M, l, o);
            });
        }
    }
}
