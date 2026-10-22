using System.Collections;
using System.Collections.Generic;
using Godot;
using Input = GameEngine.Input;
using KeyCommand = GameEngine.Input.KeyCommand;
using RaycastHit = GameEngine.Raycast.RaycastHit;

namespace ElementPlacer {
    //Adds the elements of an array of single parametric objects (e.g. the points of a train line) where the terrain or
    //a road is clicked, in the click placement mode of the array in the editor panel
    public class ArrayPointPlacer : TerrainAction {
        CityBuilderMenuBar builder;
        System.Func<IObjectWithState> getOwner;
        System.Func<object> getUndoTarget;
        CityElements.Types.Parameter param;
        object panel;
        int layerMask = -1;
        Vector3? curPoint = null;

        public ArrayPointPlacer(CityBuilderMenuBar builder) {
            this.builder = builder;
        }

        public bool IsPlacing(object panel, CityElements.Types.Parameter param) {
            return this.panel == panel && this.param == param;
        }

        public bool IsPlacingFrom(object panel) {
            return this.panel != null && this.panel == panel;
        }

        //getUndoTarget: the city element recorded for undo before each placement (the one edited by the panel)
        public void Start(object panel, System.Func<IObjectWithState> getOwner, CityElements.Types.Parameter param, System.Func<object> getUndoTarget) {
            this.panel = panel;
            this.getOwner = getOwner;
            this.getUndoTarget = getUndoTarget;
            this.param = param;
            layerMask = GetLayerMask(Operations.States.GetSingleObjectInstanceSubparam(param));
            builder.terrainClick.modifier = this;
        }

        public void Stop() {
            var wasActive = panel != null;
            panel = null;
            getOwner = null;
            getUndoTarget = null;
            param = null;
            curPoint = null;
            if (wasActive && builder.terrainClick.modifier == this) builder.SetSelectObjectMode();
        }

        //the layers the parametric objects of the type can be placed on (the placement rules of the type)
        static int GetLayerMask(CityElements.Types.Parameter objParam) {
            var type = objParam?.objectInstanceSettings?.type;
            var types = CityElements.Types.Parsers.TypeParser.GetParametricMeshInstanceSettings();
            if (type == null || !types.ContainsKey(type)) return -1;
            var meshSettings = types[type];
            if (meshSettings.rules?.placerLayerMask == null) return -1;
            meshSettings.FillInitialVariables(meshSettings.variableContainer, new ObjectState());
            return (int)meshSettings.rules.placerLayerMask.GetValue(meshSettings.variableContainer);
        }

        public override void Apply(List<RaycastHit?> hits) {
            if (param == null) return;
            if (Input.GetInputStart(KeyCommand.Close)) {
                Stop();
                return;
            }
            ShowOnMap(hits);
            if (Input.GetInputStart(KeyCommand.Select) && curPoint.HasValue) PlaceAt(curPoint.Value);
        }

        //adds an element with its object at the position (what a click does), returns whether it was added
        public bool PlaceAt(Vector3 position) {
            var owner = getOwner?.Invoke();
            if (param == null || owner == null || Operations.States.IsArrayFull(owner.GetState(), param)) return false;
            History.UndoManager.Touch(getUndoTarget?.Invoke());
            if (Operations.States.AddArrayObjectAt(owner.GetState(), param, position) < 0) return false;
            builder.NotifyChange();
            return true;
        }

        public override void ShowOnMap(List<RaycastHit?> hits) {
            if (param != null && hits.Count > 0 && hits[0].HasValue) {
                curPoint = hits[0].Value.point;
                ActionHandlerManager.ShowHandle(curPoint.Value, 1.0f, GameEngine.Color.green);
            } else {
                curPoint = null;
            }
        }

        public override List<int> GetLayerMasks() {
            return new List<int> { layerMask };
        }
    }
}
