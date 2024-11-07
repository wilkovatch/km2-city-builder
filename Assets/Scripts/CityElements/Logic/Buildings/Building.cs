using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using States;
using SubMeshData = GeometryHelpers.SubMesh.SubMeshData;
using RC = RuntimeCalculator;

public class Building: IObjectWithState {
    public List<(Vector3 point, Vector3 normal)> spline;
    public List<Vector3> splineActualNormals = new List<Vector3>();
    public TerrainPoint firstPoint, lastPoint;
    public ObjectState State { get { return state; } set { changed = true; state = value; } }
    public ObjectState state; //DO NOT MODIFY DIRECTLY UNLESS ABSOLUTELY NEEDED
    public BuildingSideGenerator front = null, left = null, right = null, back = null;
    bool deleted = false;
    public BuildingLine line;
    bool outlined = false;
    bool highlighted = false;
    public GameObject container;
    public TerrainSubmeshGenerator roof;
    public bool enabled = true;

    bool old_enabled = true;
    public bool changed = true;
    public float height;
    float maxHeight, trueHeight;
    List<Vector3> vecSpline, backSpline, leftSpline, rightSpline, topSpline;
    int updateRes;
    CityElements.Types.Runtime.Buildings.BuildingType.Building curType;
    RC.VariableContainer variableContainer;

    public void Initialize(BuildingLine line, ObjectState newState) {
        state = (ObjectState)newState.Clone();
        this.line = line;
    }

    public bool HasCollider(MeshCollider c) {
        return GetSideFromCollider(c) != null || (roof != null && roof.mc == c);
    }

    public BuildingSideGenerator GetSideFromCollider(MeshCollider c) {
        BuildingSideGenerator res = null;
        if (front != null && front.mc == c) res = front;
        else if (left != null && left.mc == c) res = left;
        else if (right != null && right.mc == c) res = right;
        else if (back != null && back.mc == c) res = back;
        return res;
    }

    public void Delete() {
        if (front != null) front.Delete();
        if (left != null) left.Delete();
        if (right != null) right.Delete();
        if (back != null) back.Delete();
        if (roof != null) roof.Delete();
        deleted = true;
    }

    public void SetState(ObjectState newState) {
        state = (ObjectState)newState.Clone();
        state.FlagAsChanged();
        if (state.Bool("front")) state.State("frontState").FlagAsChanged();
        if (state.Bool("left")) state.State("leftState").FlagAsChanged();
        if (state.Bool("right")) state.State("rightState").FlagAsChanged();
        if (state.Bool("back")) state.State("backState").FlagAsChanged();
        changed = true;
    }

    public ObjectState GetState() {
        return state;
    }

    void ReloadType() {
        var type = GetBuildingType();
        if (type != curType) {
            variableContainer = GetBuildingType().variableContainer.GetClone();
            curType = type;
        }
        curType.FillInitialVariables(variableContainer, state);
    }

    public CityElements.Types.Runtime.Buildings.BuildingType.Building GetBuildingType() {
        var dict = CityElements.Types.Parsers.TypeParser.GetBuildingTypes();
        return dict[line.state.Str("type", null)].building;
    }

    public void RefreshOutline() {
        SetOutline(outlined, highlighted);
    }

    public List<SubMeshData> GetSubmesh() {
        var smd = new List<SubMeshData>();
        if (front != null) smd.Add(front.GetSubmesh());
        if (left != null) smd.Add(left.GetSubmesh());
        if (right != null) smd.Add(right.GetSubmesh());
        if (back != null) smd.Add(back.GetSubmesh());
        if (roof != null) smd.Add(roof.GetSubmesh());
        return smd;
    }

    public void SetOutline(bool active, bool highlighted = true) {
        if (deleted) return;
        outlined = active;
        this.highlighted = highlighted;
        var trueOutlined = outlined && enabled;
        if (front != null) front.SetOutline(trueOutlined, highlighted);
        if (left != null) left.SetOutline(trueOutlined, highlighted);
        if (right != null) right.SetOutline(trueOutlined, highlighted);
        if (back != null) back.SetOutline(trueOutlined, highlighted);
    }

    BuildingSideGenerator GetSide(bool enabled, BuildingSideGenerator current, ObjectState state, BuildingSideGenerator.Side? side) {
        if (enabled) {
            if (current == null) {
                var newSide = new BuildingSideGenerator(this, state, side.Value, outlined);
                return newSide;
            } else {
                return current;
            }
        } else {
            if (current != null) current.Delete();
            return null;
        }
    }

    public void LateUpdate() {
        if (front != null) front.LateUpdate();
        if (left != null) left.LateUpdate();
        if (right != null) right.LateUpdate();
        if (back != null) back.LateUpdate();
    }

    TerrainSubmeshGenerator GetRoof(bool enabled, TerrainSubmeshGenerator current) {
        if (enabled) {
            if (current == null) {
                var newSide = new TerrainSubmeshGenerator(line);
                return newSide;
            } else {
                return current;
            }
        } else {
            if (current != null) current.Delete();
            return null;
        }
    }

    public bool DidChange() {
        return changed || old_enabled != enabled || state.HasChanged();
    }

    void CleanTemp() {
        vecSpline.Clear();
        backSpline.Clear();
        leftSpline.Clear();
        rightSpline.Clear();
        topSpline.Clear();
    }

    public void PreUpdateMesh(bool hasExternalHeight, float externalHeight, float externalMaxHeight, bool force) {
        updateRes = -1;
        if (deleted) {
            updateRes = 0;
            return;
        }
        var anySideChanged = (front != null && front.state.HasChanged()) ||
            (left != null && left.state.HasChanged()) ||
            (right != null && right.state.HasChanged()) ||
            (back != null && back.state.HasChanged());
        if (!force && !DidChange() && !anySideChanged) {
            updateRes = 0;
            return;
        }
        if (!enabled) {
            front?.Delete();
            back?.Delete();
            left?.Delete();
            right?.Delete();
            roof?.Delete();
            front = null;
            back = null;
            left = null;
            right = null;
            roof = null;

            old_enabled = enabled;

            updateRes = 1;
            return;
        }
        vecSpline = new List<Vector3>();
        foreach (var v in spline) {
            vecSpline.Add(v.point);
        }
        splineActualNormals.Clear();
        for (int i = 0; i < vecSpline.Count; i++) {
            var i0 = i == 0 ? 0 : i - 1;
            var i1 = i == 0 ? 1 : i;
            var dir = vecSpline[i1] - vecSpline[i0];
            var n = Vector3.Cross(dir, Vector3.up).normalized;
            splineActualNormals.Add(n);

        }
        backSpline = new List<Vector3>(vecSpline);
        for (int i = 0; i < spline.Count; i++) {
            backSpline[vecSpline.Count - 1 - i] = spline[i].point + spline[i].normal * state.Float("depth");
        }

        leftSpline = new List<Vector3>();
        leftSpline.Add(vecSpline[vecSpline.Count - 1]);
        leftSpline.Add(backSpline[0]);

        rightSpline = new List<Vector3>();
        rightSpline.Add(backSpline[backSpline.Count - 1]);
        rightSpline.Add(vecSpline[0]);

        //remove intersections from back spline
        backSpline = GeometryHelper.GetLineWithoutIntersections(backSpline);

        //clean the back spline (needed for acute angled building lines)
        var l00 = leftSpline[0];
        var l01 = leftSpline[1];
        var l10 = rightSpline[1];
        var l11 = rightSpline[0];
        var plane0 = new Plane(l00, l01, l00 + Vector3.up);
        var plane1 = new Plane(l10, l11, l10 + Vector3.up);
        if (state.Bool("fixAcuteAngles")) {
            var backSpline2 = new List<Vector3>();
            backSpline2.Add(backSpline[0]);
            for (int i = 1; i < backSpline.Count - 1; i++) {
                var p = backSpline[i];
                var d0 = plane0.GetDistanceToPoint(p);
                var d1 = plane1.GetDistanceToPoint(p);
                var include = d0 * d1 <= 0 || Mathf.Min(Mathf.Abs(d0), Mathf.Abs(d1)) < GeometryHelper.epsilon;
                include = include && GeometryHelper.FindVector(backSpline2, p) == -1;
                if (include) backSpline2.Add(p);
            }
            backSpline2.Add(backSpline[backSpline.Count - 1]);
            var dist0 = state.Float("depth") / Mathf.Sqrt(GeometryHelper.PointPolygonSquaredDistance(backSpline2[0], vecSpline, false));
            var dist1 = state.Float("depth") / Mathf.Sqrt(GeometryHelper.PointPolygonSquaredDistance(backSpline2[backSpline2.Count - 1], vecSpline, false));
            backSpline2[0] = vecSpline[vecSpline.Count - 1] + (backSpline2[0] - vecSpline[vecSpline.Count - 1]) * dist0;
            backSpline2[backSpline2.Count - 1] = vecSpline[0] + (backSpline2[backSpline2.Count - 1] - vecSpline[0]) * dist1;
            backSpline = backSpline2;
            leftSpline[1] = backSpline[0];
            rightSpline[0] = backSpline[backSpline.Count - 1];
        }

        height = hasExternalHeight ? externalHeight : state.Float("height");
        maxHeight = externalMaxHeight;
        if (!hasExternalHeight) {
            var maxH = float.MinValue;
            foreach (var p in vecSpline) {
                if (p.y > maxH) maxH = p.y;
            }
            foreach (var p in backSpline) {
                if (p.y > maxH) maxH = p.y;
            }
            height = maxH + state.Float("height");
            maxHeight = maxH;
        }
        trueHeight = height - maxHeight;

        topSpline = new List<Vector3>();
        topSpline.AddRange(vecSpline);
        topSpline.AddRange(backSpline);
        for (int i = 0; i < topSpline.Count; i++) {
            var p = topSpline[i];
            var p2 = new Vector3(p.x, height, p.z);
            topSpline[i] = p2;
        }
    }

    public int UpdateMesh(bool prevActive, bool nextActive, float? prevHeight, float? nextHeight, float? prevDepth, float? nextDepth, bool? prevFixAngles, bool? nextFixAngles) {
        if (updateRes != -1) return updateRes;
        ReloadType();
        if (state.Str("type") != curType.name) state.SetStr("type", curType.name);
        bool CheckFloat(float? val, float thisVal) { return !val.HasValue || val.Value < thisVal; }
        bool CheckBool(bool? val, bool thisVal) { return !val.HasValue || val.Value != thisVal; }
        var rightOkay = !prevActive || CheckFloat(prevHeight, height) || CheckFloat(prevDepth, state.Float("depth")) || CheckBool(prevFixAngles, state.Bool("fixAcuteAngles"));
        var leftOkay = !nextActive || CheckFloat(nextHeight, height) || CheckFloat(nextDepth, state.Float("depth")) || CheckBool(nextFixAngles, state.Bool("fixAcuteAngles"));

        front = GetSide(state.Bool("front"), front, state.State("frontState"), BuildingSideGenerator.Side.Front);
        left = GetSide(state.Bool("left") && leftOkay, left, state.State("leftState"), BuildingSideGenerator.Side.Left);
        right = GetSide(state.Bool("right") && rightOkay, right, state.State("rightState"), BuildingSideGenerator.Side.Right);
        back = GetSide(state.Bool("back"), back, state.State("backState"), BuildingSideGenerator.Side.Back);
        roof = GetRoof(state.Bool("top"), roof);

        var vc = variableContainer;

        var res = 0;
        var fS = state.State("frontState", false);
        var olfFChanged = fS.HasChanged();
        var allEqual = state.Bool("allSidesEqual");

        if (front != null) res += front.UpdateMesh(vecSpline, height, trueHeight, maxHeight, fS);
        if (allEqual && olfFChanged) fS.FlagAsChanged();

        if (left != null) res += left.UpdateMesh(leftSpline, height, trueHeight, maxHeight, allEqual ? fS : state.State("leftState"));
        if (allEqual && olfFChanged) fS.FlagAsChanged();

        if (right != null) res += right.UpdateMesh(rightSpline, height, trueHeight, maxHeight, allEqual ? fS : state.State("rightState"));
        if (allEqual && olfFChanged) fS.FlagAsChanged();

        if (back != null) res += back.UpdateMesh(backSpline, height, trueHeight, maxHeight, allEqual ? fS : state.State("backState"));

        if (roof != null) {
            try {
                res += roof.UpdateMesh(topSpline, state.Str("topTexture"), vc.floats[vc.floatIndex["uMult"]], vc.floats[vc.floatIndex["vMult"]]);
            } catch (System.Exception e) {
                Debug.LogWarning("Error during mesh generation on building " + container.name + ": " + e.StackTrace.ToString());
            }
        }

        old_enabled = enabled;
        changed = false;
        state.FlagAsUnchanged();
        CleanTemp();

        return res;
    }
}
