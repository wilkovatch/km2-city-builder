using System.Collections;
using System.Collections.Generic;
using Godot;
using Input = GameEngine.Input;
using KeyCommand = GameEngine.Input.KeyCommand;
using RaycastHit = GameEngine.Raycast.RaycastHit;
using GS = GameEngine.Scriptable;
using GO = GameEngine.GameObject;
using SelectionOutline = RuntimeGizmos.SelectionOutline;

namespace ElementPlacer {
    //Picks an element of the city with a click, for the tools working on a second element (the property eyedropper,
    //merging terrain patches or building lines): the element under the mouse is outlined, in green if it is accepted, in
    //red otherwise. Unlike the ReferencePicker it also picks mesh instances, and single buildings when asked. It works over
    //the click mode there is, without replacing it (see TerrainClick.overlay), and ends with the click on an accepted
    //element, when that click mode changes or with Escape (see CityBuilderMenuBar).
    public class ElementPicker : TerrainAction {
        CityBuilderMenuBar builder;
        System.Func<object, bool> accepts;
        System.Action<object> onPick;
        System.Action onEnd;
        TerrainAction previous;
        object hovered = null;
        bool buildings;
        bool active = false;

        static readonly Color AcceptedColor = new Color(0.2f, 1.0f, 0.3f);
        static readonly Color RejectedColor = new Color(1.0f, 0.25f, 0.2f);
        SelectionOutline outline;
        List<GeometryInstance3D> outlined = new List<GeometryInstance3D>();
        //a single building is a part of the mesh of its line: a copy of its shape, seen only by the outline
        MeshInstance3D buildingShape;
        Building shapeOf;

        public ElementPicker(CityBuilderMenuBar builder) {
            this.builder = builder;
        }

        //(another click mode set meanwhile ends it)
        public bool IsActive() {
            return active && builder.terrainClick.overlay == this;
        }

        //buildings: a click on a building picks it instead of its line; onEnd: after the pick or its end
        public void Start(System.Func<object, bool> accepts, System.Action<object> onPick, System.Action onEnd = null, bool buildings = false) {
            Stop();
            builder.outlinePicker.Stop(); //one tool over the click mode at a time
            this.accepts = accepts;
            this.onPick = onPick;
            this.onEnd = onEnd;
            this.buildings = buildings;
            active = true;
            ListHighlight.suspended = true; //its render layer is used by the outline
            //over the click mode there is, which stays: the open editor gets the element it edits from it
            previous = builder.terrainClick.modifier;
            builder.terrainClick.overlay = this;
        }

        public void Stop() {
            End(null);
        }

        //ended before the element is given: the action can start another pick
        void End(System.Action action) {
            if (!active) return;
            var end = onEnd;
            active = false;
            accepts = null;
            onPick = null;
            onEnd = null;
            hovered = null;
            if (builder.terrainClick.overlay == this) builder.terrainClick.overlay = null;
            previous = null;
            ShowOutline(null, false);
            shapeOf = null; //its geometry can change before the next pick
            ListHighlight.suspended = false;
            action?.Invoke();
            end?.Invoke();
        }

        //picks the element (what a click on it does), returns whether it was accepted
        public bool Pick(object element) {
            if (!IsActive() || element == null || !accepts(element)) return false;
            var pick = onPick;
            End(() => pick?.Invoke(element));
            return true;
        }

        public override void Apply(List<RaycastHit?> hits) {
            if (!IsActive()) return;
            if (builder.terrainClick.modifier != previous) {
                Stop(); //the editor it was started from was closed, or changed what it does with the clicks
                return;
            }
            ShowOnMap(hits);
            if (!Input.GetInputStart(KeyCommand.Select) || hovered == null) return;
            CityBuilderMenuBar.staticGizmo?.ConsumeClick(); //the click is not a selection
            Pick(hovered);
        }

        public override void ShowOnMap(List<RaycastHit?> hits) {
            hovered = null;
            var element = hits.Count > 0 && hits[0].HasValue ? ElementOf(hits[0].Value) : null;
            var ok = element != null && accepts(element);
            if (ok) hovered = element;
            ShowOutline(element, ok);
        }

        // The outline of the element under the mouse

        void ShowOutline(object element, bool ok) {
            foreach (var render in outlined) SelectionOutline.RemoveFromMask(render, ListHighlight.Layer);
            outlined = new List<GeometryInstance3D>();
            if (element is Building building) {
                var shape = BuildingShape(building);
                if (shape != null) outlined.Add(shape);
            } else if (element != null) {
                var node = element is Intersection intersection ? intersection.geo : element as Node3D;
                if (!GO.IsNull(node)) outlined.AddRange(SelectionOutline.GetRenderers(node));
            }
            if (outline == null) {
                if (outlined.Count == 0) return;
                outline = new SelectionOutline { Name = "PickerOutline", maskLayer = ListHighlight.Layer, outlineColor = AcceptedColor };
                builder.gizmo.AddChild(outline);
            }
            outline.SetColor(ok ? AcceptedColor : RejectedColor);
            outline.UpdateOutline(outlined);
        }

        //the copy of the shape of the building (in no render layer but the one of the outline)
        MeshInstance3D BuildingShape(Building building) {
            if (buildingShape == null || !GodotObject.IsInstanceValid(buildingShape)) {
                buildingShape = new MeshInstance3D {
                    Name = "PickedBuildingShape",
                    TopLevel = true,
                    Layers = 0,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, CullMode = BaseMaterial3D.CullModeEnum.Disabled }
                };
                builder.helper.elementManager.AddChild(buildingShape);
                shapeOf = null;
            }
            if (shapeOf != building) {
                shapeOf = building;
                var data = GeometryHelpers.SubMesh.MergeSubmeshes(building.GetSubmesh(), GameEngine.Vector3.zero, GameEngine.Vector3.one, 0);
                var vertices = new Vector3[data.vertices?.Length ?? 0];
                for (int i = 0; i < vertices.Length; i++) vertices[i] = GameEngine.Vector3.DecodePosition(data.vertices[i]);
                var indices = new List<int>();
                if (data.indices != null) foreach (var part in data.indices) indices.AddRange(part);
                var mesh = new ArrayMesh();
                if (vertices.Length > 0 && indices.Count > 0) {
                    var arrays = new Godot.Collections.Array();
                    arrays.Resize((int)Mesh.ArrayType.Max);
                    arrays[(int)Mesh.ArrayType.Vertex] = vertices;
                    arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
                    mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                }
                buildingShape.Mesh = mesh;
            }
            return buildingShape.Mesh.GetSurfaceCount() > 0 ? buildingShape : null;
        }

        object ElementOf(RaycastHit hit) {
            var element = Operations.GroupTransform.ElementOf(hit.collider as Node);
            if (buildings && element is BuildingLine line && hit.shape != null) {
                var building = line.GetColliderBuilding(hit.shape) ?? line.GetColliderSide(hit.shape)?.building;
                if (building != null) return building;
            }
            return element;
        }

        public override List<int> GetLayerMasks() {
            return new List<int> { PickMask };
        }

        //the elements, by their geometry: not the terrain, nor the editing helpers (the points of the element being
        //edited and their links, which reach over the elements next to them, would be picked instead)
        public static readonly int PickMask = ~(int)((1u << 3) | Rendering.RenderLayers.Groups["helpers"]);

        public override bool SnapsToGrid => false;

        public override bool Raycast(Node3D parent, Vector3 origin, Vector3 direction, out RaycastHit hit, float distance, int mask) {
            return GameEngine.Raycast.DoPickRaycast(parent, origin, direction, out hit, distance, mask);
        }
    }
}
