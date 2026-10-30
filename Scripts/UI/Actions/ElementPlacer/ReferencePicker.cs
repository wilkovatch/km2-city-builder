using System.Collections;
using System.Collections.Generic;
using Godot;
using Input = GameEngine.Input;
using KeyCommand = GameEngine.Input.KeyCommand;
using RaycastHit = GameEngine.Raycast.RaycastHit;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;

namespace ElementPlacer {
    //Picks the element of a reference parameter with a click on the city, started from its field in the editor panel:
    //the handle under the mouse is green on the elements it accepts, red on the others. It ends with the click on an
    //accepted element, when the panel is closed or with Escape (see CityBuilderMenuBar), and the click mode the panel had
    //before is restored.
    public class ReferencePicker : TerrainAction {
        CityBuilderMenuBar builder;
        object panel;
        object field;
        System.Func<object, bool> accepts;
        System.Action<object> onPick;
        System.Action onEnd;
        TerrainAction previous;
        object hovered = null;

        public ReferencePicker(CityBuilderMenuBar builder) {
            this.builder = builder;
        }

        //(another click mode set meanwhile ends it)
        public bool IsActive() {
            return panel != null && builder.terrainClick.modifier == this;
        }

        public bool IsPicking(object panel, object field) {
            return IsActive() && this.panel == panel && this.field == field;
        }

        public bool IsPickingFrom(object panel) {
            return this.panel != null && this.panel == panel;
        }

        //field: identifies the field started from; onEnd: after the pick or its end (the field shows the value again)
        public void Start(object panel, object field, System.Func<object, bool> accepts, System.Action<object> onPick, System.Action onEnd) {
            Stop();
            this.panel = panel;
            this.field = field;
            this.accepts = accepts;
            this.onPick = onPick;
            this.onEnd = onEnd;
            previous = builder.terrainClick.modifier;
            builder.terrainClick.modifier = this;
        }

        public void Stop() {
            End(null);
        }

        //sets the reference to the element (what a click on it does), returns whether it was accepted
        public bool Pick(object element) {
            if (!IsActive() || element == null || !accepts(element)) return false;
            var pick = onPick;
            End(() => pick?.Invoke(element));
            return true;
        }

        //the click mode is restored before the value is set: a panel can get the element it edits from it (e.g. the
        //custom elements from their placer)
        void End(System.Action action) {
            if (panel == null) return;
            var end = onEnd;
            panel = null;
            field = null;
            accepts = null;
            onPick = null;
            onEnd = null;
            hovered = null;
            if (builder.terrainClick.modifier == this) builder.terrainClick.modifier = previous;
            previous = null;
            action?.Invoke();
            end?.Invoke();
        }

        public override void Apply(List<RaycastHit?> hits) {
            if (!IsActive()) return;
            ShowOnMap(hits);
            if (Input.GetInputStart(KeyCommand.Select) && hovered != null) Pick(hovered);
        }

        public override void ShowOnMap(List<RaycastHit?> hits) {
            hovered = null;
            if (hits.Count == 0 || !hits[0].HasValue) return;
            var element = ElementOf(hits[0].Value.collider);
            var ok = element != null && accepts(element);
            if (ok) hovered = element;
            ActionHandlerManager.ShowHandle(hits[0].Value.point, 1.0f, ok ? GameEngine.Color.green : GameEngine.Color.red);
        }

        public override List<int> GetLayerMasks() {
            return new List<int> { ~(1 << 3) }; //as the selection of the elements (see TerrainModifier.Null)
        }

        //the element of the city a collider belongs to (as CityBuilderMenuBar.SelectObject finds it), null if none
        public static object ElementOf(Node node) {
            for (var obj = node as Node3D; obj != null; obj = obj.GetParentNode3D()) {
                var customElement = GS.Cast<CustomElement>(obj);
                if (customElement != null) return customElement;
                var road = GS.Cast<Road>(obj);
                if (road != null) return road;
                var intersection = GS.FindRecursive<Intersection.IntersectionComponent>(obj);
                if (intersection != null && GO.GetParent(GO.GetParent(intersection)) == obj) return intersection.intersection;
                var patch = GS.Cast<TerrainPatch>(obj);
                if (patch != null) return patch;
                var line = GS.Cast<BuildingLine>(obj);
                if (line != null) return line;
            }
            return null;
        }
    }
}
