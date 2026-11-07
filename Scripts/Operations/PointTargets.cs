using System.Collections.Generic;
using Godot;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;

namespace Operations {
    //Where the control points of a spline custom element can go, decided by the Lua function of its acceptTarget
    //(see CustomElementSplineSettings): called with the parameters of the element and ctx.target, the description of
    //what was clicked (or of the point a control is to be attached to):
    //  kind: "point" (an existing terrain point), "anchor" (of a road or intersection), "segment" (between two points or
    //        anchors: a new point on it) or "ground"
    //  owners: the elements using the point or anchor, {kind, id, type, role}, role being "perimeter" or "internal" (the
    //          points of the terrain patches), "control" (the points of the building lines and custom elements), "edge" (the
    //          anchors along the roads and intersections), "end" (the anchors across the ends of the roads, only for the custom
    //          elements) or "roadEnd" (the free ends of the roads)
    //  anchor: for a point on an anchor of a road or intersection, the owner of the anchor (as in owners)
    //  ends: for a segment, the descriptions of its two ends
    //The types with an ownerReference (a reference parameter) keep their controls on the targets of one element, their
    //owner: the function is called for an owner of the target (ctx.owner, as in owners) the reference accepts, and the
    //first control makes the one accepted the owner (the user chooses it when there are several, see Candidates)
    public static class PointTargets {
        //the element, or its type when it's being created, accepts the target (obj null: the ground)
        public static bool Allows(CustomElement element, Node3D obj) {
            if (element == null || element.IsDeleted()) return true;
            return Allows(element.GetCustomElementType(), obj, element);
        }

        public static bool Allows(CityElements.Types.Runtime.CustomElementType type, Node3D obj, CustomElement element = null) {
            if (type == null) return true;
            if (OwnerParameter(type) == null) return Accepts(type, obj, element, null);
            var owner = element != null ? OwnerOf(element) : null;
            if (owner == null) return Candidates(type, obj, element).Count > 0;
            return AcceptsWith(type, obj, element, owner);
        }

        //the target is one of the owner's, and the function accepts it as such
        static bool AcceptsWith(CityElements.Types.Runtime.CustomElementType type, Node3D obj, CustomElement element, object owner) {
            foreach (var (o, role) in TargetOwners(obj, element)) {
                if (o == owner) return Accepts(type, obj, element, Owner(o, role));
            }
            return false;
        }

        static bool Accepts(CityElements.Types.Runtime.CustomElementType type, Node3D obj, CustomElement element, Lua.LuaTable owner) {
            var function = type.acceptTargetFunction;
            if (function == null) return true;
            var name = type.name + " (acceptTarget)";
            try {
                var ctx = new Lua.LuaTable();
                ctx["target"] = Describe(obj, element);
                if (owner != null) ctx["owner"] = owner;
                var variables = element != null ? element.GetPlacementVariables() : Lua.LuaValue.Nil;
                var value = type.scriptHost.Call(function, variables, ctx, name);
                return value.TryRead<bool>(out var accepted) && accepted;
            } catch (System.Exception e) {
                type.scriptHost.ReportError(name, e.Message);
                return false;
            }
        }

        //the elements using the point accept it as a target of their control points (for a point being attached elsewhere)
        public static bool AllowedByUsers(TerrainPoint point, Node3D obj) {
            if (point == null) return true;
            foreach (var link in point.GetLinks()) {
                if (link is CustomElement element && !Allows(element, obj)) return false;
            }
            return true;
        }

        //the elements using the point accept it leaving its target (e.g. moved freely with the gizmo, or detached)
        public static bool AllowsGround(TerrainPoint point) {
            return AllowedByUsers(point, null);
        }

        //the type accepts free points (whatever the parameters of the element: asked once; never with an owner)
        public static bool AcceptsGround(CityElements.Types.Runtime.CustomElementType type) {
            if (type == null || (type.acceptTargetFunction == null && OwnerParameter(type) == null)) return true;
            type.acceptsGround ??= Allows(type, null);
            return type.acceptsGround.Value;
        }

        // Owner (see CustomElementSplineSettings.ownerReference)

        //the reference parameter of the owner of the controls, null if the type has none
        public static CityElements.Types.Parameter OwnerParameter(CityElements.Types.Runtime.CustomElementType type) {
            var name = type?.typeData.settings.spline?.ownerReference;
            if (name == null || type.parameterContainer.parameters == null) return null;
            foreach (var param in type.parameterContainer.parameters) {
                if (param.fullName() == name) return param;
            }
            return null;
        }

        //the owner of the controls of the element, null if it has none (yet)
        public static object OwnerOf(CustomElement element) {
            var param = OwnerParameter(element?.GetCustomElementType());
            return param == null ? null : States.ResolveReference(element.state.State(param.fullName()));
        }

        public static void SetOwner(CustomElement element, object owner) {
            var param = OwnerParameter(element?.GetCustomElementType());
            if (param == null || OwnerOf(element) == owner) return;
            History.UndoManager.Touch(element);
            element.state.SetState(param.fullName(), owner == null ? null : States.MakeReference(owner));
            element.state.FlagAsChanged();
        }

        //the elements of the target that can own the controls of an element of the type (one without an owner yet): its
        //owners the reference accepts, for which the function accepts the target
        public static List<object> Candidates(CityElements.Types.Runtime.CustomElementType type, Node3D obj, CustomElement element = null) {
            var res = new List<object>();
            var param = OwnerParameter(type);
            if (param == null) return res;
            foreach (var (o, role) in TargetOwners(obj, element)) {
                if (res.Contains(o) || !States.AcceptsReferenceKind(param, element, o)) continue;
                if (Accepts(type, obj, element, Owner(o, role))) res.Add(o);
            }
            return res;
        }

        //an element without an owner gets the only candidate of the target (chosen: the one the user chose among several);
        //false if the target has none, or several and none was chosen
        public static bool ClaimOwner(CustomElement element, Node3D obj, object chosen = null) {
            var type = element?.GetCustomElementType();
            if (OwnerParameter(type) == null || OwnerOf(element) != null) return true;
            var candidates = Candidates(type, obj, element);
            var owner = chosen != null && candidates.Contains(chosen) ? chosen : candidates.Count == 1 ? candidates[0] : null;
            if (owner == null) return false;
            SetOwner(element, owner);
            return true;
        }

        //all the controls of the element are accepted with the owner (for a change of the reference)
        public static bool AcceptsOwner(CustomElement element, object owner) {
            var type = element?.GetCustomElementType();
            if (OwnerParameter(type) == null || owner == null) return true;
            foreach (var point in element.GetAllPoints()) {
                if (point != null && !point.IsDeleted() && !AcceptsWith(type, point.GetObject(), element, owner)) return false;
            }
            return true;
        }

        //the elements using the target (as in Describe), with their roles; a segment has the ones of both its ends
        static List<(object element, string role)> TargetOwners(Node3D obj, CustomElement exclude) {
            var res = new List<(object element, string role)>();
            if (obj == null) return res;
            var point = GS.FindComponent<TerrainPoint>(obj);
            if (point != null && !point.IsDeleted()) {
                foreach (var link in point.GetLinks()) {
                    if (link != exclude) res.Add((link, PointRole(link, point)));
                }
                if (point.anchor is TerrainAnchor onAnchor && !GO.IsNull(onAnchor) && !onAnchor.IsDeleted()) {
                    var anchorOwner = Anchors.OwnerElement(onAnchor);
                    if (anchorOwner != null && !res.Exists(x => x.element == anchorOwner)) res.Add((anchorOwner, AnchorRole(onAnchor)));
                }
                return res;
            }
            var anchor = GS.FindComponent<TerrainAnchor>(obj);
            if (anchor != null && !anchor.IsDeleted()) {
                var owner = Anchors.OwnerElement(anchor);
                if (owner != null) res.Add((owner, AnchorRole(anchor)));
                return res;
            }
            var segment = GS.Cast<PointLink>(obj);
            if (segment != null && !GO.IsNull(segment.next)) {
                var other = TargetOwners(segment.next, exclude);
                foreach (var (o, role) in TargetOwners(GO.GetParent(segment), exclude)) {
                    if (other.Exists(x => x.element == o)) res.Add((o, role));
                }
            }
            return res;
        }

        static string PointRole(IGroundable link, TerrainPoint point) {
            if (link is TerrainPatch patch) return patch.GetPerimeterPointsComponents().Contains(point) ? "perimeter" : "internal";
            return "control";
        }

        static string AnchorRole(TerrainAnchor anchor) => anchor.roadEnd ? "roadEnd" : anchor.customElementsOnly ? "end" : "edge";

        // Descriptions

        public static Lua.LuaTable Describe(Node3D obj, CustomElement exclude = null) {
            var point = obj == null ? null : GS.FindComponent<TerrainPoint>(obj);
            if (point != null && !point.IsDeleted()) return DescribePoint(point, exclude);
            var anchor = obj == null ? null : GS.FindComponent<TerrainAnchor>(obj);
            if (anchor != null && !anchor.IsDeleted()) return DescribeAnchor(anchor);
            var link = obj == null ? null : GS.Cast<PointLink>(obj);
            var res = new Lua.LuaTable();
            if (link != null && !GO.IsNull(link.next)) {
                res["kind"] = "segment";
                var ends = new Lua.LuaTable();
                ends[1] = Describe(GO.GetParent(link), exclude);
                ends[2] = Describe(link.next, exclude);
                res["ends"] = ends;
            } else {
                res["kind"] = "ground";
            }
            res["owners"] = new Lua.LuaTable();
            return res;
        }

        static Lua.LuaTable DescribePoint(TerrainPoint point, CustomElement exclude) {
            var res = new Lua.LuaTable();
            res["kind"] = "point";
            var owners = new Lua.LuaTable();
            int i = 0;
            foreach (var link in point.GetLinks()) {
                if (link == exclude) continue;
                var owner = Owner(link, PointRole(link, point));
                if (owner != null) owners[++i] = owner;
            }
            res["owners"] = owners;
            if (point.anchor is TerrainAnchor anchor && !GO.IsNull(anchor) && !anchor.IsDeleted()) {
                var anchorOwner = AnchorOwner(anchor);
                if (anchorOwner != null) res["anchor"] = anchorOwner;
            }
            return res;
        }

        static Lua.LuaTable DescribeAnchor(TerrainAnchor anchor) {
            var res = new Lua.LuaTable();
            res["kind"] = "anchor";
            var owners = new Lua.LuaTable();
            var owner = AnchorOwner(anchor);
            if (owner != null) owners[1] = owner;
            res["owners"] = owners;
            return res;
        }

        static Lua.LuaTable AnchorOwner(TerrainAnchor anchor) {
            var owner = Anchors.OwnerElement(anchor);
            return owner == null ? null : Owner(owner, AnchorRole(anchor));
        }

        static Lua.LuaTable Owner(object element, string role) {
            var kind = History.ElementSnapshot.ReferenceKind(element);
            if (kind == null) return null;
            var res = new Lua.LuaTable();
            res["kind"] = kind;
            res["id"] = History.ElementRegistry.GetId(element);
            res["type"] = (element as IObjectWithState)?.GetState()?.Str("type", "") ?? "";
            res["role"] = role;
            return res;
        }

        //the anchors and points of the city a type refuses are hidden while its points are placed (the other ones stay as
        //ElementManager.ShowAnchors left them)
        public static void HideRefused(ElementManager manager, CityElements.Types.Runtime.CustomElementType type, CustomElement element) {
            if (type == null || (type.acceptTargetFunction == null && OwnerParameter(type) == null)) return;
            foreach (var anchor in Anchors.GetAll(manager)) {
                if (!anchor.IsDeleted() && GO.GetEnabled(anchor.GetObject()) && !Allows(type, anchor.GetObject(), element)) anchor.SetActive(false);
            }
            foreach (var point in manager.GetTerrainPoints()) {
                if (point.IsDeleted() || (element != null && element.HasPoint(point))) continue;
                if (GO.GetEnabled(point.GetObject()) && !Allows(type, point.GetObject(), element)) point.SetActive(false);
            }
        }
    }
}
