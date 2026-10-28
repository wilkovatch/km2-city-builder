using System.Collections;
using System.Collections.Generic;
using System.IO;
using Godot;
using SM = StringManager;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;

namespace EditorPanels {
    public class TransformPropertiesEditorPanel : EditorPanel {
        public Node3D instance = null;
        EditorPanelElements.Label spaceLabel = null;
        bool notOnHandle;
        bool onParametricObject;
        ElementPlacer.TerrainPointAttachmentPicker attachmentPicker;
        EditorPanelElements.Label attachmentStatus;
        EditorPanelElements.Button attachPointButton, detachPointButton, projectPointButton;
        EditorPanelElement pointPositionField;

        public override void Initialize(Control canvas) {
            if (PreferencesManager.workingDirectory == "") {
                Initialize(canvas, 1, 1.8f);
                return;
            }
            titleActive = true;
            var width = 1.8f;
            var w = width;
            Initialize(canvas, 1, w);
            var p0 = GetPage(0);
            attachmentStatus = null;
            detachPointButton = null;
            pointPositionField = null;
            projectPointButton = null;
            CityElements.Types.ObjectInstanceSettings objSettings = null;
            var instComp = instance != null ? GS.Cast<MeshInstance>(instance) : null;
            if (instComp == null) onParametricObject = false;
            if (notOnHandle) {
                if (onParametricObject) {
                    objSettings = GetParametricObjectSettings();
                    if (IsArrayObject()) {
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
                p0.AddButton(SM.Get("END_EDITING"), EndHandleEditing, w);
                if (GetHandleRoad() != null) {
                    //road control points: the sections can be rotated (yaw and roll) in the frame of the curve
                    p0.IncreaseRow();
                    var rotBtn = p0.AddButton(SM.Get("TRANSF_P_ROTATE"), SetRotate, w * 0.5f, SM.Get("TRANSF_P_ROTATE_POINT_TOOLTIP"));
                    p0.AddButton(SM.Get("TRANSF_P_MOVE"), SetMove, w * 0.5f);
                    var axes = GetPointRotationAxes();
                    rotBtn.SetInteractable(axes[1] || axes[2]);
                }
            }
            if (GetProceduralPoint() != null) {
                attachmentPicker ??= new ElementPlacer.TerrainPointAttachmentPicker(builder);
                p0.IncreaseRow();
                attachmentStatus = p0.AddLabel("", w);
                p0.IncreaseRow();
                attachPointButton = p0.AddButton(SM.Get("POINT_ATTACH"), AttachPoint, w * 0.65f, SM.Get("POINT_ATTACH_TOOLTIP"));
                detachPointButton = p0.AddButton(SM.Get("POINT_DETACH"), DetachPoint, w * 0.35f, SM.Get("POINT_DETACH_TOOLTIP"));
            }
            p0.IncreaseRow();
            p0.AddFieldCheckbox(SM.Get("TRANSF_P_SNAPTOGRID"), GetGizmo, "snapToGrid", null, w * 0.5f, SM.Get("TRANSF_P_SNAPTOGRID_TOOLTIP"));
            projectPointButton = p0.AddButton(SM.Get("PROJECT_TO_GROUND"), ProjectToGround, w * 0.5f);
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
            var posField = p0.AddFieldVectorInputField(SM.Get("TRANSF_P_POS"), GetCurTransform, "transform." + posFieldName, null, w, null, null, value => { Reselect(); });
            pointPositionField = posField;
            p0.IncreaseRow();
            if (!notOnHandle && GetHandleRoad() != null) {
                var axes = GetPointRotationAxes();
                var yawField = p0.AddFieldInputField(SM.Get("TRANSF_P_YAW"), SM.Get("TRANSF_P_YAW"), GameEngine.InputContentType.Decimal, GetCurPointRotation,
                    "rotation.yaw", null, w * 0.5f, SM.Get("TRANSF_P_YAW_TOOLTIP"));
                var rollField = p0.AddFieldInputField(SM.Get("TRANSF_P_ROLL"), SM.Get("TRANSF_P_ROLL"), GameEngine.InputContentType.Decimal, GetCurPointRotation,
                    "rotation.roll", null, w * 0.5f, SM.Get("TRANSF_P_ROLL_TOOLTIP"));
                yawField.SetInteractable(axes[1]);
                rollField.SetInteractable(axes[2]);
                p0.IncreaseRow();
            }
            if (notOnHandle) {
                var rotFieldName = onParametricObject ? "localRotation" : "rotation";
                var rotField = p0.AddFieldRotationVectorInputField(SM.Get("TRANSF_P_ROT"), GetCurTransform, "transform." + rotFieldName, null, w, null, null, value => { Reselect(); });
                p0.IncreaseRow();
                var scaleField = p0.AddFieldVectorInputField(SM.Get("TRANSF_P_SCALE_VAL"), GetCurTransform, "transform.localScale", null, w, null, null, value => { Reselect(); });
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
                    objSettings = GS.Cast<MeshInstance>(instance).objectParameter.objectInstanceSettings;
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
                                    p0.AddFieldInputField(SM.Get(param.label), SM.Get(param.placeholder), GameEngine.InputContentType.Decimal, GetCurState, pFullName, null, uiInfo.width, SM.Get(param.tooltip));
                                    break;
                                case "texture":
                                    p0.AddFieldTextureField(builder, SM.Get(param.label), SM.Get(param.placeholder), GetCurState, pFullName, null, uiInfo.width, SM.Get(param.tooltip));
                                    break;
                                case "mesh":
                                    p0.AddFieldMeshField(builder, SM.Get(param.label), SM.Get(param.placeholder), GetCurState, pFullName, null, true, uiInfo.width, SM.Get(param.tooltip));
                                    break;
                                case "string":
                                    p0.AddFieldInputField(SM.Get(param.label), SM.Get(param.placeholder), GameEngine.InputContentType.Standard, GetCurState, pFullName, null, uiInfo.width, SM.Get(param.tooltip));
                                    break;
                                case "int":
                                    p0.AddFieldInputField(SM.Get(param.label), SM.Get(param.placeholder), GameEngine.InputContentType.Integer, GetCurState, pFullName, null, uiInfo.width, SM.Get(param.tooltip));
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

        TerrainPoint GetProceduralPoint() {
            DropDeletedInstance();
            var point = instance == null || notOnHandle ? null : GS.FindRecursive<TerrainPoint>(instance);
            return point != null && !point.IsDeleted() && point.GetLinks().Exists(e => e is TerrainPatch || e is BuildingLine) ? point : null;
        }

        bool FixedPoint() {
            var point = GetProceduralPoint();
            return point != null && !GO.IsNull(point.anchor) && !point.anchor.IsDeleted() && !point.anchor.IsMoveable();
        }

        void RefreshAttachmentControls() {
            var point = GetProceduralPoint();
            if (point == null || attachmentStatus == null) return;
            point.Select(true);
            point.SetMoveable(true);
            var attached = !GO.IsNull(point.anchor) && !point.anchor.IsDeleted();
            var key = attachmentPicker.IsActive ? "POINT_PICKING" : !attached ? "POINT_FREE" : point.anchor is LineAnchor ? "POINT_ON_SEGMENT" : "POINT_ANCHORED";
            attachmentStatus.SetValue(SM.Get(key));
            detachPointButton.SetInteractable(attached && !attachmentPicker.IsActive);
            pointPositionField?.SetInteractable(!FixedPoint() && !attachmentPicker.IsActive);
            projectPointButton?.SetInteractable(!FixedPoint() && !attachmentPicker.IsActive);
            SetAllowedAxes();
        }

        void AttachPoint() {
            var point = GetProceduralPoint();
            if (point == null) return;
            if (CancelAttachment()) return;
            attachmentPicker.Start(point, () => {
                if (!ActiveSelf() || GetProceduralPoint() == null) return;
                point.Select(true);
                point.SetMoveable(true);
                Reselect();
                RefreshAttachmentControls();
            });
            RefreshAttachmentControls();
        }

        void DetachPoint() {
            var point = GetProceduralPoint();
            if (point == null) return;
            Operations.TerrainPoints.Detach(point);
            point.Select(true);
            point.SetMoveable(true);
            builder.NotifyChange();
            Reselect();
            RefreshAttachmentControls();
        }

        public bool CancelAttachment() {
            var active = attachmentPicker?.IsActive ?? false;
            attachmentPicker?.Stop();
            return active;
        }

        public override void Update() {
            RefreshAttachmentControls();
        }

        ObjectState GetCurState() {
            if (instance == null) return new ObjectState();
            var mI = GS.Cast<MeshInstance>(instance);
            if (mI != null) return mI.settings;
            else return new ObjectState();
        }

        RuntimeGizmos.TransformGizmo GetGizmo() {
            return builder.gizmo;
        }

        void ProjectToGround() {
            Operations.Meshes.ProjectToGround(instance);
            ReadCurValues();
            if (onParametricObject) builder.NotifyChange();
        }

        ArrayObject GetArrayObject() {
            return Operations.Meshes.GetArrayObject(instance);
        }

        bool IsArrayObject() {
            return GetArrayObject() != null;
        }

        //same condition under which the delete button is shown: plain mesh instances and array elements only,
        //other parametric objects (e.g. traffic lights) are generated by their parent and can't be deleted manually
        public bool CanDelete() {
            DropDeletedInstance();
            if (instance == null || !notOnHandle) return false;
            return Operations.Meshes.CanDelete(GS.Cast<MeshInstance>(instance));
        }

        public void Delete() {
            if (!CanDelete()) return; //undeletable parametric objects must not be deleted
            var mI = GS.Cast<MeshInstance>(instance);
            if (mI != null) {
                if (onParametricObject) {
                    //removed from its array
                    Operations.Meshes.Delete(mI);
                    builder.NotifyVisibilityChange();
                    builder.NotifyChange();
                    parentPanel = null;
                    BackToParent();
                } else {
                    Operations.Meshes.Delete(mI);
                    builder.NotifyChange();
                    Terminate();
                }
            }
        }

        public override void Terminate() {
            CancelAttachment();
            var handleOwner = OnOwnedHandle() ? parentPanel : null;
            parentPanel = null;
            if (handleOwner != null) {
                //the owner was only hidden, close it properly as well
                builder.RestoreControlPointOwner();
                handleOwner.Terminate();
            }
            base.Terminate();
        }

        bool OnOwnedHandle() {
            return !notOnHandle && parentPanel != null;
        }

        //back to the editor of the element the control point belongs to, keeping the point selected
        void EndHandleEditing() {
            CancelAttachment();
            if (!OnOwnedHandle()) {
                Terminate();
                return;
            }
            var handle = instance;
            builder.RestoreControlPointOwner();
            GoUp();
            parentPanel = null;
            DropDeletedInstance();
            if (instance != null) builder.ReselectControlPoint(handle);
        }

        void BackToParent() {
            if (parentPanel != null) {
                GoUp();
                parentPanel = null;
            } else {
                builder.SelectObject((Node3D)instance.GetParent(), true, null);
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
            if (!notOnHandle) builder.gizmo.space = RuntimeGizmos.TransformSpace.Global;
        }

        void SetRotate() {
            if (!notOnHandle) {
                //a road control point rotates in the frame of the curve (the handle follows it): yaw around Y, roll around Z
                if (GetHandleRoad() == null) return;
                builder.gizmo.rotAxesAllowed = GetPointRotationAxes();
                builder.gizmo.space = RuntimeGizmos.TransformSpace.Local;
            }
            builder.gizmo.transformType = RuntimeGizmos.TransformType.Rotate;
        }

        //the road of the handle being edited, if it is one of its control points
        Road GetHandleRoad() {
            DropDeletedInstance();
            if (instance == null || notOnHandle) return null;
            var road = GS.Cast<Road>(instance.GetParentNode3D());
            return road != null && road.points.Contains(instance as StaticBody3D) ? road : null;
        }

        //the rotation axes of the gizmo for a road control point (X: none, Y: yaw, Z: roll)
        bool[] GetPointRotationAxes() {
            var road = GetHandleRoad();
            if (road == null) return new bool[] { false, false, false };
            return new bool[] { false, road.IsYawAllowed(road.points.IndexOf(instance as StaticBody3D)), road.IsRollAllowed() };
        }

        object GetCurPointRotation() {
            var road = GetHandleRoad();
            return road != null ? new PointRotationBinding(road, instance) : null;
        }

        //accessor for the "rotation.*" field bindings of a road control point
        class PointRotationBinding {
            readonly Road road;
            readonly Node3D point;

            public PointRotationBinding(Road road, Node3D point) {
                this.road = road;
                this.point = point;
            }

            public PointRotationBinding rotation => this;
            int Index => road.points.IndexOf(point as StaticBody3D);

            Vector2 Get() {
                road.ReadRotatedHandles(); //the gizmo may have rotated the handle since the last update
                return road.GetPointRotations()[Index];
            }

            public float yaw { get => Get().X; set => road.SetPointRotation(Index, new Vector2(value, Get().Y)); }
            public float roll { get => Get().Y; set => road.SetPointRotation(Index, new Vector2(Get().X, value)); }
        }

        void SetScale() {
            builder.gizmo.transformType = RuntimeGizmos.TransformType.Scale;
        }

        void Reselect() {
            builder.gizmo.allowDeselect = true;
            builder.gizmo.ClearTargets();
            builder.gizmo.AddTarget(instance);
            SetAllowedAxes();
            builder.gizmo.allowDeselect = false;
            ReadCurValues();
            if (onParametricObject) builder.NotifyChange();
        }

        CityElements.Types.ObjectInstanceSettings GetParametricObjectSettings() {
            return instance != null ? GS.Cast<MeshInstance>(instance).objectParameter.objectInstanceSettings : null;
        }

        void DisableGizmo() {
            builder.gizmo.SetRotateType = Key.None;
            builder.gizmo.SetMoveType = Key.None;
            builder.gizmo.SetScaleType = Key.None;
            builder.gizmo.SetSpaceToggle = Key.None;
            builder.gizmo.transformPanel = null;
        }

        //the axes the object can be moved, rotated and scaled along (the gizmo allows all of them again when its targets
        //are cleared, so this follows each selection of the object)
        void SetAllowedAxes() {
            if (GetProceduralPoint() != null) {
                var move = !FixedPoint() && !(attachmentPicker?.IsActive ?? false);
                builder.gizmo.posAxesAllowed = new bool[] { move, move, move };
                builder.gizmo.rotAxesAllowed = builder.gizmo.scaleAxesAllowed = new bool[] { false, false, false };
                builder.gizmo.transformType = move ? RuntimeGizmos.TransformType.Move : RuntimeGizmos.TransformType.None;
                return;
            }
            if (!notOnHandle || !onParametricObject) return;
            var objSettings = GetParametricObjectSettings();
            builder.gizmo.posAxesAllowed = objSettings.allowPosition;
            builder.gizmo.rotAxesAllowed = objSettings.allowRotation;
            builder.gizmo.scaleAxesAllowed = objSettings.allowScale;
        }

        void EnableGizmo() {
            bool move = true, rotate = true, scale = true, toggleSpace = true;
            if (onParametricObject) {
                var objSettings = GetParametricObjectSettings();
                scale = objSettings.allowScale[0] || objSettings.allowScale[1] || objSettings.allowScale[2];
                rotate = objSettings.allowRotation[0] || objSettings.allowRotation[1] || objSettings.allowRotation[2];
                move = objSettings.allowPosition[0] || objSettings.allowPosition[1] || objSettings.allowPosition[2];
            }
            SetAllowedAxes();
            if (move) builder.gizmo.SetMoveType = Key.W;
            if (rotate) builder.gizmo.SetRotateType = Key.E;
            if (scale) builder.gizmo.SetScaleType = Key.R;
            if (toggleSpace) {
                builder.gizmo.SetSpaceToggle = Key.S;
                builder.gizmo.transformPanel = this;
            }
        }

        void ChangeMesh(string newPath) {
            var instComp = GS.Cast<MeshInstance>(instance);
            if (instComp != null) {
                instance = Operations.Meshes.ReplaceMesh(builder.helper.elementManager, instComp, newPath);
                Reselect();
            }
        }

        //Godot keeps a disposed wrapper around
        void DropDeletedInstance() {
            if (instance != null && !GodotObject.IsInstanceValid(instance)) instance = null;
        }

        public void Select(bool active) {
            DropDeletedInstance();
            if (active) {
                builder.gizmo.ClearTargets();
                builder.gizmo.AddTarget(instance);
                if (notOnHandle) {
                    EnableGizmo();
                } else {
                    DisableGizmo();
                    builder.gizmo.transformType = RuntimeGizmos.TransformType.Move;
                    SetAllowedAxes();
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
            if (!active) CancelAttachment();
            DropDeletedInstance();
            if (active) {
                UpdateSpaceLabel();
                notOnHandle = GS.Cast<Handle>(instance) == null;
                var inst = GS.Cast<MeshInstance>(instance);
                onParametricObject = inst != null && inst.objectParameter != null;
                Initialize(lastCanvas);
                if (instance != null) {
                    if (notOnHandle) {
                        SetTitle(GO.GetName(instance), !onParametricObject);
                    } else {
                        SetTitle("Handle", false);
                    }
                    SetMoveable(active);
                    ReadCurValues();
                }
                Select(true);
                RefreshAttachmentControls();
            } else {
                SetMoveable(active);
                Select(false);
            }
            base.SetActive(active);
        }

        void SetMoveable(bool active) {
            if (instance != null) {
                var mI = GS.Cast<MeshInstance>(instance);
                if (mI != null) {
                    mI.SetMoveable(active);
                }
            }
        }

        protected override void ReplaceTitle(string value) {
            var obj = GetCurInstance();
            if (value == null || value == "") {
                if (obj != null) {
                    value = GO.GetName(obj);
                } else {
                    value = "";
                }
            } else {
                if (obj != null) GO.SetName(obj, value);
            }
            base.ReplaceTitle(value);
        }

        public void Refresh() {
            DropDeletedInstance();
            if (ActiveSelf() && instance != null) {
                ReadCurValues();
                RefreshAttachmentControls();
            }
        }

        Node3D GetCurInstance() {
            return instance;
        }

        public override object GetUndoTarget() {
            DropDeletedInstance();
            return History.UndoManager.ResolveOwner(instance) ?? base.GetUndoTarget();
        }

        object GetCurTransform() {
            return instance != null ? new TransformBinding(instance, GetProceduralPoint()) : null;
        }

        //accessor for the "transform.*" field bindings, Godot nodes have no "transform" member
        class TransformBinding {
            readonly Node3D obj;
            readonly TerrainPoint point;

            public TransformBinding(Node3D obj, TerrainPoint point) {
                this.obj = obj;
                this.point = point;
            }

            public TransformBinding transform => this;
            //the owner was recorded by the panel, but a road end on an intersection moves the intersection too
            public Vector3 position { get => GO.GetPosition(obj); set {
                if (point != null) Operations.TerrainPoints.Move(point, value);
                else { History.UndoManager.TouchOwner(obj); GO.SetPosition(obj, value); }
            } }
            public Vector3 localPosition { get => GO.GetLocalPosition(obj); set { History.UndoManager.TouchOwner(obj); GO.SetLocalPosition(obj, value); } }
            public Quaternion rotation { get => GO.GetRotationQuaternion(obj); set => GO.SetRotation(obj, value); }
            public Quaternion localRotation { get => GameEngine.Quaternion.Encode(obj.Quaternion); set => GO.SetLocalRotation(obj, value); }
            public Vector3 localScale { get => GO.GetLocalScale(obj); set => GO.SetLocalScale(obj, value); }
        }
    }
}
