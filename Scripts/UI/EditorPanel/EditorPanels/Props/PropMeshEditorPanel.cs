using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using GameObject = Godot.Control;
using SM = StringManager;
using TP = CityElements.Types.Parsers.TypeParser;
using MeshInstanceType = CityElements.Types.Runtime.MeshInstanceType;

namespace EditorPanels.Props {
    //The placement of the mesh chosen in the mesh list, as an instance of a mesh instance type (a custom element type with
    //a meshInstance setting). Its panel is the ui.json of the type, with these built-in groups:
    //mainGroup (back, close, the type: a dropdown when opened for a group of the Create menu), placementGroup (the
    //placement options the type allows: mass placement, random rotation), transformGroup (only in the edit panel)
    public class PropMeshEditorPanel : EditorPanel {
        ElementPlacer.MeshPlacer placer = null;
        public string selectedMesh = null;
        EditorPanelElements.Slider intSlider, rdSlider;
        EditorPanelElements.Checkbox delCheckbox, multipleCheckbox, rotationCheckbox;
        //the parameters of the next instances, one draft for each type
        readonly Dictionary<string, ObjectState> drafts = new Dictionary<string, ObjectState>();
        public string placementType; //the type to place (null: the ones of placementGroup)
        public string placementGroup;
        string builtType;
        List<string> groupTypes = new List<string>();
        protected override bool SingleStateParameters => true;

        //the creatable types listed under the group in the Create menu
        public static List<string> GroupTypes(string group) {
            var res = new List<string>();
            foreach (var pair in TP.GetMeshInstanceTypes()) {
                var settings = pair.Value.typeData.settings;
                if (settings.creatable && settings.createMenu?.direct != true && (settings.createMenu?.group ?? "CREATE_CUSTOM_ELEMENT") == group) res.Add(pair.Key);
            }
            return res;
        }

        string CurrentType() {
            if (placementType != null) return placementType;
            groupTypes = GroupTypes(placementGroup);
            var last = PreferencesManager.Get("lastMeshInstanceType", "");
            return groupTypes.Contains(last) ? last : groupTypes.Count > 0 ? groupTypes[0] : null;
        }

        ObjectState Draft() {
            if (builtType == null) return new ObjectState();
            if (!drafts.TryGetValue(builtType, out var draft)) {
                draft = Operations.Meshes.NewSettings(builtType);
                drafts[builtType] = draft;
            }
            return draft;
        }

        public override void Initialize(GameObject canvas) {
            intSlider = rdSlider = null;
            delCheckbox = multipleCheckbox = rotationCheckbox = null;
            builtType = PreferencesManager.workingDirectory == "" ? null : CurrentType();
            if (builtType == null) {
                Initialize(canvas, 1);
                if (PreferencesManager.workingDirectory == "") return;
                var p = GetPage(0);
                p.AddLabel(SM.Get("MI_NO_TYPES"), 1.5f);
                p.IncreaseRow();
                p.AddButton(SM.Get("BACK"), GoUp, 0.75f);
                p.AddButton(SM.Get("CLOSE"), Terminate, 0.75f);
                return;
            }
            InitializeWithCustomParameters<MeshInstanceType, CityElements.Types.CustomElementType>(canvas, Draft, null, builtType,
                TP.GetMeshInstanceTypes, ProcessCustomParts, false, 1.5f, false);
        }

        bool ProcessCustomParts(CityElements.Types.TabElement elem, EditorPanelPage p, PresetSelector pS,
            TypeSelector<MeshInstanceType> tS, MeshInstanceType type) {

            var w = elem.width;
            if (elem.name == "mainGroup") {
                p.EnsureNewRow();
                p.AddButton(SM.Get("BACK"), GoUp, w * 0.5f);
                p.AddButton(SM.Get("CLOSE"), Terminate, w * 0.5f);
                p.IncreaseRow();
                if (placementType == null && groupTypes.Count > 1) {
                    var dropdown = p.AddDropdown(SM.Get("MI_TYPE"), groupTypes.ConvertAll(t => SM.GetOrKey(TP.GetMeshInstanceTypes()[t].Label())), SetType, w);
                    dropdown.actionEnabled = false;
                    dropdown.SetValue(groupTypes.IndexOf(type.name));
                    dropdown.actionEnabled = true;
                } else {
                    p.AddLabel(SM.Get("MI_TYPE") + ": " + SM.GetOrKey(type.Label()), w);
                }
                p.IncreaseRow();
                return true;
            }
            if (elem.name == "placementGroup") {
                p.EnsureNewRow();
                var settings = type.Settings;
                if (settings.massPlacement) {
                    multipleCheckbox = p.AddCheckbox(SM.Get("PROP_PLACER_MULTIPLE"), false, SetMultiple, w);
                    p.IncreaseRow();
                    intSlider = p.AddSlider(SM.Get("PROP_PLACER_INTENSITY"), 1, 10, SetTerrainIntensity, w);
                    p.IncreaseRow();
                    rdSlider = p.AddSlider(SM.Get("PROP_PLACER_RADIUS"), 1, 30, SetTerrainRadius, w);
                    p.IncreaseRow();
                    delCheckbox = p.AddCheckbox(SM.Get("PROP_PLACER_DELETE"), false, SetDeleteMode, w);
                    p.IncreaseRow();
                    intSlider.SetInteractable(false);
                    rdSlider.SetInteractable(false);
                    delCheckbox.SetInteractable(false);
                }
                if (settings.randomRotation) {
                    rotationCheckbox = p.AddCheckbox(SM.Get("PROP_PLACER_RAND_ROT"), false, SetRandomRotation, w);
                    p.IncreaseRow();
                }
                return true;
            }
            return elem.name == "transformGroup";
        }

        void SetType(int i) {
            if (i < 0 || i >= groupTypes.Count) return;
            PreferencesManager.Set("lastMeshInstanceType", groupTypes[i]);
            builder.DoDelayed(() => { if (ActiveSelf()) SetActive(true); });
        }

        void SetDeleteMode(bool enabled) {
            placer.deleteMode = enabled;
        }

        void SetRandomRotation(bool enabled) {
            placer.randomRotation = enabled;
        }

        void SetMultiple(bool multiple) {
            intSlider.SetInteractable(multiple);
            rdSlider.SetInteractable(multiple);
            delCheckbox.SetInteractable(multiple);
            placer.multiple = multiple;
        }

        void SetTerrainIntensity(float val) {
            placer.multipleIntensity = val;
        }

        void SetTerrainRadius(float val) {
            placer.multipleRadius = val;
        }

        public override void SetActive(bool active) {
            if (!PrepareActivation(active)) return;
            if (active) {
                Initialize(lastCanvas); //the type may have changed
                placer = builder.meshPlacer;
                placer.settings = builtType != null ? Draft() : null;
                placer.placeEnabled = true;
                placer.multiple = false;
                placer.deleteMode = false;
                placer.randomRotation = false;
                builder.terrainClick.modifier = placer;
            } else {
                builder.SetSelectObjectMode();
            }
            base.SetActive(active);
        }
    }
}
