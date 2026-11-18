using System.Collections.Generic;

namespace Operations {
    //Copying the properties of an element to another one of the same kind (the property eyedropper of the editor, the
    //elements.copyProperties command of the API): what a preset of the element would hold, without saving one. What
    //belongs to the element itself is kept: its points, its instance specific values (e.g. the values of the spline of
    //a road), its references and its container.
    public static class Properties {
        public enum CopyResult {
            Copied,
            Same, //the source is the target
            DifferentKind, //e.g. a road and a terrain patch
            DifferentType, //custom elements and mesh instances only take the properties of their own type
            NotSupported
        }

        //the kinds whose properties can be copied
        public static bool Supports(object element) {
            switch (element) {
                case Road:
                case Intersection:
                case TerrainPatch:
                case BuildingLine:
                case Building:
                case CustomElement:
                    return true;
                case MeshInstance mesh:
                    return mesh.objectParameter == null;
                default:
                    return false;
            }
        }

        //what Copy would answer, without changing anything
        public static CopyResult Check(object source, object target) {
            if (!Supports(source) || !Supports(target)) return CopyResult.NotSupported;
            if (source == target) return CopyResult.Same;
            if (source.GetType() != target.GetType()) return CopyResult.DifferentKind;
            if (source is CustomElement a && target is CustomElement b && a.TypeName != b.TypeName) return CopyResult.DifferentType;
            if (source is MeshInstance m && target is MeshInstance n && m.TypeName != n.TypeName) return CopyResult.DifferentType;
            if (source is Building x && target is Building y && x.line.state.Str("type", null) != y.line.state.Str("type", null)) return CopyResult.DifferentType;
            return CopyResult.Copied;
        }

        public static CopyResult Copy(object source, object target) {
            var res = Check(source, target);
            if (res != CopyResult.Copied) return res;
            switch (target) {
                case Road road:
                    road.SetState((ObjectState)((Road)source).state.Clone());
                    break;
                case Intersection intersection:
                    History.UndoManager.Touch(intersection);
                    intersection.state = (ObjectState)((Intersection)source).state.Clone();
                    intersection.state.FlagAsChanged();
                    break;
                case TerrainPatch patch:
                    History.UndoManager.Touch(patch);
                    //where the points are (on the ground or not) is of the patch, not a property to copy
                    var project = patch.state.Bool("projectToGround");
                    patch.state = (ObjectState)((TerrainPatch)source).state.Clone();
                    patch.state.SetBool("projectToGround", project);
                    patch.state.FlagAsChanged();
                    break;
                case BuildingLine line:
                    History.UndoManager.Touch(line);
                    BuildingLines.SetLineState(line, ((BuildingLine)source).state);
                    line.state.FlagAsChanged();
                    break;
                case Building building:
                    History.UndoManager.Touch(building.line);
                    building.SetState(((Building)source).State);
                    break;
                case CustomElement custom:
                    CustomElementPresets.Apply(custom, CustomElementPresets.Extract((CustomElement)source));
                    break;
                case MeshInstance mesh:
                    History.UndoManager.Touch(mesh);
                    var copy = (ObjectState)((MeshInstance)source).settings.Clone();
                    PropertyContainers.Assign(copy, PropertyContainers.Scope(mesh.settings));
                    mesh.settings.ReplaceWith(copy);
                    mesh.settings.FlagAsChanged();
                    break;
            }
            return res;
        }

        //copies the properties of the source to every target it can give them to, returns how many got them
        public static int CopyToAll(object source, IEnumerable<object> targets) {
            var count = 0;
            foreach (var target in targets) {
                if (Copy(source, target) == CopyResult.Copied) count++;
            }
            return count;
        }
    }
}
