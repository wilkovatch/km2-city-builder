using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using SM = StringManager;

namespace EditorPanels {
    public class TransformPropertiesEditorPanel : EditorPanel {
        public GameObject instance = null;
        EditorPanelElements.Label spaceLabel = null;
        bool notOnHandle;
        bool onParametricObject;

        public override void Initialize(GameObject canvas) {
            if (PreferencesManager.workingDirectory == "") {
                Initialize(canvas, 1, 1.8f);
                return;
            }
            titleActive = true;
            var width = 1.8f;
            var w = width;
            Initialize(canvas, 1, w);
            var p0 = GetPage(0);
            CityElements.Types.ObjectInstanceSettings objSettings = null;
            var instComp = instance != null ? instance.GetComponent<MeshInstance>() : null;
            if (instComp == null) onParametricObject = false;
            if (notOnHandle) {
                if (onParametricObject) {
                    objSettings = GetParametricObjectSettings();
                    var isArrayObject = instance?.transform?.parent?.parent?.gameObject?.GetComponent<ArrayObject>() != null;
                    if (isArrayObject) {
                        p0.AddButton(SM.Get("END_EDITING"), Terminate, w * 1f / 3f);
                        p0.AddButton(SM.Get("DELETE"), Delete, w * 1f / 3f);
                        p0.AddButton(SM.Get("BACK"), BackToParent, w * 1f / 3f);
                    } else {
                        p0.AddButton(SM.Get("END_EDITING"), Terminate, w * 0.5f);
                        p0.AddButton(SM.Get("BACK"), BackToParent, w * 0.5f);
                    }
                } else {
                    p0.AddButton(SM.Get("END_EDITING"), Terminate, w * 0.5f);
                    p0.AddButton(SM.Get("DELETE"), Delete, w * 0.5f);
                }
                p0.IncreaseRow();
                var meshField = p0.AddMeshField(builder, SM.Get("TRANSF_MESH"), null, instComp?.meshPath, ChangeMesh, false, null, w);
                p0.IncreaseRow();
                var scaleBtn = p0.AddButton(SM.Get("TRANSF_P_SCALE"), SetScale, w / 3.0f);
                var rotBtn = p0.AddButton(SM.Get("TRANSF_P_ROTATE"), SetRotate, w / 3.0f);
                var moveBtn = p0.AddButton(SM.Get("TRANSF_P_MOVE"), SetMove, w / 3.0f);
                if (onParametricObject) {
                    meshField.SetInteractable(objSettings.allowCustomModel);
                    scaleBtn.SetInteractable(objSettings.allowScale[0] || objSettings.allowScale[1] || objSettings.allowScale[2]);
                    rotBtn.SetInteractable(objSettings.allowRotation[0] || objSettings.allowRotation[1] || objSettings.allowRotation[2]);
                    moveBtn.SetInteractable(objSettings.allowPosition[0] || objSettings.allowPosition[1] || objSettings.allowPosition[2]);
                }
            } else {
                p0.AddButton(SM.Get("END_EDITING"), Terminate, w);
            }
            p0.IncreaseRow();
            p0.AddFieldCheckbox(SM.Get("TRANSF_P_SNAPTOGRID"), GetGizmo, "snapToGrid", null, w * 0.5f, SM.Get("TRANSF_P_SNAPTOGRID_TOOLTIP"));
            p0.AddButton(SM.Get("PROJECT_TO_GROUND"), ProjectToGround, w * 0.5f);
            p0.IncreaseRow();
            if (onParametricObject) {
                p0.AddLabel(SM.Get("TRANSF_P_SPACE_LABEL_PARENT"), w);
                spaceLabel = null;
                SwitchSpace(); //defaults to parent in this case
            } else {
                spaceLabel = p0.AddLabel(SM.Get("TRANSF_P_SPACE_LABEL_GLOBAL"), w * 0.5f);
                p0.AddButton(SM.Get("TRANSF_P_SWITCH_SPACE"), SwitchSpace, w * 0.5f, SM.Get("TRANSF_P_SWITCH_SPACE_TOOLTIP" + (onParametricObject ? "_PARAMETRIC" : "")));
            }
            p0.IncreaseRow();
            var posFieldName = onParametricObject ? "localPosition" : "position";
            var posField = p0.AddFieldVectorInputField(SM.Get("TRANSF_P_POS"), GetCurInstance, "transform." + posFieldName, null, w, null, null, value => { Reselect(); });
            p0.IncreaseRow();
            if (notOnHandle) {
                var rotFieldName = onParametricObject ? "localRotation" : "rotation";
                var rotField = p0.AddFieldRotationVectorInputField(SM.Get("TRANSF_P_ROT"), GetCurInstance, "transform." + rotFieldName, null, w, null, null, value => { Reselect(); });
                p0.IncreaseRow();
                var scaleField = p0.AddFieldVectorInputField(SM.Get("TRANSF_P_SCALE_VAL"), GetCurInstance, "transform.localScale", null, w, null, null, value => { Reselect(); });
                p0.IncreaseRow();
                if (onParametricObject) {
                    scaleField.SetInteractable(objSettings.allowScale[0], objSettings.allowScale[1], objSettings.allowScale[2]);
                    rotField.SetInteractable(objSettings.allowRotation[0], objSettings.allowRotation[1], objSettings.allowRotation[2]);
                    posField.SetInteractable(objSettings.allowPosition[0], objSettings.allowPosition[1], objSettings.allowPosition[2]);
                }
            }

            //mesh instance properties
            if (notOnHandle) {
                CityElements.Types.Runtime.MeshInstanceSettings settings = null;
                if (onParametricObject) {
                    objSettings = instance.GetComponent<MeshInstance>().objectParameter.objectInstanceSettings;
                    settings = CityElements.Types.Parsers.TypeParser.GetParametricMeshInstanceSettings(needsReloading)[objSettings.type];
                } else {
                    settings = CityElements.Types.Parsers.TypeParser.GetMeshInstanceSettings(needsReloading);
                }
                if (settings == null) return;
                var curW = 0.0f;
                for (int i = 0; i < settings.typeData.uiInfo.Length; i++) {
                    var uiInfo = settings.typeData.uiInfo[i];
                    curW += uiInfo.width;
                    if (curW > width) {
                        p0.IncreaseRow();
                        curW = uiInfo.width;
                    }
                    foreach (var param in settings.typeData.parametersInfo.parameters) {
                        if (param.name == uiInfo.name) {
                            var pFullName = "properties." + param.fullName();
                            switch (param.type) { //todo: remove duplicate code
                                case "bool":
                                    p0.AddFieldCheckbox(SM.Get(param.label), GetCurState, pFullName, null, uiInfo.width, SM.Get(param.tooltip));
                                    break;
                                case "float":
                                    p0.AddFieldInputField(SM.Get(param.label), SM.Get(param.placeholder), UnityEngine.UI.InputField.ContentType.DecimalNumber, GetCurState, pFullName, null, uiInfo.width, SM.Get(param.tooltip));
                                    break;
                                case "texture":
                                    p0.AddFieldTextureField(builder, SM.Get(param.label), SM.Get(param.placeholder), GetCurState, pFullName, null, uiInfo.width, SM.Get(param.tooltip));
                                    break;
                                case "mesh":
                                    p0.AddFieldMeshField(builder, SM.Get(param.label), SM.Get(param.placeholder), GetCurState, pFullName, null, true, uiInfo.width, SM.Get(param.tooltip));
                                    break;
                                case "string":
                                    p0.AddFieldInputField(SM.Get(param.label), SM.Get(param.placeholder), UnityEngine.UI.InputField.ContentType.Standard, GetCurState, pFullName, null, uiInfo.width, SM.Get(param.tooltip));
                                    break;
                                case "int":
                                    p0.AddFieldInputField(SM.Get(param.label), SM.Get(param.placeholder), UnityEngine.UI.InputField.ContentType.IntegerNumber, GetCurState, pFullName, null, uiInfo.width, SM.Get(param.tooltip));
                                    break;
                                case "enum":
                                    var typesNames = new List<string>();
                                    foreach (var t in param.enumLabels) typesNames.Add(SM.Get(t));
                                    p0.AddFieldDropdown(SM.Get(param.label), typesNames, GetCurState, pFullName, null, uiInfo.width, SM.Get(param.tooltip));
                                    break;
                            }
                            break;
                        }
                    }
                    if (i == settings.typeData.uiInfo.Length - 1) p0.IncreaseRow();
                }
            }
        }

        ObjectState GetCurState() {
            if (instance == null) return new ObjectState();
            var mI = instance.GetComponent<MeshInstance>();
            if (mI != null) return mI.settings;
            else return new ObjectState();
        }

        RuntimeGizmos.TransformGizmo GetGizmo() {
            return builder.gizmo;
        }

        void ProjectToGround() {
            instance.transform.position = GeometryHelper.ProjectPoint(instance.transform.position);
            ReadCurValues();
            if (onParametricObject) builder.NotifyChange();
        }

        public void Delete() {
            if (instance == null) return;
            var mI = instance.GetComponent<MeshInstance>();
            if (mI != null) {
                if (onParametricObject) { //todo: allow and sync
                    //remove it from the array and sync
                    var arrObj = instance.transform.parent.parent.gameObject.GetComponent<ArrayObject>();
                    if (arrObj != null) {
                        var p = arrObj.state.GetParent();
                        ArrayObject.PruneSelections(p);
                        var arr = p.Array<ObjectState>(arrObj.paramInParent);
                        var list = new List<ObjectState>(arr);
                        list.Remove(arrObj.state);
                        var newArr = list.ToArray();
                        p.SetArray(arrObj.paramInParent, newArr);
                    }
                    builder.NotifyVisibilityChange();
                    builder.NotifyChange();
                    parentPanel = null;
                    BackToParent();
                } else {
                    mI.Delete();
                    builder.NotifyChange();
                    Terminate();
                }
            }
        }

        public override void Terminate() {
            parentPanel = null;
            base.Terminate();
        }

        void BackToParent() {
            if (parentPanel != null) {
                GoUp();
                parentPanel = null;
            } else {
                builder.SelectObject(instance.transform.parent.gameObject, true, null);
            }
        }

        public void UpdateSpaceLabel() {
            if (spaceLabel == null) return;
            var isGlobal = builder.gizmo.space == RuntimeGizmos.TransformSpace.Global;
            spaceLabel.SetValue(SM.Get("TRANSF_P_SPACE_LABEL_" + (isGlobal ? "GLOBAL" : "LOCAL")));
        }

        void SwitchSpace() {
            var isGlobal = builder.gizmo.space == RuntimeGizmos.TransformSpace.Global;
            builder.gizmo.space = onParametricObject ? RuntimeGizmos.TransformSpace.Parent : isGlobal ? RuntimeGizmos.TransformSpace.Local : RuntimeGizmos.TransformSpace.Global;
            UpdateSpaceLabel();
        }

        void SetMove() {
            builder.gizmo.transformType = RuntimeGizmos.TransformType.Move;
        }

        void SetRotate() {
            builder.gizmo.transformType = RuntimeGizmos.TransformType.Rotate;
        }

        void SetScale() {
            builder.gizmo.transformType = RuntimeGizmos.TransformType.Scale;
        }

        void Reselect() {
            builder.gizmo.allowDeselect = true;
            builder.gizmo.ClearTargets();
            builder.gizmo.AddTarget(instance.transform);
            builder.gizmo.allowDeselect = false;
            ReadCurValues();
            if (onParametricObject) builder.NotifyChange();
        }

        CityElements.Types.ObjectInstanceSettings GetParametricObjectSettings() {
            return instance != null ? instance.GetComponent<MeshInstance>().objectParameter.objectInstanceSettings : null;
        }

        void DisableGizmo() {
            builder.gizmo.SetRotateType = KeyCode.None;
            builder.gizmo.SetMoveType = KeyCode.None;
            builder.gizmo.SetScaleType = KeyCode.None;
            builder.gizmo.SetSpaceToggle = KeyCode.None;
            builder.gizmo.transformPanel = null;
        }

        void EnableGizmo() {
            bool move = true, rotate = true, scale = true, toggleSpace = true;
            if (onParametricObject) {
                var objSettings = GetParametricObjectSettings();
                scale = objSettings.allowScale[0] || objSettings.allowScale[1] || objSettings.allowScale[2];
                rotate = objSettings.allowRotation[0] || objSettings.allowRotation[1] || objSettings.allowRotation[2];
                move = objSettings.allowPosition[0] || objSettings.allowPosition[1] || objSettings.allowPosition[2];
                builder.gizmo.posAxesAllowed = objSettings.allowPosition;
                builder.gizmo.rotAxesAllowed = objSettings.allowRotation;
                builder.gizmo.scaleAxesAllowed = objSettings.allowScale;
            } else {
                var allAxes = new bool[] { true, true, true };
                builder.gizmo.posAxesAllowed = allAxes;
                builder.gizmo.rotAxesAllowed = allAxes;
                builder.gizmo.scaleAxesAllowed = allAxes;
            }
            if (move) builder.gizmo.SetMoveType = KeyCode.W;
            if (rotate) builder.gizmo.SetRotateType = KeyCode.E;
            if (scale) builder.gizmo.SetScaleType = KeyCode.R;
            if (toggleSpace) {
                builder.gizmo.SetSpaceToggle = KeyCode.S;
                builder.gizmo.transformPanel = this;
            }
        }

        void ChangeMesh(string newPath) {
            var instComp = instance?.GetComponent<MeshInstance>();
            if (instComp != null) {
                instance = instComp.ReplaceMesh(newPath, builder.helper.elementManager).gameObject;
                Reselect();
            }
        }

        public void Select(bool active) {
            if (active) {
                builder.gizmo.ClearTargets();
                builder.gizmo.AddTarget(instance.transform);
                if (notOnHandle) {
                    EnableGizmo();
                } else {
                    DisableGizmo();
                    builder.gizmo.transformType = RuntimeGizmos.TransformType.Move;
                }
                builder.gizmo.allowDeselect = false;
            } else {
                builder.gizmo.transformType = RuntimeGizmos.TransformType.Move;
                DisableGizmo();
                builder.gizmo.space = RuntimeGizmos.TransformSpace.Global;
                builder.gizmo.allowDeselect = true;
                builder.gizmo.snapToGrid = true;
                builder.gizmo.ClearTargets();
            }
        }

        public override void SetActive(bool active) {
            if (active) {
                UpdateSpaceLabel();
                notOnHandle = instance.GetComponent<Handle>() == null;
                var inst = instance.GetComponent<MeshInstance>();
                onParametricObject = inst != null && inst.objectParameter != null;
                Initialize(lastCanvas);
                if (instance != null) {
                    if (notOnHandle) {
                        SetTitle(instance.gameObject.name, !onParametricObject);
                    } else {
                        SetTitle("Handle", false);
                    }
                    SetMoveable(active);
                    ReadCurValues();
                }
                Select(true);
            } else {
                SetMoveable(active);
                Select(false);
            }
            base.SetActive(active);
        }

        void SetMoveable(bool active) {
            if (instance != null) {
                var mI = instance.GetComponent<MeshInstance>();
                if (mI != null) {
                    mI.SetMoveable(active);
                }
            }
        }

        protected override void ReplaceTitle(string value) {
            var obj = GetCurInstance();
            if (value == null || value == "") {
                if (obj != null) {
                    value = obj.name;
                } else {
                    value = "";
                }
            } else {
                if (obj != null) obj.name = value;
            }
            base.ReplaceTitle(value);
        }

        public void Refresh() {
            if (ActiveSelf()) ReadCurValues();
        }

        GameObject GetCurInstance() {
            return instance;
        }
    }
}
