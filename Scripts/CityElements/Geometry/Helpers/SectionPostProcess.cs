using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Lua;
using RuntimeCalculator.Scripting;
using RC = RuntimeCalculator;

//The post-process pass of the sections of a road or of a custom element spline ("postProcess" in the settings of the type):
//a function of the Lua scripts of the core gets all the sections, once they are computed, and can replace their points,
//frames and vertices, before the meshes, anchors, lanes and props are generated from them.
//All or nothing: an error or an invalid result is logged once and leaves every section as it was.
public static class SectionPostProcess {
    public class Section {
        public Vector3 point, direction, right, up; //right: with the width factor (the length of the offset unit)
        public float z, t; //the distance from the start, the curve parameter
        public float[] offsets; //the values of the section vertices of the type (vertex = point + right * offset)
        public Vector3[] vertices;
    }

    static readonly HashSet<string> editableFields = new HashSet<string> { "point", "right", "up", "vertices" };

    //The tables of the states, built only if the script reads them (most scripts don't need them)
    public static void AddStates(LuaTable ctx, ObjectState state, ObjectState instanceState) {
        var meta = new LuaTable();
        meta["__index"] = new LuaFunction("states", (context, ct) => {
            var table = context.GetArgument<LuaTable>(0);
            var key = context.GetArgument(1);
            var value = LuaValue.Nil;
            if (key.TryRead<string>(out var name)) {
                if (name == "state" && state != null) value = PropPlacerGeometry.StateTable(state);
                else if (name == "instanceState" && instanceState != null) value = PropPlacerGeometry.StateTable(instanceState);
                if (value.Type != LuaValueType.Nil) table[name] = value; //read once
            }
            return new ValueTask<int>(context.Return(value));
        });
        ctx.Metatable = meta;
    }

    //1-based Lua arrays of vectors
    public static LuaTable VectorList(IEnumerable<Vector3> values) {
        var table = new LuaTable(); int i = 0;
        foreach (var v in values) table[++i] = LuaVectors.ToLua(v);
        return table;
    }

    public static LuaTable VectorList(IEnumerable<Vector2> values) {
        var table = new LuaTable(); int i = 0;
        foreach (var v in values) table[++i] = LuaVectors.ToLua(v);
        return table;
    }

    static LuaTable SectionTable(Section s) {
        var table = new LuaTable();
        table["point"] = LuaVectors.ToLua(s.point);
        table["direction"] = LuaVectors.ToLua(s.direction);
        table["right"] = LuaVectors.ToLua(s.right);
        table["up"] = LuaVectors.ToLua(s.up);
        table["z"] = s.z;
        table["t"] = s.t;
        var offsets = new LuaTable();
        for (int i = 0; i < s.offsets.Length; i++) offsets[i + 1] = s.offsets[i];
        table["offsets"] = offsets;
        table["vertices"] = VectorList(s.vertices);
        return table;
    }

    static Vector3 ReadVector(LuaValue value, string what) {
        if (!value.TryRead<LuaVec3>(out var v)) throw new ArgumentException(what + " is not a vec3");
        if (!float.IsFinite(v.value.X) || !float.IsFinite(v.value.Y) || !float.IsFinite(v.value.Z)) throw new ArgumentException(what + " is not finite");
        return v.value;
    }

    //Calls the function with the variables of the element and ctx (the sections are added to it), then writes its result into
    //the sections. Returns the indices of the sections changed (empty if none, or if the script failed or gave an invalid result).
    public static List<int> Apply(LuaScriptHost host, LuaFunction function, string name, RC.VariableContainer vc, List<Section> sections, LuaTable ctx) {
        var changed = new List<int>();
        var list = new LuaTable();
        for (int i = 0; i < sections.Count; i++) list[i + 1] = SectionTable(sections[i]);
        ctx["sections"] = list;
        var result = host.Call(function, LuaVariableView.Get(vc), ctx, name);
        if (result.Type == LuaValueType.Nil) return changed; //nothing to change (or an error, already reported)
        try {
            var edits = Read(result, sections);
            foreach (var (i, edit) in edits) {
                var s = sections[i];
                if (edit.point.HasValue) s.point = edit.point.Value;
                if (edit.right.HasValue) s.right = edit.right.Value;
                if (edit.up.HasValue) s.up = edit.up.Value;
                if (edit.vertices != null) s.vertices = edit.vertices;
                changed.Add(i);
            }
        } catch (ArgumentException e) {
            host.ReportError(name, e.Message);
        }
        return changed;
    }

    //The edits of the result, checked before any of them is applied
    static List<(int, (Vector3? point, Vector3? right, Vector3? up, Vector3[] vertices))> Read(LuaValue result, List<Section> sections) {
        var res = new List<(int, (Vector3?, Vector3?, Vector3?, Vector3[]))>();
        if (!result.TryRead<LuaTable>(out var table)) throw new ArgumentException("the result must be a table (or nil)");
        var list = table["sections"];
        if (list.Type == LuaValueType.Nil) return res;
        if (!list.TryRead<LuaTable>(out var entries)) throw new ArgumentException("sections must be a table");
        var key = LuaValue.Nil;
        while (entries.TryGetNext(key, out var pair)) {
            key = pair.Key;
            if (!key.TryRead<double>(out var index) || index != Math.Floor(index) || index < 1 || index > sections.Count) {
                throw new ArgumentException("invalid section index " + key + " (the sections are 1 to " + sections.Count + ")");
            }
            var i = (int)index - 1;
            if (pair.Value.Type == LuaValueType.Nil || (pair.Value.TryRead<bool>(out var b) && !b)) continue;
            if (!pair.Value.TryRead<LuaTable>(out var entry)) throw new ArgumentException("section " + (i + 1) + " must be a table");
            var what = "section " + (i + 1);
            Vector3? point = null, right = null, up = null;
            Vector3[] vertices = null;
            var fieldKey = LuaValue.Nil;
            while (entry.TryGetNext(fieldKey, out var field)) {
                fieldKey = field.Key;
                if (!fieldKey.TryRead<string>(out var fieldName) || !editableFields.Contains(fieldName)) {
                    throw new ArgumentException(what + ": unknown field " + fieldKey + " (only point, right, up and vertices can be changed)");
                }
            }
            if (entry["point"].Type != LuaValueType.Nil) point = ReadVector(entry["point"], what + " point");
            if (entry["right"].Type != LuaValueType.Nil) {
                right = ReadVector(entry["right"], what + " right");
                if (right.Value.LengthSquared() < 1e-12f) throw new ArgumentException(what + " right is zero");
            }
            if (entry["up"].Type != LuaValueType.Nil) up = ReadVector(entry["up"], what + " up");
            if (entry["vertices"].Type != LuaValueType.Nil) {
                if (!entry["vertices"].TryRead<LuaTable>(out var verticesTable)) throw new ArgumentException(what + " vertices must be a table");
                var count = sections[i].vertices.Length;
                if (verticesTable.ArrayLength != count) throw new ArgumentException(what + " has " + verticesTable.ArrayLength + " vertices instead of " + count);
                vertices = new Vector3[count];
                for (int j = 0; j < count; j++) vertices[j] = ReadVector(verticesTable[j + 1], what + " vertex " + (j + 1));
            }
            res.Add((i, (point, right, up, vertices)));
        }
        return res;
    }
}
