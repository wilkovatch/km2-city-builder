using System;
using System.Collections.Generic;
using Godot;
using Input = GameEngine.Input;
using KeyCommand = GameEngine.Input.KeyCommand;
using GO = GameEngine.GameObject;

// Godot port of the (customized) HiddenMonk's Unity3DRuntimeTransformGizmo
// (MIT, see res://CustomPackages/RuntimeGizmo/LICENSE.txt).
// Only the features actually used by the city builder are ported: move/rotate/scale in global, local
// and parent space, pivot-mode only, snapping, multi-selection and the selection outline (drawn in
// screen space, see SelectionOutline.cs).
// Everything works in Godot world space; the "forward" (blue) axis is Godot's -Z, i.e. the program's +Z.
namespace RuntimeGizmos {
    public enum TransformSpace { Global, Local, Parent }
    public enum TransformType { Move, Rotate, Scale, None }
    public enum Axis { None, X, Y, Z, Any }

    public partial class TransformGizmo : Node3D {
        public TransformSpace space = TransformSpace.Global;
        public TransformType transformType = TransformType.Move;

        //Key.None disables the hotkey
        public Key SetMoveType = Key.None;
        public Key SetRotateType = Key.None;
        public Key SetScaleType = Key.None;
        public Key SetSpaceToggle = Key.None;

        public Color xColor = new Color(1, 0, 0, 0.8f);
        public Color yColor = new Color(0, 1, 0, 0.8f);
        public Color zColor = new Color(0, 0, 1, 0.8f);
        public Color allColor = new Color(.7f, .7f, .7f, 0.8f);
        public Color selectedColor = new Color(1, 1, 0, 0.8f);
        public Color hoverColor = new Color(1, .75f, 0, 0.8f);
        public float planesOpacity = .5f;

        public float movementSnap = 1f;
        public float rotationSnap = 15f;
        public float scaleSnap = .25f;
        public bool snapToGrid = true;
        //point selection is available from startup; the transform panel locks it while editing one object
        public bool allowDeselect = true;

        public float handleLength = .25f;
        public float handleWidth = .003f;
        public float planeSize = .035f;
        public float triangleSize = .03f;
        public float boxSize = .03f;
        public int circleDetail = 40;
        public float minSelectedDistanceCheck = .01f;
        public float moveSpeedMultiplier = 1f;
        public float scaleSpeedMultiplier = 1f;
        public float rotateSpeedMultiplier = 1f;
        public float allRotateSpeedMultiplier = 20f;

        public uint selectionMask = (1u << 8) | (1u << 10);
        public int selectOnlyLayer = 10;

        public Action onAnyChange;
        //at the end of a transform, with its targets, before its undo step is committed (so changes made by it are part of it)
        public Action<IReadOnlyList<Node3D>> onTransformFinished;

        public EditorPanels.TransformPropertiesEditorPanel transformPanel = null;
        public bool[] posAxesAllowed = new bool[3] { true, true, true };
        public bool[] rotAxesAllowed = new bool[3] { true, true, true };
        public bool[] scaleAxesAllowed = new bool[3] { true, true, true };

        const float SelectionRayDistance = 100000f;
        const float MouseAxisSensitivity = 0.1f; //"Mouse X"/"Mouse Y" sensitivity

        public bool isTransforming { get; private set; }
        public bool isNearAxis => nearAxis != Axis.None;
        public float totalScaleAmount { get; private set; }
        public Quaternion totalRotationAmount { get; private set; } = Quaternion.Identity;
        public Axis translatingAxis { get { return nearAxis; } }
        public Axis translatingAxisPlane { get { return planeAxis; } }
        public bool hasTranslatingAxisPlane { get { return translatingAxisPlane != Axis.None && translatingAxisPlane != Axis.Any; } }
        public TransformType transformingType { get { return translatingType; } }

        public Vector3 pivotPoint { get; private set; }

        public Node3D mainTargetRoot { get { return targetRootsOrdered.Count > 0 ? targetRootsOrdered[0] : null; } }

        Camera3D myCamera;
        AxisInfo axisInfo;
        Axis nearAxis = Axis.None;
        Axis planeAxis = Axis.None;
        TransformType translatingType;

        AxisVectors handleLines = new AxisVectors();
        AxisVectors handlePlanes = new AxisVectors();
        AxisVectors handleTriangles = new AxisVectors();
        AxisVectors handleSquares = new AxisVectors();
        AxisVectors circlesLines = new AxisVectors();

        List<Node3D> targetRootsOrdered = new List<Node3D>();
        bool undoTransactionOpen = false;
        ulong consumedClickFrame = ulong.MaxValue;
        List<GeometryInstance3D> highlightedRenderers = new List<GeometryInstance3D>();

        MeshInstance3D drawInstance;
        ImmediateMesh drawMesh;
        Material lineMaterial;
        SelectionOutline outline;
        List<Vector3> drawVertices = new List<Vector3>();
        List<Color> drawColors = new List<Color>();

        //state of the transformation in progress
        TransformType dragType;
        Vector3 originalPivot, dragAxis, otherAxis1, otherAxis2, planeNormal, projectedAxis, previousMousePosition;
        Vector3 currentSnapMovementAmount;
        float currentSnapRotationAmount, currentSnapScaleAmount;
        Vector2 lastMouseScreenPosition, mouseScreenDelta;

        public override void _Ready() {
            lineMaterial = GameEngine.Resources.LoadMaterial("Materials/GizmoLines");
            drawMesh = new ImmediateMesh();
            drawInstance = new MeshInstance3D {
                Name = "GizmoMesh",
                Mesh = drawMesh,
                TopLevel = true,
                Layers = GameEngine.Camera.OverlayLayer,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            };
            AddChild(drawInstance);
            drawInstance.GlobalTransform = Transform3D.Identity;
            outline = new SelectionOutline { Name = "SelectionOutline" };
            AddChild(outline);
        }

        public override void _ExitTree() {
            ClearAllHighlightedRenderers();
        }

        public override void _Process(double delta) {
            myCamera = GetViewport().GetCamera3D();
            if (myCamera == null) return;
            var mouseScreenPosition = GetViewport().GetMousePosition();
            mouseScreenDelta = mouseScreenPosition - lastMouseScreenPosition;
            lastMouseScreenPosition = mouseScreenPosition;
            PruneTargets();

            //Update
            if (!isTransforming) {
                translatingType = transformType;
                CommitUndoTransaction(); //in case the transformation ended without FinishTransform
            }
            SetNearAxis();
            GetTarget();
            if (mainTargetRoot != null) {
                if (isTransforming) {
                    ContinueTransform();
                } else {
                    TransformSelected();
                }
            }

            //LateUpdate
            outline.UpdateOutline(highlightedRenderers);
            if (mainTargetRoot == null) {
                drawMesh.ClearSurfaces();
                return;
            }
            if (!isTransforming) SetPivotPoint();
            SetAxisInfo();
            SetLines();
            Draw();
        }

        public override void _UnhandledKeyInput(InputEvent @event) {
            if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo) return;
            if (keyEvent.CtrlPressed) return; //Ctrl+key combinations are menu shortcuts
            var key = keyEvent.PhysicalKeycode;
            if (key == Key.None) return;

            if (key == SetMoveType) transformType = TransformType.Move;
            else if (key == SetRotateType) transformType = TransformType.Rotate;
            else if (key == SetScaleType) transformType = TransformType.Scale;

            if (!isTransforming) translatingType = transformType;

            if (key == SetSpaceToggle) {
                if (space == TransformSpace.Global) space = TransformSpace.Local;
                else if (space == TransformSpace.Local) space = TransformSpace.Global;
                if (transformPanel != null) transformPanel.UpdateSpaceLabel();
            }
        }

        Vector3 GetCameraPosition() {
            return myCamera.GlobalPosition;
        }

        (Vector3 origin, Vector3 direction) GetMouseRay() {
            var mousePosition = GetViewport().GetMousePosition();
            return (myCamera.ProjectRayOrigin(mousePosition), myCamera.ProjectRayNormal(mousePosition));
        }

        Color GetColor(TransformType type, Color normalColor, Color nearColor, bool forceUseNormal = false) {
            return GetColor(type, normalColor, nearColor, false, 1, forceUseNormal);
        }
        Color GetColor(TransformType type, Color normalColor, Color nearColor, float alpha, bool forceUseNormal = false) {
            return GetColor(type, normalColor, nearColor, true, alpha, forceUseNormal);
        }
        Color GetColor(TransformType type, Color normalColor, Color nearColor, bool setAlpha, float alpha, bool forceUseNormal = false) {
            Color color;
            if (!forceUseNormal && TranslatingTypeContains(type, false)) {
                color = nearColor;
            } else {
                color = normalColor;
            }

            if (setAlpha) {
                color.A = alpha;
            }

            return color;
        }

        //We only support scaling in local space.
        public TransformSpace GetProperTransformSpace() {
            return transformType == TransformType.Scale ? TransformSpace.Local : space;
        }

        public bool TransformTypeContains(TransformType type) {
            return transformType == type;
        }
        public bool TranslatingTypeContains(TransformType type, bool checkIsTransforming = true) {
            TransformType transType = !checkIsTransforming || isTransforming ? translatingType : transformType;
            return transType == type;
        }

        public float GetHandleLength(TransformType type, Axis axis = Axis.None, bool multiplyDistanceMultiplier = true) {
            float length = handleLength;

            if (multiplyDistanceMultiplier) length *= GetDistanceMultiplier();

            if (type == TransformType.Scale && isTransforming && (translatingAxis == axis || translatingAxis == Axis.Any)) length += totalScaleAmount;

            return length;
        }

        void TransformSelected() {
            if (mainTargetRoot != null) {
                if (nearAxis != Axis.None && Input.GetInputStart(KeyCommand.Select) && GetNearAxisAllowed(GetAxesAllowed(translatingType))) {
                    StartTransform(translatingType);
                }
            }
        }

        bool[] GetAxesAllowed(TransformType transType) {
            switch (transType) {
                case TransformType.Move:
                    return posAxesAllowed;
                case TransformType.Rotate:
                    return rotAxesAllowed;
                case TransformType.Scale:
                    return scaleAxesAllowed;
                default:
                    return new bool[] { true, true, true };
            }
        }

        //the transformation is split into start/continue/finish
        void StartTransform(TransformType transType) {
            //the whole drag is a single undo step, the elements the targets belong to are recorded before it starts
            History.UndoManager.Begin("Transform");
            undoTransactionOpen = true;
            foreach (var target in targetRootsOrdered) History.UndoManager.TouchOwner(target);
            isTransforming = true;
            dragType = transType;
            totalScaleAmount = 0;
            totalRotationAmount = Quaternion.Identity;

            originalPivot = pivotPoint;

            dragAxis = GetNearAxisDirection(out otherAxis1, out otherAxis2);
            planeNormal = hasTranslatingAxisPlane ? dragAxis : (GetCameraPosition() - originalPivot).Normalized();
            projectedAxis = ExtVector3.ProjectOnPlane(dragAxis, planeNormal).Normalized();
            previousMousePosition = Vector3.Zero;

            currentSnapMovementAmount = Vector3.Zero;
            currentSnapRotationAmount = 0;
            currentSnapScaleAmount = 0;

            TransformStep();
        }

        void ContinueTransform() {
            if (!Input.GetInputPressed(KeyCommand.Select)) {
                FinishTransform();
                return;
            }
            TransformStep();
        }

        void FinishTransform() {
            totalRotationAmount = Quaternion.Identity;
            totalScaleAmount = 0;
            isTransforming = false;
            SetTranslatingAxis(transformType, Axis.None);

            if (undoTransactionOpen) onTransformFinished?.Invoke(targetRootsOrdered);
            SetPivotPoint();
            CommitUndoTransaction();
        }

        void CommitUndoTransaction() {
            if (!undoTransactionOpen) return;
            undoTransactionOpen = false;
            History.UndoManager.Commit();
        }

        void TransformStep() {
            var mouseRay = GetMouseRay();
            Vector3 mousePosition = Geometry.LinePlaneIntersect(mouseRay.origin, mouseRay.direction, originalPivot, planeNormal);
            bool isSnapping = Input.GetInputPressed(KeyCommand.Modifier1);
            Vector3 axis = dragAxis;

            if (previousMousePosition != Vector3.Zero && mousePosition != Vector3.Zero) {
                if (dragType == TransformType.Move) {
                    Vector3 movement = Vector3.Zero;

                    if (hasTranslatingAxisPlane) {
                        movement = mousePosition - previousMousePosition;
                    } else {
                        float moveAmount = ExtVector3.MagnitudeInDirection(mousePosition - previousMousePosition, projectedAxis) * moveSpeedMultiplier;
                        movement = axis * moveAmount;
                    }

                    if (isSnapping && movementSnap > 0) {
                        currentSnapMovementAmount += movement;
                        movement = Vector3.Zero;

                        if (hasTranslatingAxisPlane) {
                            float amountInAxis1 = ExtVector3.MagnitudeInDirection(currentSnapMovementAmount, otherAxis1);
                            float amountInAxis2 = ExtVector3.MagnitudeInDirection(currentSnapMovementAmount, otherAxis2);
                            float positionInAxis1 = ExtVector3.MagnitudeInDirection(targetRootsOrdered[0].GlobalPosition, otherAxis1);
                            float positionInAxis2 = ExtVector3.MagnitudeInDirection(targetRootsOrdered[0].GlobalPosition, otherAxis2);

                            float snapAmount1 = CalculateSnapAmount(positionInAxis1, movementSnap, amountInAxis1, out _);
                            float snapAmount2 = CalculateSnapAmount(positionInAxis2, movementSnap, amountInAxis2, out _);

                            if (snapAmount1 != 0) {
                                Vector3 snapMove = (otherAxis1 * snapAmount1);
                                movement += snapMove;
                                currentSnapMovementAmount -= snapMove;
                            }
                            if (snapAmount2 != 0) {
                                Vector3 snapMove = (otherAxis2 * snapAmount2);
                                movement += snapMove;
                                currentSnapMovementAmount -= snapMove;
                            }
                        } else {
                            float positionInAxis = Mathf.Abs(ExtVector3.MagnitudeInDirection(targetRootsOrdered[0].GlobalPosition, axis));
                            float snapAmount = CalculateSnapAmount(positionInAxis, movementSnap, currentSnapMovementAmount.Length(), out float remainder);

                            if (snapAmount != 0) {
                                movement = currentSnapMovementAmount.Normalized() * snapAmount;
                                currentSnapMovementAmount = currentSnapMovementAmount.Normalized() * remainder;
                            }
                        }
                    }

                    foreach (var target in targetRootsOrdered) {
                        target.GlobalPosition += movement;
                    }
                    onAnyChange?.Invoke();

                    SetPivotPointOffset(movement);
                } else if (dragType == TransformType.Scale) {
                    Vector3 projected = (nearAxis == Axis.Any) ? myCamera.GlobalBasis.X.Normalized() : projectedAxis;
                    float scaleAmount = ExtVector3.MagnitudeInDirection(mousePosition - previousMousePosition, projected) * scaleSpeedMultiplier;

                    if (isSnapping && scaleSnap > 0) {
                        currentSnapScaleAmount += scaleAmount;
                        scaleAmount = 0;

                        var trueSnapToGrid = snapToGrid;
                        snapToGrid = false;
                        float snapAmount = CalculateSnapAmount(targetRootsOrdered[0].Scale.Length(), scaleSnap, currentSnapScaleAmount, out float remainder);
                        snapToGrid = trueSnapToGrid;

                        if (snapAmount != 0) {
                            scaleAmount = snapAmount;
                            currentSnapScaleAmount = remainder;
                        }
                    }

                    //scaling is always in local space, so the local axis is the unit vector of the dragged axis
                    Vector3 targetScaleAmount;
                    if (nearAxis == Axis.Any) targetScaleAmount = ExtVector3.Abs(mainTargetRoot.Scale.Normalized()) * scaleAmount;
                    else targetScaleAmount = GetLocalAxis(nearAxis) * scaleAmount;

                    foreach (var target in targetRootsOrdered) {
                        target.Scale += targetScaleAmount;
                    }
                    onAnyChange?.Invoke();

                    totalScaleAmount += scaleAmount;
                } else if (dragType == TransformType.Rotate) {
                    float rotateAmount = 0;
                    Vector3 rotationAxis = axis;

                    if (nearAxis == Axis.Any) {
                        //The handedness flip between Godot and the program coordinates inverts the angle around the same world axis.
                        var mouseAxis = new Vector3(-mouseScreenDelta.Y, -mouseScreenDelta.X, 0) * MouseAxisSensitivity;
                        Vector3 rotation = myCamera.GlobalBasis.Orthonormalized() * mouseAxis;
                        rotateAmount = -rotation.Length() * allRotateSpeedMultiplier;
                        rotationAxis = rotation.Normalized();
                    } else {
                        Vector3 projected = (ExtVector3.IsParallel(axis, planeNormal)) ? planeNormal : axis.Cross(planeNormal);
                        rotateAmount = (ExtVector3.MagnitudeInDirection(mousePosition - previousMousePosition, projected) * (rotateSpeedMultiplier * 100f)) / GetDistanceMultiplier();
                    }

                    if (isSnapping && rotationSnap > 0) {
                        currentSnapRotationAmount += rotateAmount;
                        rotateAmount = 0;

                        var trueSnapToGrid = snapToGrid;
                        snapToGrid = false;
                        float snapAmount = CalculateSnapAmount(0, rotationSnap, currentSnapRotationAmount, out float remainder);
                        snapToGrid = trueSnapToGrid;

                        if (snapAmount != 0) {
                            rotateAmount = snapAmount;
                            currentSnapRotationAmount = remainder;
                        }
                    }

                    if (rotateAmount != 0 && rotationAxis.LengthSquared() > 0) {
                        var normalizedAxis = rotationAxis.Normalized();
                        var angle = Mathf.DegToRad(rotateAmount);
                        foreach (var target in targetRootsOrdered) {
                            target.GlobalRotate(normalizedAxis, angle);
                        }
                        totalRotationAmount = totalRotationAmount * new Quaternion(normalizedAxis, angle);
                    }
                    onAnyChange?.Invoke();
                }
            }

            previousMousePosition = mousePosition;
        }

        static Vector3 GetLocalAxis(Axis axis) {
            switch (axis) {
                case Axis.X: return Vector3.Right;
                case Axis.Y: return Vector3.Up;
                case Axis.Z: return Vector3.Back;
                default: return Vector3.Zero;
            }
        }

        float CalculateSnapAmount(float curValue, float snapValue, float currentAmount, out float remainder) {
            remainder = 0;
            if (snapValue <= 0) return currentAmount;

            float currentAmountAbs = Mathf.Abs(currentAmount);
            if (snapToGrid) {
                float sumValue = curValue + currentAmount;
                return snapValue * (Mathf.Sign(sumValue) * Mathf.Floor(Mathf.Abs(sumValue) / snapValue)) - curValue;
            } else if (currentAmountAbs > snapValue) {
                remainder = currentAmountAbs % snapValue;
                return snapValue * (Mathf.Sign(currentAmount) * Mathf.Floor(currentAmountAbs / snapValue));
            }

            return 0;
        }

        Vector3 GetNearAxisDirection(out Vector3 otherAxis1, out Vector3 otherAxis2) {
            otherAxis1 = otherAxis2 = Vector3.Zero;

            if (nearAxis != Axis.None) {
                if (nearAxis == Axis.X) {
                    otherAxis1 = axisInfo.yDirection;
                    otherAxis2 = axisInfo.zDirection;
                    return axisInfo.xDirection;
                }
                if (nearAxis == Axis.Y) {
                    otherAxis1 = axisInfo.xDirection;
                    otherAxis2 = axisInfo.zDirection;
                    return axisInfo.yDirection;
                }
                if (nearAxis == Axis.Z) {
                    otherAxis1 = axisInfo.xDirection;
                    otherAxis2 = axisInfo.yDirection;
                    return axisInfo.zDirection;
                }
                if (nearAxis == Axis.Any) {
                    return Vector3.One;
                }
            }

            return Vector3.Zero;
        }

        bool GetNearAxisAllowed(bool[] axesAllowed) {
            //a plane (planeAxis is its normal) moves along the two other axes
            switch (planeAxis) {
                case Axis.X: return axesAllowed[1] && axesAllowed[2];
                case Axis.Y: return axesAllowed[0] && axesAllowed[2];
                case Axis.Z: return axesAllowed[0] && axesAllowed[1];
            }
            switch (nearAxis) {
                case Axis.X: return axesAllowed[0];
                case Axis.Y: return axesAllowed[1];
                case Axis.Z: return axesAllowed[2];
                case Axis.Any: return axesAllowed[0] && axesAllowed[1] && axesAllowed[2];
                default: return false;
            }
        }

        //The current click was already handled by a terrain action (which runs before the gizmo), so it must not select or deselect anything
        public void ConsumeClick() {
            consumedClickFrame = Engine.GetProcessFrames();
        }

        void GetTarget() {
            if (!allowDeselect || UIDetection.OnUI()) return;
            if (consumedClickFrame == Engine.GetProcessFrames()) return;
            if (nearAxis == Axis.None && Input.GetInputStart(KeyCommand.Select)) {
                //Ctrl toggles the clicked target in the selection
                bool isToggling = Input.GetInputPressed(KeyCommand.Modifier2);

                var target = RaycastSelection();
                if (target != null) {
                    if (target is StaticBody3D body && GO.GetLayer(body) == selectOnlyLayer) {
                        transformType = TransformType.None;
                    } else {
                        transformType = TransformType.Move;
                    }

                    if (isToggling) {
                        if (targetRootsOrdered.Contains(target)) RemoveTarget(target);
                        else AddTarget(target);
                    } else {
                        ClearAndAddTarget(target);
                    }
                } else {
                    if (!isToggling) {
                        ClearTargets();
                    }
                }
            }
        }

        Node3D RaycastSelection() {
            var mouseRay = GetMouseRay();
            var query = PhysicsRayQueryParameters3D.Create(mouseRay.origin, mouseRay.origin + mouseRay.direction * SelectionRayDistance, selectionMask);
            query.CollideWithAreas = false;
            var result = GetWorld3D().DirectSpaceState.IntersectRay(query);
            if (result.Count == 0) return null;
            return result["collider"].As<Node3D>();
        }

        public void AddTarget(Node3D target) {
            if (target == null || !GodotObject.IsInstanceValid(target)) return;
            if (targetRootsOrdered.Contains(target)) return;

            targetRootsOrdered.Add(target);
            AddTargetHighlightedRenderers(target);

            SetPivotPoint();
        }

        public bool HasTarget(Node3D target) {
            return target != null && targetRootsOrdered.Contains(target);
        }

        public void RemoveTarget(Node3D target) {
            if (target == null) return;
            if (!targetRootsOrdered.Contains(target)) return;

            RemoveTargetHighlightedRenderers(target);
            targetRootsOrdered.Remove(target);

            SetPivotPoint();
        }

        //also allows all the axes again (the ones of an object don't apply to the next targets)
        public void ClearTargets() {
            ClearAllHighlightedRenderers();
            targetRootsOrdered.Clear();
            posAxesAllowed = rotAxesAllowed = scaleAxesAllowed = new bool[3] { true, true, true };
        }

        void ClearAndAddTarget(Node3D target) {
            ClearTargets();
            AddTarget(target);
        }

        //Drops targets that were deleted
        void PruneTargets() {
            for (int i = targetRootsOrdered.Count - 1; i >= 0; i--) {
                var target = targetRootsOrdered[i];
                if (!GodotObject.IsInstanceValid(target) || !target.IsInsideTree() || target.IsQueuedForDeletion()) {
                    targetRootsOrdered.RemoveAt(i);
                    if (i == 0 && isTransforming) FinishTransform();
                }
            }
            highlightedRenderers.RemoveAll(r => !GodotObject.IsInstanceValid(r));
        }

        void AddTargetHighlightedRenderers(Node3D target) {
            foreach (var render in GetTargetRenderers(target)) {
                if (!highlightedRenderers.Contains(render)) {
                    render.Layers |= SelectionOutline.MaskLayer;
                    highlightedRenderers.Add(render);
                }
            }
        }

        void RemoveTargetHighlightedRenderers(Node3D target) {
            RemoveHighlightedRenderers(GetTargetRenderers(target));
        }

        void ClearAllHighlightedRenderers() {
            RemoveHighlightedRenderers(new List<GeometryInstance3D>(highlightedRenderers));
        }

        void RemoveHighlightedRenderers(List<GeometryInstance3D> renderers) {
            foreach (var render in renderers) {
                SelectionOutline.RemoveFromMask(render);
                highlightedRenderers.Remove(render);
            }
        }

        static List<GeometryInstance3D> GetTargetRenderers(Node target) {
            return SelectionOutline.GetRenderers(target);
        }

        public void SetPivotPoint() {
            if (mainTargetRoot != null) {
                pivotPoint = mainTargetRoot.GlobalPosition;
            }
        }

        void SetPivotPointOffset(Vector3 offset) {
            pivotPoint += offset;
        }

        public void SetTranslatingAxis(TransformType type, Axis axis, Axis planeAxis = Axis.None) {
            this.translatingType = type;
            this.nearAxis = axis;
            this.planeAxis = planeAxis;
        }

        public AxisInfo GetAxisInfo() {
            AxisInfo currentAxisInfo = axisInfo;

            if (isTransforming && GetProperTransformSpace() == TransformSpace.Global && translatingType == TransformType.Rotate) {
                currentAxisInfo.xDirection = totalRotationAmount * Vector3.Right;
                currentAxisInfo.yDirection = totalRotationAmount * Vector3.Up;
                currentAxisInfo.zDirection = totalRotationAmount * Vector3.Forward;
            }

            return currentAxisInfo;
        }

        void SetNearAxis() {
            if (isTransforming) return;

            SetTranslatingAxis(transformType, Axis.None);

            if (mainTargetRoot == null) return;

            float distanceMultiplier = GetDistanceMultiplier();
            float handleMinSelectedDistanceCheck = (this.minSelectedDistanceCheck + handleWidth) * distanceMultiplier;

            if (nearAxis == Axis.None && (TransformTypeContains(TransformType.Move) || TransformTypeContains(TransformType.Scale))) {
                //Important to check scale lines before move lines since in TransformType.All the move planes would block the scales center scale all gizmo.
                if (nearAxis == Axis.None && TransformTypeContains(TransformType.Scale)) {
                    float tipMinSelectedDistanceCheck = (this.minSelectedDistanceCheck + boxSize) * distanceMultiplier;
                    HandleNearestPlanes(TransformType.Scale, handleSquares, tipMinSelectedDistanceCheck);
                }

                if (nearAxis == Axis.None && TransformTypeContains(TransformType.Move)) {
                    //Important to check the planes first before the handle tip since it makes selecting the planes easier.
                    float planeMinSelectedDistanceCheck = (this.minSelectedDistanceCheck + planeSize) * distanceMultiplier;
                    HandleNearestPlanes(TransformType.Move, handlePlanes, planeMinSelectedDistanceCheck);

                    if (nearAxis != Axis.None) {
                        planeAxis = nearAxis;
                    } else {
                        float tipMinSelectedDistanceCheck = (this.minSelectedDistanceCheck + triangleSize) * distanceMultiplier;
                        HandleNearestLines(TransformType.Move, handleTriangles, tipMinSelectedDistanceCheck);
                    }
                }

                if (nearAxis == Axis.None) {
                    HandleNearestLines(transformType, handleLines, handleMinSelectedDistanceCheck);
                }
            }

            if (nearAxis == Axis.None && TransformTypeContains(TransformType.Rotate)) {
                HandleNearestLines(TransformType.Rotate, circlesLines, handleMinSelectedDistanceCheck);
            }
        }

        void HandleNearestLines(TransformType type, AxisVectors axisVectors, float minSelectedDistanceCheck) {
            float xClosestDistance = ClosestDistanceFromMouseToLines(axisVectors.x);
            float yClosestDistance = ClosestDistanceFromMouseToLines(axisVectors.y);
            float zClosestDistance = ClosestDistanceFromMouseToLines(axisVectors.z);
            float allClosestDistance = ClosestDistanceFromMouseToLines(axisVectors.all);

            HandleNearest(type, xClosestDistance, yClosestDistance, zClosestDistance, allClosestDistance, minSelectedDistanceCheck);
        }

        void HandleNearestPlanes(TransformType type, AxisVectors axisVectors, float minSelectedDistanceCheck) {
            float xClosestDistance = ClosestDistanceFromMouseToPlanes(axisVectors.x);
            float yClosestDistance = ClosestDistanceFromMouseToPlanes(axisVectors.y);
            float zClosestDistance = ClosestDistanceFromMouseToPlanes(axisVectors.z);
            float allClosestDistance = ClosestDistanceFromMouseToPlanes(axisVectors.all);

            HandleNearest(type, xClosestDistance, yClosestDistance, zClosestDistance, allClosestDistance, minSelectedDistanceCheck);
        }

        void HandleNearest(TransformType type, float xClosestDistance, float yClosestDistance, float zClosestDistance, float allClosestDistance, float minSelectedDistanceCheck) {
            if (type == TransformType.Scale && allClosestDistance <= minSelectedDistanceCheck) SetTranslatingAxis(type, Axis.Any);
            else if (xClosestDistance <= minSelectedDistanceCheck && xClosestDistance <= yClosestDistance && xClosestDistance <= zClosestDistance) SetTranslatingAxis(type, Axis.X);
            else if (yClosestDistance <= minSelectedDistanceCheck && yClosestDistance <= xClosestDistance && yClosestDistance <= zClosestDistance) SetTranslatingAxis(type, Axis.Y);
            else if (zClosestDistance <= minSelectedDistanceCheck && zClosestDistance <= xClosestDistance && zClosestDistance <= yClosestDistance) SetTranslatingAxis(type, Axis.Z);
            else if (type == TransformType.Rotate && mainTargetRoot != null) {
                var mouseRay = GetMouseRay();
                Vector3 mousePlaneHit = Geometry.LinePlaneIntersect(mouseRay.origin, mouseRay.direction, pivotPoint, (GetCameraPosition() - pivotPoint).Normalized());
                float rotateHandleLength = GetHandleLength(TransformType.Rotate);
                if ((pivotPoint - mousePlaneHit).LengthSquared() <= rotateHandleLength * rotateHandleLength) SetTranslatingAxis(type, Axis.Any);
            }
        }

        float ClosestDistanceFromMouseToLines(List<Vector3> lines) {
            var mouseRay = GetMouseRay();

            float closestDistance = float.MaxValue;
            for (int i = 0; i + 1 < lines.Count; i++) {
                IntersectPoints points = Geometry.ClosestPointsOnSegmentToLine(lines[i], lines[i + 1], mouseRay.origin, mouseRay.direction);
                float distance = points.first.DistanceTo(points.second);
                if (distance < closestDistance) {
                    closestDistance = distance;
                }
            }
            return closestDistance;
        }

        float ClosestDistanceFromMouseToPlanes(List<Vector3> planePoints) {
            float closestDistance = float.MaxValue;

            if (planePoints.Count >= 4) {
                var mouseRay = GetMouseRay();

                for (int i = 0; i < planePoints.Count; i += 4) {
                    var plane = new Plane(planePoints[i], planePoints[i + 1], planePoints[i + 2]);

                    var pointOnPlane = plane.IntersectsRay(mouseRay.origin, mouseRay.direction);
                    if (pointOnPlane.HasValue) {
                        Vector3 planeCenter = (planePoints[0] + planePoints[1] + planePoints[2] + planePoints[3]) / 4f;

                        float distance = planeCenter.DistanceTo(pointOnPlane.Value);
                        if (distance < closestDistance) {
                            closestDistance = distance;
                        }
                    }
                }
            }

            return closestDistance;
        }

        void SetAxisInfo() {
            if (mainTargetRoot != null) {
                axisInfo.Set(mainTargetRoot, pivotPoint, GetProperTransformSpace());
            }
        }

        //This helps keep the size consistent no matter how far we are from it.
        public float GetDistanceMultiplier() {
            if (mainTargetRoot == null || myCamera == null) return 0f;

            if (myCamera.Projection == Camera3D.ProjectionType.Orthogonal) return Mathf.Max(.01f, myCamera.Size);
            var cameraForward = -myCamera.GlobalBasis.Z.Normalized();
            return Mathf.Max(.01f, Mathf.Abs(ExtVector3.MagnitudeInDirection(pivotPoint - GetCameraPosition(), cameraForward)));
        }

        void SetLines() {
            SetHandleLines();
            SetHandlePlanes();
            SetHandleTriangles();
            SetHandleSquares();
            SetCircles(GetAxisInfo(), circlesLines);
        }

        void SetHandleLines() {
            handleLines.Clear();

            if (TranslatingTypeContains(TransformType.Move) || TranslatingTypeContains(TransformType.Scale)) {
                float lineWidth = handleWidth * GetDistanceMultiplier();

                float xLineLength = 0;
                float yLineLength = 0;
                float zLineLength = 0;
                if (TranslatingTypeContains(TransformType.Move)) {
                    xLineLength = yLineLength = zLineLength = GetHandleLength(TransformType.Move);
                } else if (TranslatingTypeContains(TransformType.Scale)) {
                    xLineLength = GetHandleLength(TransformType.Scale, Axis.X);
                    yLineLength = GetHandleLength(TransformType.Scale, Axis.Y);
                    zLineLength = GetHandleLength(TransformType.Scale, Axis.Z);
                }

                AddQuads(pivotPoint, axisInfo.xDirection, axisInfo.yDirection, axisInfo.zDirection, xLineLength, lineWidth, handleLines.x);
                AddQuads(pivotPoint, axisInfo.yDirection, axisInfo.xDirection, axisInfo.zDirection, yLineLength, lineWidth, handleLines.y);
                AddQuads(pivotPoint, axisInfo.zDirection, axisInfo.xDirection, axisInfo.yDirection, zLineLength, lineWidth, handleLines.z);
            }
        }

        void SetHandlePlanes() {
            handlePlanes.Clear();

            if (TranslatingTypeContains(TransformType.Move)) {
                Vector3 pivotToCamera = GetCameraPosition() - pivotPoint;
                float cameraXSign = Mathf.Sign(axisInfo.xDirection.Dot(pivotToCamera));
                float cameraYSign = Mathf.Sign(axisInfo.yDirection.Dot(pivotToCamera));
                float cameraZSign = Mathf.Sign(axisInfo.zDirection.Dot(pivotToCamera));

                float planeSize = this.planeSize * GetDistanceMultiplier();

                Vector3 xDirection = (axisInfo.xDirection * planeSize) * cameraXSign;
                Vector3 yDirection = (axisInfo.yDirection * planeSize) * cameraYSign;
                Vector3 zDirection = (axisInfo.zDirection * planeSize) * cameraZSign;

                Vector3 xPlaneCenter = pivotPoint + (yDirection + zDirection);
                Vector3 yPlaneCenter = pivotPoint + (xDirection + zDirection);
                Vector3 zPlaneCenter = pivotPoint + (xDirection + yDirection);

                AddQuad(xPlaneCenter, axisInfo.yDirection, axisInfo.zDirection, planeSize, handlePlanes.x);
                AddQuad(yPlaneCenter, axisInfo.xDirection, axisInfo.zDirection, planeSize, handlePlanes.y);
                AddQuad(zPlaneCenter, axisInfo.xDirection, axisInfo.yDirection, planeSize, handlePlanes.z);
            }
        }

        void SetHandleTriangles() {
            handleTriangles.Clear();

            if (TranslatingTypeContains(TransformType.Move)) {
                float triangleLength = triangleSize * GetDistanceMultiplier();
                AddTriangles(axisInfo.GetXAxisEnd(GetHandleLength(TransformType.Move)), axisInfo.xDirection, axisInfo.yDirection, axisInfo.zDirection, triangleLength, handleTriangles.x);
                AddTriangles(axisInfo.GetYAxisEnd(GetHandleLength(TransformType.Move)), axisInfo.yDirection, axisInfo.xDirection, axisInfo.zDirection, triangleLength, handleTriangles.y);
                AddTriangles(axisInfo.GetZAxisEnd(GetHandleLength(TransformType.Move)), axisInfo.zDirection, axisInfo.yDirection, axisInfo.xDirection, triangleLength, handleTriangles.z);
            }
        }

        void AddTriangles(Vector3 axisEnd, Vector3 axisDirection, Vector3 axisOtherDirection1, Vector3 axisOtherDirection2, float size, List<Vector3> resultsBuffer) {
            Vector3 endPoint = axisEnd + (axisDirection * (size * 2f));
            Square baseSquare = GetBaseSquare(axisEnd, axisOtherDirection1, axisOtherDirection2, size / 2f);

            resultsBuffer.Add(baseSquare.bottomLeft);
            resultsBuffer.Add(baseSquare.topLeft);
            resultsBuffer.Add(baseSquare.topRight);
            resultsBuffer.Add(baseSquare.topLeft);
            resultsBuffer.Add(baseSquare.bottomRight);
            resultsBuffer.Add(baseSquare.topRight);

            for (int i = 0; i < 4; i++) {
                resultsBuffer.Add(baseSquare[i]);
                resultsBuffer.Add(baseSquare[i + 1]);
                resultsBuffer.Add(endPoint);
            }
        }

        void SetHandleSquares() {
            handleSquares.Clear();

            if (TranslatingTypeContains(TransformType.Scale)) {
                float boxSize = this.boxSize * GetDistanceMultiplier();
                AddSquares(axisInfo.GetXAxisEnd(GetHandleLength(TransformType.Scale, Axis.X)), axisInfo.xDirection, axisInfo.yDirection, axisInfo.zDirection, boxSize, handleSquares.x);
                AddSquares(axisInfo.GetYAxisEnd(GetHandleLength(TransformType.Scale, Axis.Y)), axisInfo.yDirection, axisInfo.xDirection, axisInfo.zDirection, boxSize, handleSquares.y);
                AddSquares(axisInfo.GetZAxisEnd(GetHandleLength(TransformType.Scale, Axis.Z)), axisInfo.zDirection, axisInfo.xDirection, axisInfo.yDirection, boxSize, handleSquares.z);
                AddSquares(pivotPoint - (axisInfo.xDirection * (boxSize * .5f)), axisInfo.xDirection, axisInfo.yDirection, axisInfo.zDirection, boxSize, handleSquares.all);
            }
        }

        void AddSquares(Vector3 axisStart, Vector3 axisDirection, Vector3 axisOtherDirection1, Vector3 axisOtherDirection2, float size, List<Vector3> resultsBuffer) {
            AddQuads(axisStart, axisDirection, axisOtherDirection1, axisOtherDirection2, size, size * .5f, resultsBuffer);
        }
        void AddQuads(Vector3 axisStart, Vector3 axisDirection, Vector3 axisOtherDirection1, Vector3 axisOtherDirection2, float length, float width, List<Vector3> resultsBuffer) {
            Vector3 axisEnd = axisStart + (axisDirection * length);
            AddQuads(axisStart, axisEnd, axisOtherDirection1, axisOtherDirection2, width, resultsBuffer);
        }
        void AddQuads(Vector3 axisStart, Vector3 axisEnd, Vector3 axisOtherDirection1, Vector3 axisOtherDirection2, float width, List<Vector3> resultsBuffer) {
            Square baseRectangle = GetBaseSquare(axisStart, axisOtherDirection1, axisOtherDirection2, width);
            Square baseRectangleEnd = GetBaseSquare(axisEnd, axisOtherDirection1, axisOtherDirection2, width);

            resultsBuffer.Add(baseRectangle.bottomLeft);
            resultsBuffer.Add(baseRectangle.topLeft);
            resultsBuffer.Add(baseRectangle.topRight);
            resultsBuffer.Add(baseRectangle.bottomRight);

            resultsBuffer.Add(baseRectangleEnd.bottomLeft);
            resultsBuffer.Add(baseRectangleEnd.topLeft);
            resultsBuffer.Add(baseRectangleEnd.topRight);
            resultsBuffer.Add(baseRectangleEnd.bottomRight);

            for (int i = 0; i < 4; i++) {
                resultsBuffer.Add(baseRectangle[i]);
                resultsBuffer.Add(baseRectangleEnd[i]);
                resultsBuffer.Add(baseRectangleEnd[i + 1]);
                resultsBuffer.Add(baseRectangle[i + 1]);
            }
        }

        void AddQuad(Vector3 axisStart, Vector3 axisOtherDirection1, Vector3 axisOtherDirection2, float width, List<Vector3> resultsBuffer) {
            Square baseRectangle = GetBaseSquare(axisStart, axisOtherDirection1, axisOtherDirection2, width);

            resultsBuffer.Add(baseRectangle.bottomLeft);
            resultsBuffer.Add(baseRectangle.topLeft);
            resultsBuffer.Add(baseRectangle.topRight);
            resultsBuffer.Add(baseRectangle.bottomRight);
        }

        Square GetBaseSquare(Vector3 axisEnd, Vector3 axisOtherDirection1, Vector3 axisOtherDirection2, float size) {
            Square square;
            Vector3 offsetUp = ((axisOtherDirection1 * size) + (axisOtherDirection2 * size));
            Vector3 offsetDown = ((axisOtherDirection1 * size) - (axisOtherDirection2 * size));
            //These might not really be the proper directions, as in the bottomLeft might not really be at the bottom left...
            square.bottomLeft = axisEnd + offsetDown;
            square.topLeft = axisEnd + offsetUp;
            square.bottomRight = axisEnd - offsetUp;
            square.topRight = axisEnd - offsetDown;
            return square;
        }

        void SetCircles(AxisInfo axisInfo, AxisVectors axisVectors) {
            axisVectors.Clear();

            if (TranslatingTypeContains(TransformType.Rotate)) {
                float circleLength = GetHandleLength(TransformType.Rotate);
                AddCircle(pivotPoint, axisInfo.xDirection, circleLength, axisVectors.x);
                AddCircle(pivotPoint, axisInfo.yDirection, circleLength, axisVectors.y);
                AddCircle(pivotPoint, axisInfo.zDirection, circleLength, axisVectors.z);
                AddCircle(pivotPoint, (pivotPoint - GetCameraPosition()).Normalized(), circleLength, axisVectors.all, false);
            }
        }

        void AddCircle(Vector3 origin, Vector3 axisDirection, float size, List<Vector3> resultsBuffer, bool depthTest = true) {
            //Godot's Slerp(up, -up, .5) degenerates, so pick a perpendicular explicitly
            Vector3 up = axisDirection.Normalized() * size;
            Vector3 helper = Mathf.Abs(axisDirection.Normalized().Dot(Vector3.Up)) < 0.99f ? Vector3.Up : Vector3.Right;
            Vector3 forward = up.Cross(helper).Normalized() * size;
            Vector3 right = up.Cross(forward).Normalized() * size;

            Vector3 lastPoint = origin + right;
            Vector3 nextPoint;
            float multiplier = 360f / circleDetail;

            var plane = new Plane((GetCameraPosition() - pivotPoint).Normalized(), pivotPoint);

            float circleHandleWidth = handleWidth * GetDistanceMultiplier();

            for (int i = 0; i < circleDetail + 1; i++) {
                float angle = Mathf.DegToRad(i * multiplier);
                nextPoint = origin + right * Mathf.Cos(angle) + forward * Mathf.Sin(angle);

                if (!depthTest || plane.IsPointOver(lastPoint)) {
                    Vector3 centerPoint = (lastPoint + nextPoint) * .5f;
                    Vector3 upDirection = (centerPoint - origin).Normalized();
                    AddQuads(lastPoint, nextPoint, upDirection, axisDirection, circleHandleWidth, resultsBuffer);
                }

                lastPoint = nextPoint;
            }
        }

        //The handles are rebuilt into an always-on-top mesh every frame
        void Draw() {
            drawVertices.Clear();
            drawColors.Clear();

            Color xColor = (nearAxis == Axis.X) ? (isTransforming) ? selectedColor : hoverColor : this.xColor;
            Color yColor = (nearAxis == Axis.Y) ? (isTransforming) ? selectedColor : hoverColor : this.yColor;
            Color zColor = (nearAxis == Axis.Z) ? (isTransforming) ? selectedColor : hoverColor : this.zColor;
            Color allColor = (nearAxis == Axis.Any) ? (isTransforming) ? selectedColor : hoverColor : this.allColor;

            //hide disabled axes
            var clear = new Color(0, 0, 0, 0);
            bool[] axesAllowed = GetAxesAllowed(translatingType);
            if (!axesAllowed[0]) xColor = clear;
            if (!axesAllowed[1]) yColor = clear;
            if (!axesAllowed[2]) zColor = clear;
            if (!(axesAllowed[0] && axesAllowed[1] && axesAllowed[2])) allColor = clear;

            //Note: The order of drawing the axis decides what gets drawn over what.

            TransformType moveOrScaleType = (transformType == TransformType.Scale || (isTransforming && translatingType == TransformType.Scale)) ? TransformType.Scale : TransformType.Move;
            DrawQuads(handleLines.z, GetColor(moveOrScaleType, this.zColor, zColor, hasTranslatingAxisPlane));
            DrawQuads(handleLines.x, GetColor(moveOrScaleType, this.xColor, xColor, hasTranslatingAxisPlane));
            DrawQuads(handleLines.y, GetColor(moveOrScaleType, this.yColor, yColor, hasTranslatingAxisPlane));

            DrawTriangles(handleTriangles.x, GetColor(TransformType.Move, this.xColor, xColor, hasTranslatingAxisPlane));
            DrawTriangles(handleTriangles.y, GetColor(TransformType.Move, this.yColor, yColor, hasTranslatingAxisPlane));
            DrawTriangles(handleTriangles.z, GetColor(TransformType.Move, this.zColor, zColor, hasTranslatingAxisPlane));

            DrawQuads(handlePlanes.z, GetColor(TransformType.Move, this.zColor, zColor, planesOpacity, !hasTranslatingAxisPlane));
            DrawQuads(handlePlanes.x, GetColor(TransformType.Move, this.xColor, xColor, planesOpacity, !hasTranslatingAxisPlane));
            DrawQuads(handlePlanes.y, GetColor(TransformType.Move, this.yColor, yColor, planesOpacity, !hasTranslatingAxisPlane));

            DrawQuads(handleSquares.x, GetColor(TransformType.Scale, this.xColor, xColor));
            DrawQuads(handleSquares.y, GetColor(TransformType.Scale, this.yColor, yColor));
            DrawQuads(handleSquares.z, GetColor(TransformType.Scale, this.zColor, zColor));
            DrawQuads(handleSquares.all, GetColor(TransformType.Scale, this.allColor, allColor));

            DrawQuads(circlesLines.all, GetColor(TransformType.Rotate, this.allColor, allColor));
            DrawQuads(circlesLines.x, GetColor(TransformType.Rotate, this.xColor, xColor));
            DrawQuads(circlesLines.y, GetColor(TransformType.Rotate, this.yColor, yColor));
            DrawQuads(circlesLines.z, GetColor(TransformType.Rotate, this.zColor, zColor));

            drawMesh.ClearSurfaces();
            if (drawVertices.Count == 0) return;
            drawMesh.SurfaceBegin(Mesh.PrimitiveType.Triangles, lineMaterial);
            for (int i = 0; i < drawVertices.Count; i++) {
                drawMesh.SurfaceSetColor(drawColors[i]);
                drawMesh.SurfaceAddVertex(drawVertices[i]);
            }
            drawMesh.SurfaceEnd();
        }

        void AddDrawTriangle(Vector3 a, Vector3 b, Vector3 c, Color color) {
            drawVertices.Add(a);
            drawVertices.Add(b);
            drawVertices.Add(c);
            drawColors.Add(color);
            drawColors.Add(color);
            drawColors.Add(color);
        }

        void DrawTriangles(List<Vector3> lines, Color color) {
            if (lines.Count == 0 || color.A <= 0) return;
            color = color.SrgbToLinear(); //vertex colors are linear

            for (int i = 0; i + 2 < lines.Count; i += 3) {
                AddDrawTriangle(lines[i], lines[i + 1], lines[i + 2], color);
            }
        }

        void DrawQuads(List<Vector3> lines, Color color) {
            if (lines.Count == 0 || color.A <= 0) return;
            color = color.SrgbToLinear();

            for (int i = 0; i + 3 < lines.Count; i += 4) {
                AddDrawTriangle(lines[i], lines[i + 1], lines[i + 2], color);
                AddDrawTriangle(lines[i], lines[i + 2], lines[i + 3], color);
            }
        }
    }

    public struct AxisInfo {
        public Vector3 pivot;
        public Vector3 xDirection;
        public Vector3 yDirection;
        public Vector3 zDirection;

        public void Set(Node3D target, Vector3 pivot, TransformSpace space) {
            Node3D reference = null;
            if (space == TransformSpace.Local) reference = target;
            else if (space == TransformSpace.Parent) reference = target.GetParentNode3D();

            if (reference == null) {
                xDirection = Vector3.Right;
                yDirection = Vector3.Up;
                zDirection = Vector3.Forward;
            } else {
                var basis = reference.GlobalBasis.Orthonormalized();
                xDirection = basis.X;
                yDirection = basis.Y;
                zDirection = -basis.Z;
            }

            this.pivot = pivot;
        }

        public Vector3 GetXAxisEnd(float size) {
            return pivot + (xDirection * size);
        }
        public Vector3 GetYAxisEnd(float size) {
            return pivot + (yDirection * size);
        }
        public Vector3 GetZAxisEnd(float size) {
            return pivot + (zDirection * size);
        }
    }

    public class AxisVectors {
        public List<Vector3> x = new List<Vector3>();
        public List<Vector3> y = new List<Vector3>();
        public List<Vector3> z = new List<Vector3>();
        public List<Vector3> all = new List<Vector3>();

        public void Clear() {
            x.Clear();
            y.Clear();
            z.Clear();
            all.Clear();
        }
    }

    public struct Square {
        public Vector3 bottomLeft;
        public Vector3 bottomRight;
        public Vector3 topLeft;
        public Vector3 topRight;

        public Vector3 this[int index] {
            get {
                switch (index) {
                    case 0: return bottomLeft;
                    case 1: return topLeft;
                    case 2: return topRight;
                    case 3: return bottomRight;
                    case 4: return bottomLeft; //so we wrap around back to start
                    default: return Vector3.Zero;
                }
            }
        }
    }

    public struct IntersectPoints {
        public Vector3 first;
        public Vector3 second;
    }

    public static class Geometry {
        public static float LinePlaneDistance(Vector3 linePoint, Vector3 lineVec, Vector3 planePoint, Vector3 planeNormal) {
            //calculate the distance between the linePoint and the line-plane intersection point
            float dotNumerator = (planePoint - linePoint).Dot(planeNormal);
            float dotDenominator = lineVec.Dot(planeNormal);

            //line and plane are not parallel
            if (dotDenominator != 0f) {
                return dotNumerator / dotDenominator;
            }

            return 0;
        }

        //Note that the line is infinite, this is not a line-segment plane intersect
        public static Vector3 LinePlaneIntersect(Vector3 linePoint, Vector3 lineVec, Vector3 planePoint, Vector3 planeNormal) {
            float distance = LinePlaneDistance(linePoint, lineVec, planePoint, planeNormal);

            //line and plane are not parallel
            if (distance != 0f) {
                return linePoint + (lineVec * distance);
            }

            return Vector3.Zero;
        }

        //Returns 2 points since on line 1 there will be a closest point to line 2, and on line 2 there will be a closest point to line 1.
        public static IntersectPoints ClosestPointsOnTwoLines(Vector3 point1, Vector3 point1Direction, Vector3 point2, Vector3 point2Direction) {
            IntersectPoints intersections = new IntersectPoints();

            float a = point1Direction.Dot(point1Direction);
            float b = point1Direction.Dot(point2Direction);
            float e = point2Direction.Dot(point2Direction);

            float d = a * e - b * b;

            if (d != 0f) {
                Vector3 r = point1 - point2;
                float c = point1Direction.Dot(r);
                float f = point2Direction.Dot(r);

                float s = (b * f - c * e) / d;
                float t = (a * f - c * b) / d;

                intersections.first = point1 + point1Direction * s;
                intersections.second = point2 + point2Direction * t;
            } else {
                //Lines are parallel, select any points next to eachother
                intersections.first = point1;
                intersections.second = point2 + ExtVector3.Project(point1 - point2, point2Direction);
            }

            return intersections;
        }

        public static IntersectPoints ClosestPointsOnSegmentToLine(Vector3 segment0, Vector3 segment1, Vector3 linePoint, Vector3 lineDirection) {
            IntersectPoints closests = ClosestPointsOnTwoLines(segment0, segment1 - segment0, linePoint, lineDirection);
            closests.first = ClampToSegment(closests.first, segment0, segment1);

            return closests;
        }

        //Assumes the point is already on the line somewhere
        public static Vector3 ClampToSegment(Vector3 point, Vector3 linePoint1, Vector3 linePoint2) {
            Vector3 lineDirection = linePoint2 - linePoint1;

            if (!ExtVector3.IsInDirection(point - linePoint1, lineDirection)) {
                point = linePoint1;
            } else if (ExtVector3.IsInDirection(point - linePoint2, lineDirection)) {
                point = linePoint2;
            }

            return point;
        }
    }

    public static class ExtVector3 {
        public static float MagnitudeInDirection(Vector3 vector, Vector3 direction, bool normalizeParameters = true) {
            if (normalizeParameters) direction = direction.Normalized();
            return vector.Dot(direction);
        }

        public static Vector3 Abs(Vector3 vector) {
            return new Vector3(Mathf.Abs(vector.X), Mathf.Abs(vector.Y), Mathf.Abs(vector.Z));
        }

        public static bool IsParallel(Vector3 direction, Vector3 otherDirection, float precision = .0001f) {
            return direction.Cross(otherDirection).LengthSquared() < precision;
        }

        public static bool IsInDirection(Vector3 direction, Vector3 otherDirection) {
            return direction.Dot(otherDirection) > 0f;
        }

        public static Vector3 Project(Vector3 vector, Vector3 onNormal) {
            float sqrMag = onNormal.LengthSquared();
            if (sqrMag < Mathf.Epsilon) return Vector3.Zero;
            return onNormal * vector.Dot(onNormal) / sqrMag;
        }

        public static Vector3 ProjectOnPlane(Vector3 vector, Vector3 planeNormal) {
            float sqrMag = planeNormal.LengthSquared();
            if (sqrMag < Mathf.Epsilon) return vector;
            return vector - planeNormal * vector.Dot(planeNormal) / sqrMag;
        }
    }
}
