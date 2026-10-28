using System.Collections.Generic;
using Godot;
using PhysicsScriptable = Godot.StaticBody3D;
using Mesh = Godot.ArrayMesh;
using MeshRenderer = Godot.MeshInstance3D;
using MeshCollider = Godot.CollisionShape3D;
using LineRenderer = GameEngine.LineRenderer;
using GO = GameEngine.GameObject;
using GameObject = Godot.Node3D;
using V3 = GameEngine.Vector3;

//The geometry of a low-level element, as built by the geometry script of its type: the meshes (one submesh per mesh), the
//lines and a collider (the meshes and a strip along each line) to select the element by clicking on it. The placeholder
//meshes are kept apart (not exported), drawn only while the element is a container whose contents draw nothing
public partial class LowLevelGenerator : PhysicsScriptable {
    const float MinLineWidth = 0.1f;
    const float MinColliderLineWidth = 0.5f;

    public MeshRenderer mr;
    public Mesh m;
    MeshCollider mc;
    Mesh colliderMesh;
    readonly List<(LineRenderer lr, LowLevel.GeometryLine line)> lines = new List<(LineRenderer, LowLevel.GeometryLine)>();
    bool linesShown = false;
    bool containerShown = false; //the container of the element, or an element in it, is being edited (see ShowContainerLines)
    //the placeholder meshes, under a node of their own (so that the export, which takes the first renderer, ignores them)
    GameObject placeholderContainer;
    MeshRenderer placeholderMr;
    Mesh placeholderM;
    List<Vector3> placeholderVertices = new List<Vector3>();
    List<int> placeholderIndices = new List<int>();
    bool placeholderShown = false;
    //the collider as last built from the meshes (the line strips are added to it), to rebuild it when a view toggle changes
    List<Vector3> meshColliderVertices = new List<Vector3>();
    List<int[]> meshColliderIndices = new List<int[]>();

    //the meshes as built (for the export): their textures, by submesh
    public readonly List<string> textures = new List<string>();
    //the props of the prop rule (see PropPlacer), placed along the propLines of the geometry
    public GameObject propsContainer;
    public readonly List<(string container, string meshName, GameObject obj)> props = new List<(string, string, GameObject)>();

    public void Initialize() {
        GO.SetLayer(this, 7);
        (mr, m) = GameEngine.Mesh.CreateMesh(GO.GetObject(this), new Material[] { MaterialManager.GetMaterial("") });
        colliderMesh = new Mesh();
        mc = GameEngine.Mesh.CreateCollider(GO.GetObject(this), colliderMesh);
        propsContainer = GO.Create("PropsContainer");
        GO.SetParent(propsContainer, this);
        placeholderContainer = GO.Create("Placeholder");
        GO.SetParent(placeholderContainer, this);
        (placeholderMr, placeholderM) = GameEngine.Mesh.CreateMesh(placeholderContainer, new Material[] { MaterialManager.GetMaterial("") });
        GO.SetLayer(placeholderMr, 7);
        placeholderMr.Visible = false;
    }

    //the props of the prop rule of the state, for the containers of the type
    void PlaceProps(List<LowLevel.GeometryPropLine> propLines, ObjectState state, string[] containers) {
        GO.Destroy(propsContainer);
        props.Clear();
        propsContainer = GO.Create("PropsContainer");
        GO.SetParent(propsContainer, this);
        if (GetParent() is PropPath) {
            PropPlacer.PlaceRibbons(state.State("propRule"), propLines, propsContainer, props);
            return;
        }
        PropPlacer.Place(state.State("propRule"), containers, container => {
            var line = propLines.Find(l => l.container == container);
            return line == null ? null : (line.startBound.ToArray(), line.endBound.ToArray());
        }, propsContainer, props);
    }

    public void Rebuild(LowLevel.GeometryResult geometry, ObjectState state, string[] propsContainers) {
        textures.Clear();
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var indices = new List<int[]>();
        var materials = new List<Material>();
        var pickVertices = new List<Vector3>();
        var pickIndices = new List<int>();
        var phVertices = new List<Vector3>();
        var phUVs = new List<Vector2>();
        var phIndices = new List<int[]>();
        var phMaterials = new List<Material>();
        foreach (var mesh in geometry.meshes) {
            if (mesh.placeholder) {
                var phOffset = phVertices.Count;
                phVertices.AddRange(mesh.vertices);
                phUVs.AddRange(mesh.uvs);
                phIndices.Add(mesh.triangles.ConvertAll(i => i + phOffset).ToArray());
                var phTexture = mesh.texture == null ? "" : MaterialManager.GetTextureName(state, mesh.texture);
                phMaterials.Add(MaterialManager.GetMaterial(string.IsNullOrEmpty(phTexture) ? "_TRANSPARENT_" : phTexture));
                continue;
            }
            if (mesh.pickOnly) {
                var pickOffset = pickVertices.Count;
                pickVertices.AddRange(mesh.vertices);
                foreach (var i in mesh.triangles) pickIndices.Add(i + pickOffset);
                continue;
            }
            var offset = vertices.Count;
            vertices.AddRange(mesh.vertices);
            uvs.AddRange(mesh.uvs);
            var submesh = new int[mesh.triangles.Count];
            for (int i = 0; i < submesh.Length; i++) submesh[i] = mesh.triangles[i] + offset;
            indices.Add(submesh);
            var texture = mesh.texture == null ? "" : MaterialManager.GetTextureName(state, mesh.texture);
            textures.Add(texture);
            //a mesh without a texture is not drawn
            materials.Add(MaterialManager.GetMaterial(string.IsNullOrEmpty(texture) ? "_TRANSPARENT_" : texture));
        }
        if (indices.Count == 0) {
            GameEngine.Mesh.Clear(m);
        } else {
            GameEngine.Mesh.AssignMaterials(mr, materials.ToArray());
            GameEngine.Mesh.AssignData(m, materials.ToArray(), indices, vertices, uvs, GO.GetName(this));
        }
        RebuildLines(geometry.lines, state);
        var colliderIndices = new List<int[]>(indices);
        if (pickIndices.Count > 0) {
            var pickOffset = vertices.Count;
            vertices.AddRange(pickVertices);
            colliderIndices.Add(pickIndices.ConvertAll(i => i + pickOffset).ToArray());
        }
        meshColliderVertices = vertices;
        meshColliderIndices = colliderIndices;
        if (phIndices.Count == 0) {
            GameEngine.Mesh.Clear(placeholderM);
        } else {
            GameEngine.Mesh.AssignMaterials(placeholderMr, phMaterials.ToArray());
            GameEngine.Mesh.AssignData(placeholderM, phMaterials.ToArray(), phIndices, phVertices, phUVs, GO.GetName(this) + " placeholder");
        }
        placeholderVertices = phVertices;
        placeholderIndices = new List<int>();
        foreach (var submesh in phIndices) placeholderIndices.AddRange(submesh);
        RebuildCollider(geometry.lines);
        PlaceProps(geometry.propLines, state, propsContainers);
    }

    void RebuildLines(List<LowLevel.GeometryLine> newLines, ObjectState state) {
        while (lines.Count > newLines.Count) {
            GO.Destroy(lines[lines.Count - 1].lr);
            lines.RemoveAt(lines.Count - 1);
        }
        for (int i = 0; i < newLines.Count; i++) {
            var line = newLines[i];
            var lr = i < lines.Count ? lines[i].lr : GO.CreateLineRenderer(GO.GetObject(this), "Line " + i);
            if (i < lines.Count) lines[i] = (lr, line);
            else lines.Add((lr, line));
            var points = new List<Vector3>(line.points);
            if (line.closed) points.Add(points[0]);
            lr.SetPositions(points.ToArray());
            var texture = line.texture != null ? MaterialManager.GetTextureName(state, line.texture) : null;
            lr.material = !string.IsNullOrEmpty(texture) ? MaterialManager.GetMaterial(texture) : MaterialManager.GetHandleMaterial(line.color);
            lr.heightOffset = line.heightOffset;
            if (line.width.HasValue) {
                lr.startWidth = line.width.Value;
                lr.endWidth = line.width.Value;
                lr.frameUpdate = null;
            } else {
                lr.startWidth = MinLineWidth;
                lr.endWidth = MinLineWidth;
                lr.frameUpdate = () => UpdateLineWidth(lr);
            }
            lr.enabled = IsShown(line);
        }
    }

    //an always shown line is hidden by its view toggle while it's off, unless the element is edited
    static bool AlwaysShown(LowLevel.GeometryLine line) {
        return line.always && Rendering.ViewToggles.IsOn(line.toggle);
    }

    bool IsShown(LowLevel.GeometryLine line) {
        return AlwaysShown(line) || linesShown || (line.withContainer && containerShown);
    }

    //a thin line keeps about the same size on screen
    void UpdateLineWidth(LineRenderer lr) {
        if (!lr.IsVisibleInTree() || lr.positionCount < 2) return;
        var cam = GameEngine.Camera.Get(this);
        if (cam == null) return;
        var points = new List<Vector3>();
        for (int i = 0; i < lr.positionCount; i++) points.Add(lr.GetPosition(i));
        var camPos = GO.GetPosition(cam);
        var dist = V3.Distance(GeometryHelper.ClosestPointToCurve(camPos, points, out _), camPos);
        var width = Mathf.Max(MinLineWidth * (dist / 20.0f), MinLineWidth);
        lr.startWidth = width;
        lr.endWidth = width;
    }

    //the lines not shown always are shown while the element is edited
    public void ShowLines(bool show) {
        linesShown = show;
        foreach (var (lr, line) in lines) {
            if (lr != null) lr.enabled = IsShown(line);
        }
    }

    //the lines shown with the container (withContainer) are shown while it, or an element in it, is being edited
    public void ShowContainerLines(bool show) {
        if (containerShown == show) return;
        containerShown = show;
        ShowLines(linesShown);
    }

    //whether the element draws a mesh (not counting the placeholders)
    public bool DrawsMesh() {
        return textures.Exists(t => !string.IsNullOrEmpty(t));
    }

    public bool HasPlaceholder() {
        return placeholderIndices.Count > 0;
    }

    //the placeholder meshes are shown (and can be clicked) while the contents of the element draw nothing
    public void ShowPlaceholder(bool show) {
        if (placeholderShown == show) return;
        placeholderShown = show;
        placeholderMr.Visible = show;
        RebuildCollider(lines.ConvertAll(l => l.line));
    }

    //after a view toggle changed
    public void RefreshVisibility() {
        ShowLines(linesShown);
        RebuildCollider(lines.ConvertAll(l => l.line));
    }

    void RebuildCollider(List<LowLevel.GeometryLine> newLines) {
        var cVertices = new List<Vector3>(meshColliderVertices);
        var cIndices = new List<int>();
        foreach (var submesh in meshColliderIndices) cIndices.AddRange(submesh);
        if (placeholderShown && placeholderIndices.Count > 0) {
            var offset = cVertices.Count;
            cVertices.AddRange(placeholderVertices);
            foreach (var i in placeholderIndices) cIndices.Add(i + offset);
        }
        foreach (var line in newLines) {
            if (!AlwaysShown(line)) continue; //the lines shown only while the element is edited (or hidden by a view toggle) can't be clicked
            var width = Mathf.Max(line.width ?? 0, MinColliderLineWidth);
            var points = new List<Vector3>(line.points);
            if (line.closed) points.Add(points[0]);
            for (int i = 0; i < points.Count - 1; i++) {
                var a = points[i] + V3.up * line.heightOffset;
                var b = points[i + 1] + V3.up * line.heightOffset;
                var dir = b - a;
                dir.Y = 0;
                if (dir.LengthSquared() < 1e-8f) continue;
                var side = V3.Normalize(V3.Cross(V3.up, dir)) * (width * 0.5f);
                var n = cVertices.Count;
                cVertices.Add(a - side);
                cVertices.Add(a + side);
                cVertices.Add(b - side);
                cVertices.Add(b + side);
                cIndices.AddRange(new[] { n, n + 2, n + 1, n + 1, n + 2, n + 3 });
            }
        }
        if (cIndices.Count == 0) {
            GameEngine.Mesh.Clear(colliderMesh);
            GameEngine.Mesh.RefreshCollider(colliderMesh, mc, false);
            return;
        }
        GameEngine.Mesh.AssignData(colliderMesh, cIndices, cVertices);
        GameEngine.Mesh.RefreshCollider(colliderMesh, mc, true);
    }

    public void Clear() {
        GameEngine.Mesh.Clear(m);
        foreach (var (lr, _) in lines) GO.Destroy(lr);
        lines.Clear();
        textures.Clear();
        meshColliderVertices = new List<Vector3>();
        meshColliderIndices = new List<int[]>();
        GameEngine.Mesh.Clear(placeholderM);
        placeholderVertices = new List<Vector3>();
        placeholderIndices = new List<int>();
        GameEngine.Mesh.Clear(colliderMesh);
        GameEngine.Mesh.RefreshCollider(colliderMesh, mc, false);
        PlaceProps(new List<LowLevel.GeometryPropLine>(), new ObjectState(), null);
    }
}
