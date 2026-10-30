using System.Collections.Generic;
using Godot;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;
using TP = CityElements.Types.Parsers.TypeParser;
using MeshInstanceType = CityElements.Types.Runtime.MeshInstanceType;

namespace Operations {
    //Mesh instances placed freely in the city, and the parametric objects (objectInstance parameters) of the elements
    public static class Meshes {
        //the mesh instance type (null if unknown)
        public static MeshInstanceType GetMeshType(string type) {
            if (type == null) return null;
            return TP.GetMeshInstanceTypes().TryGetValue(type, out var t) ? t : null;
        }

        static MeshInstanceType RequireType(string type) {
            return GetMeshType(type) ?? throw new System.ArgumentException("unknown mesh instance type '" + type + "'");
        }

        //the settings of a new instance: the given ones (if any) with the type
        public static ObjectState NewSettings(string type, ObjectState settings = null) {
            var res = settings != null ? (ObjectState)settings.Clone() : new ObjectState();
            res.SetStr("type", type);
            return res;
        }

        static MeshInstanceType FillType(ObjectState settings) {
            var t = GetMeshType(settings?.Str("type", null));
            t?.FillInitialVariables(t.variableContainer, settings);
            return t;
        }

        //the layer the mesh instances with the given settings (with their type) are put on (-1 if unknown)
        public static int GetLayer(ObjectState settings) {
            var t = FillType(settings);
            if (t == null) return -1;
            return (int)t.rules.layer.GetValue(t.variableContainer);
        }

        //the layers the mesh instances with the given settings (with their type) are placed on (-1 if unknown)
        public static int GetPlacerLayerMask(ObjectState settings) {
            var t = FillType(settings);
            if (t == null) return -1;
            return (int)t.rules.placerLayerMask.GetValue(t.variableContainer);
        }

        //settings: with the type ("type"); the random rotation is applied only if the type allows it
        public static MeshInstance Create(ElementManager manager, string meshPath, Vector3 position, ObjectState settings, bool randomRotation = false) {
            var mesh = MeshInstance.Create(meshPath, position, manager.GetMeshContainer(), settings);
            if (randomRotation && (mesh.GetMeshInstanceType()?.Settings.randomRotation ?? false)) {
                GO.SetRotation(mesh, new Vector3(0, 360 * (float)RandomManager.rnd.NextDouble(), 0));
            }
            manager.AddMesh(mesh);
            return mesh;
        }

        static MeshInstanceType RequireMassPlacement(ObjectState settings) {
            var t = RequireType(settings?.Str("type", null));
            if (!t.Settings.massPlacement) throw new System.ArgumentException("mesh instance type '" + t.name + "' does not allow mass placement");
            return t;
        }

        //places count instances at random points within the radius, projected on the layers they can be placed on
        //(only for the types with massPlacement)
        public static List<MeshInstance> Scatter(ElementManager manager, string meshPath, Vector3 center, float radius, int count, ObjectState settings, bool randomRotation) {
            RequireMassPlacement(settings);
            var points = new List<Vector3>();
            var rnd = RandomManager.rnd;
            var mask = GetPlacerLayerMask(settings);
            for (int i = 0; i < count; i++) {
                var randomDir = new Vector3((float)rnd.NextDouble() - 0.5f, 0, (float)rnd.NextDouble() - 0.5f).Normalized();
                var point = center + randomDir * radius * (float)rnd.NextDouble();
                points.Add(GeometryHelper.ProjectOnSurface(point, mask));
            }
            var res = new List<MeshInstance>();
            foreach (var point in points) {
                res.Add(Create(manager, meshPath, point, settings, randomRotation));
            }
            return res;
        }

        //deletes the instances of the type of the given settings within the radius (only for the types with massPlacement)
        public static int EraseInRadius(ElementManager manager, Vector3 center, float radius, ObjectState settings) {
            var type = RequireMassPlacement(settings).name;
            var delList = new List<MeshInstance>();
            foreach (var mesh in manager.meshes) {
                if (mesh.TypeName == type) {
                    // The root sits on the placement point (only its children are raised by the
                    // y offset), so it is compared as is.
                    // center is in program coordinates, so the position must be read through GO.
                    var dist = GameEngine.Vector3.Distance(GO.GetPosition(mesh), center);
                    if (dist < radius) {
                        delList.Add(mesh);
                    }
                }
            }
            foreach (var mesh in delList) {
                mesh.Delete();
            }
            return delList.Count;
        }

        //switches a plain instance to one of the compatibleTypes of its type: the parameters of both types are kept, the
        //others are removed (the new ones get their defaults); the layer and mesh transform follow the new rules
        public static void SetType(MeshInstance instance, string newType) {
            if (instance == null || instance.objectParameter != null) throw new System.ArgumentException("only plain mesh instances have a type");
            if (instance.TypeName == newType) return;
            var current = instance.GetMeshInstanceType();
            var target = RequireType(newType);
            if (current == null || !current.IsCompatibleWith(newType)) {
                throw new System.ArgumentException("mesh instance type '" + instance.TypeName + "' can't be switched to '" + newType + "'");
            }
            History.UndoManager.Touch(instance);
            var settings = instance.settings;
            foreach (var key in new List<string>(settings.properties.Keys)) {
                if (key == "type") continue;
                if (!HasParameter(target, key)) settings.RemoveProperty(key);
            }
            settings.SetStr("type", newType);
        }

        static bool HasParameter(MeshInstanceType type, string name) {
            foreach (var p in type.parameterContainer.parameters ?? new CityElements.Types.Parameter[0]) {
                if (p.fullName() == name) return true;
            }
            return false;
        }

        //replaces the mesh keeping the transform; returns the new instance (the old one is freed)
        public static MeshInstance ReplaceMesh(ElementManager manager, MeshInstance instance, string meshPath) {
            return instance.ReplaceMesh(meshPath, manager);
        }

        public static void ProjectToGround(Node3D instance) {
            //ProjectPoint works with program-space world positions
            GO.SetPosition(instance, GeometryHelper.ProjectPoint(GO.GetPosition(instance)));
        }

        // Parametric objects

        //the array element node a parametric object is generated for (null for the objects of the element itself)
        public static ArrayObject GetArrayObject(Node3D instance) {
            return GS.Cast<ArrayObject>(instance?.GetParent()?.GetParent() as Node3D);
        }

        //plain mesh instances and the objects of array elements can be deleted, the other parametric objects
        //(e.g. traffic lights) are generated by their parent
        public static bool CanDelete(MeshInstance instance) {
            if (instance == null) return false;
            return instance.objectParameter == null || GetArrayObject(instance) != null;
        }

        public static bool Delete(MeshInstance instance) {
            if (!CanDelete(instance)) return false;
            if (instance.objectParameter != null) {
                //remove the element from its array
                var arrObj = GetArrayObject(instance);
                var p = arrObj.state.GetParent();
                ArrayObject.PruneSelections(p);
                var arr = p.Array<ObjectState>(arrObj.paramInParent);
                var list = new List<ObjectState>(arr);
                list.Remove(arrObj.state);
                p.SetArray(arrObj.paramInParent, list.ToArray());
            } else {
                instance.Delete();
            }
            return true;
        }

        //the parametric object of the element for an objectInstance parameter. path: the array elements leading
        //to it, from the element ("param.index", or "param" for custom city properties), empty for the element's own ones
        public static MeshInstance FindParametricObject(IParametricObjectContainer owner, string key, IList<string> path) {
            var parentParams = new List<string>();
            if (path != null) {
                for (int i = path.Count - 1; i >= 0; i--) parentParams.Add(path[i]);
            }
            switch (owner) {
                case Road r: return r.FindParametricObject(key, parentParams);
                case CityProperties c: return c.FindParametricObject(key, parentParams);
                default: return null;
            }
        }

        //sets the transform of a parametric object relative to its base (null values are left as they are)
        public static void SetParametricTransform(MeshInstance instance, Vector3? localPosition, Vector3? localRotation, Vector3? localScale) {
            if (localPosition.HasValue) GO.SetLocalPosition(instance, localPosition.Value);
            if (localRotation.HasValue) GO.SetLocalRotation(instance, localRotation.Value);
            if (localScale.HasValue) GO.SetLocalScale(instance, localScale.Value);
            instance.SyncParametricObjectState();
        }

        public static void SetTransform(MeshInstance instance, Vector3? position, Quaternion? rotation, Vector3? scale) {
            if (position.HasValue) GO.SetPosition(instance, position.Value);
            if (rotation.HasValue) GO.SetRotation(instance, rotation.Value);
            if (scale.HasValue) GO.SetLocalScale(instance, scale.Value);
        }
    }
}
