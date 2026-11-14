using System.Collections.Generic;
using Godot;
using Lua;
using GameObject = Godot.Node3D;
using GO = GameEngine.GameObject;
using RC = RuntimeCalculator;
using States;

namespace CityElements.Types.Runtime {
    //the extras of a road or intersection type (see Types.ExtraInfo): their conditions, compiled for the variables of the
    //type, and their Lua functions
    public class ExtrasType {
        public class Extra {
            public string name;
            public RC.Booleans.Boolean condition; //null: always on
            public LuaFunction function;
        }

        public readonly List<Extra> extras = new List<Extra>();
        public readonly RC.Scripting.LuaScriptHost scriptHost;

        //null if the type has no extras
        public static ExtrasType Create(Types.ExtraInfo[] infos, RC.VariableContainer variableContainer, RC.Scripting.LuaScriptHost scriptHost = null) {
            if (infos == null || infos.Length == 0) return null;
            return new ExtrasType(infos, variableContainer, scriptHost);
        }

        ExtrasType(Types.ExtraInfo[] infos, RC.VariableContainer variableContainer, RC.Scripting.LuaScriptHost scriptHost) {
            this.scriptHost = scriptHost ?? RC.Scripting.LuaScriptHost.Get();
            foreach (var info in infos) {
                if (info.name == null || info.geometry == null) throw new System.Exception("an extra needs a name and a geometry function");
                var extra = new Extra { name = info.name, function = this.scriptHost.Resolve(info.geometry) };
                if (info.condition != null) {
                    extra.condition = RC.Parsers.BooleanParser.ParseExpression(info.condition);
                    extra.condition.SetIndices(variableContainer);
                }
                extras.Add(extra);
            }
        }
    }
}

//The extras of an element (a road or an intersection): the meshes made by the Lua functions of its type, in a child of its
//generator (not its first mesh, so they are neither picked nor exported with the element's mesh), and the data each one
//gives to the exporters
public class ElementExtras {
    GameObject container;
    MeshInstance3D mr;
    ArrayMesh m;
    bool empty = true; //the mesh has no surfaces
    //the extras made in the last rebuild, by name, with the data returned by their functions for the exporters
    public readonly List<(string name, object export)> built = new List<(string, object)>();

    public ElementExtras(GameObject parent) {
        container = GO.Create("Extras");
        GO.SetParent(container, parent);
        (mr, m) = GameEngine.Mesh.CreateMesh(container, new Material[0]);
    }

    //vars: the variables of the element, filled; context: makes the context of the functions (called only if an extra
    //is on, shared by them); name: of the element, for the errors
    public void Rebuild(CityElements.Types.Runtime.ExtrasType type, RC.VariableContainer vars, ObjectState state,
        System.Func<LuaTable> context, string name) {
        built.Clear();
        if (type == null && empty) return; //nothing to clear (the types without extras)
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var indices = new List<int[]>();
        var materials = new List<Material>();
        if (type != null) {
            LuaTable ctx = null;
            foreach (var extra in type.extras) {
                if (extra.condition != null && !extra.condition.GetValue(vars)) continue;
                var definition = name + " (" + extra.name + ")";
                try {
                    ctx ??= context();
                    ctx["extra"] = extra.name;
                    var errors = type.scriptHost.ErrorCount;
                    var result = type.scriptHost.Call(extra.function, RC.Scripting.LuaVariableView.Get(vars), ctx, definition);
                    if (type.scriptHost.ErrorCount != errors) continue;
                    foreach (var mesh in CustomElements.CustomElementScripting.ReadGeometry(result).meshes) {
                        if (mesh.pickOnly || mesh.placeholder || mesh.triangles.Count == 0) continue;
                        var offset = vertices.Count;
                        vertices.AddRange(mesh.vertices);
                        uvs.AddRange(mesh.uvs);
                        indices.Add(mesh.triangles.ConvertAll(i => i + offset).ToArray());
                        materials.Add(MeshMaterial(mesh, state));
                    }
                    var export = result.TryRead<LuaTable>(out var table) ? ToPlain(table["export"]) : null;
                    built.Add((extra.name, export));
                } catch (System.Exception e) {
                    type.scriptHost.ReportError(definition, e.Message);
                }
            }
        }
        var mats = materials.ToArray();
        GameEngine.Mesh.AssignMaterials(mr, mats);
        GameEngine.Mesh.AssignData(m, mats, indices, vertices, uvs, name + " (extras)");
        empty = mats.Length == 0;
    }

    public void Clear() {
        built.Clear();
        if (empty) return;
        GameEngine.Mesh.AssignMaterials(mr, new Material[0]);
        GameEngine.Mesh.Clear(m);
        empty = true;
    }

    public void SetVisible(bool visible) {
        mr.Visible = visible;
    }

    //the material of a mesh of a Lua script: its solid colour, or its texture (a texture parameter of the element, a
    //built-in material name, or a texture path such as "texture/a.tex" when the element has no value with that name),
    //a mesh with neither is not drawn
    public static Material MeshMaterial(CustomElements.GeometryMesh mesh, ObjectState state) {
        if (mesh.color.HasValue) return MaterialManager.GetHandleMaterial(mesh.color.Value);
        var texture = mesh.texture == null ? "" : MaterialManager.GetTextureName(state, mesh.texture);
        if (string.IsNullOrEmpty(texture) && mesh.texture != null && (mesh.texture.Contains('/') || mesh.texture.Contains('.'))) texture = mesh.texture;
        return MaterialManager.GetMaterial(string.IsNullOrEmpty(texture) ? "_TRANSPARENT_" : texture);
    }

    //the data given to the exporters: tables as lists (from 1) or objects, the vectors as lists of numbers
    public static object ToPlain(LuaValue value) {
        switch (value.Type) {
            case LuaValueType.Boolean: return value.Read<bool>();
            case LuaValueType.Number: return value.Read<double>();
            case LuaValueType.String: return value.Read<string>();
            case LuaValueType.UserData:
                if (value.TryRead<RC.Scripting.LuaVec3>(out var v3)) return new List<object> { (double)v3.value.X, (double)v3.value.Y, (double)v3.value.Z };
                if (value.TryRead<RC.Scripting.LuaVec2>(out var v2)) return new List<object> { (double)v2.value.X, (double)v2.value.Y };
                return null;
            case LuaValueType.Table:
                var table = value.Read<LuaTable>();
                if (table.ArrayLength > 0) {
                    var list = new List<object>();
                    for (int i = 1; i <= table.ArrayLength; i++) list.Add(ToPlain(table[i]));
                    return list;
                }
                var dictionary = new Dictionary<string, object>();
                var key = LuaValue.Nil;
                while (table.TryGetNext(key, out var pair)) {
                    key = pair.Key;
                    if (key.TryRead<string>(out var name)) dictionary[name] = ToPlain(pair.Value);
                }
                return dictionary;
            default: return null;
        }
    }
}
