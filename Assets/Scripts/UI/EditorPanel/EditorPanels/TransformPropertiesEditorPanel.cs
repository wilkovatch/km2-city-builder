using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using SM = StringManager;

namespace EditorPanels {
    public class TransformPropertiesEditorPanel : EditorPanel {
        public GameObject instance = null;
        EditorPanelElements.Label spaceLabel;
        bool notOnHandle;

        public override void Initialize(GameObject canvas) {
            titleActive = true;
            var width = 1.8f;
            var w = width;
            Initialize(canvas, 1, w);
            var p0 = GetPage(0);
            if (notOnHandle) {
                p0.AddButton(SM.Get("END_EDITING"), Close, w * 0.5f);
                p0.AddButton(SM.Get("DELETE"), Delete, w * 0.5f);
                p0.IncreaseRow();
                p0.AddButton(SM.Get("TRANSF_P_SCALE"), SetScale, w / 3.0f);
                p0.AddButton(SM.Get("TRANSF_P_ROTATE"), SetRotate, w / 3.0f);
                p0.AddButton(SM.Get("TRANSF_P_MOVE"), SetMove, w / 3.0f);
            } else {
                p0.AddButton(SM.Get("END_EDITING"), Close, w);
            }
            p0.IncreaseRow();
            p0.AddFieldCheckbox(SM.Get("TRANSF_P_SNAPTOGRID"), GetGizmo, "snapToGrid", null, w, SM.Get("TRANSF_P_SNAPTOGRID_TOOLTIP"));
            p0.IncreaseRow();
            spaceLabel = p0.AddLabel(SM.Get("TRANSF_P_SPACE_LABEL_GLOBAL"), w * 0.5f);
            p0.AddButton(SM.Get("TRANSF_P_SWITCH_SPACE"), SwitchSpace, w * 0.5f, SM.Get("TRANSF_P_SWITCH_SPACE_TOOLTIP"));
            p0.IncreaseRow();
            p0.AddFieldVectorInputField(SM.Get("TRANSF_P_POS"), GetCurInstance, "transform.position", null, w, null, null, value => { Reselect(); });
            p0.IncreaseRow();
            if (notOnHandle) {
                p0.AddFieldRotationVectorInputField(SM.Get("TRANSF_P_ROT"), GetCurInstance, "transform.rotation", null, w, null, null, value => { Reselect(); });
                p0.IncreaseRow();
                p0.AddFieldVectorInputField(SM.Get("TRANSF_P_SCALE_VAL"), GetCurInstance, "transform.localScale", null, w, null, null, value => { Reselect(); });
                p0.IncreaseRow();
            }

            //mesh instance properties
            if (notOnHandle) {
                var settings = CityElements.Types.Parsers.TypeParser.GetMeshInstanceSettings(needsReloading);
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
                            switch (param.type) {
                                case "bool":
                                    p0.AddFieldCheckbox(SM.Get(param.label), GetCurState, pFullName, null, uiInfo.width, SM.Get(param.tooltip));
                                    break;
                                case "float":
                                    p0.AddFieldInputField(SM.Get(param.label), SM.Get(param.placeholder), UnityEngine.UI.InputField.ContentType.DecimalNumber, GetCurState, pFullName, null, uiInfo.width, SM.Get(param.tooltip));
                                    break;
                                case "texture":
                                    p0.AddFieldTextureField(builder, SM.Get(param.label), SM.Get(param.placeholder), GetCurState, pFullName, null, uiInfo.width, SM.Get(param.tooltip));
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

        public void Delete() {
            if (instance == null) return;
            var mI = instance.GetComponent<MeshInstance>();
            if (mI != null) {
                mI.Delete();
                builder.NotifyChange();
                Close();
            }
        }

        void Close() {
            SetActive(false);
        }

        void UpdateSpaceLabel() {
            var isGlobal = builder.gizmo.space == RuntimeGizmos.TransformSpace.Global;
            spaceLabel.SetValue(SM.Get("TRANSF_P_SPACE_LABEL_" + (isGlobal ? "GLOBAL" : "LOCAL")));
        }

        void SwitchSpace() {
            var isGlobal = builder.gizmo.space == RuntimeGizmos.TransformSpace.Global;
            builder.gizmo.space = isGlobal ? RuntimeGizmos.TransformSpace.Local : RuntimeGizmos.TransformSpace.Global;
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
        }

        public void Select(bool active) {
            if (active) {
                builder.UnsetModifier(false);
                builder.gizmo.ClearTargets();
                builder.gizmo.AddTarget(instance.transform);
                if (notOnHandle) {
                    builder.gizmo.SetMoveType = KeyCode.W;
                    builder.gizmo.SetRotateType = KeyCode.E;
                    builder.gizmo.SetScaleType = KeyCode.R;
                    builder.gizmo.SetSpaceToggle = KeyCode.S;
                } else {
                    builder.gizmo.SetRotateType = KeyCode.None;
                    builder.gizmo.SetMoveType = KeyCode.None;
                    builder.gizmo.SetScaleType = KeyCode.None;
                    builder.gizmo.SetSpaceToggle = KeyCode.None;
                    builder.gizmo.transformType = RuntimeGizmos.TransformType.Move;
                }
                builder.gizmo.allowDeselect = false;
            } else {
                if (ActiveSelf()) builder.UnsetModifier();
                builder.gizmo.transformType = RuntimeGizmos.TransformType.Move;
                builder.gizmo.SetRotateType = KeyCode.None;
                builder.gizmo.SetMoveType = KeyCode.None;
                builder.gizmo.SetScaleType = KeyCode.None;
                builder.gizmo.SetSpaceToggle = KeyCode.None;
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
                Initialize(lastCanvas);
                if (instance != null) {
                    if (instance.GetComponent<Handle>() == null) {
                        SetTitle(instance.gameObject.name);
                    } else {
                        SetTitle("Handle", false);
                    }
                    var mI = instance.GetComponent<MeshInstance>();
                    if (mI != null) {
                        mI.SetMoveable(active);
                    }
                    ReadCurValues();
                }
                Select(true);
            } else {
                Select(false);
            }
            base.SetActive(active);
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
