using System.Collections.Generic;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;

namespace Operations {
    //The anchors along the edges of the roads and intersections, where terrain patches and building lines attach.
    //Each anchor is linked to the previous and next one (also across elements), forming the loops around the blocks.
    public static class Anchors {
        //a terrain point attached to an anchor: directly, or on the segment from the anchor to the next one
        public struct User {
            public object element; //terrain patch or building line
            public TerrainPoint point;
            public bool segment;
        }

        //the road or intersection the anchor belongs to, and its index among their anchors (null, -1 if not found)
        public static (object owner, int index) GetOwner(TerrainAnchor anchor) {
            if (anchor == null || GO.IsNull(anchor) || anchor.IsDeleted()) return (null, -1);
            var parent = GO.GetParent(anchor.GetObject());
            if (parent == null) return (null, -1);
            var road = GS.Cast<Road>(parent);
            if (road != null) return (road, road.anchorManager.GetTerrainAnchors().IndexOf(anchor));
            var intersection = GS.FindRecursive<Intersection.IntersectionComponent>(parent);
            if (intersection != null) return (intersection.intersection, intersection.intersection.anchorManager.GetTerrainAnchors().IndexOf(anchor));
            return (null, -1);
        }

        //every anchor of the city, in the order used by the city-wide generators
        public static List<TerrainAnchor> GetAll(ElementManager manager) {
            var res = new List<TerrainAnchor>();
            var container = manager.GetRoadContainer();
            for (int i = 0; i < GO.GetChildCount(container); i++) {
                var childI = GO.GetChildI(container, i);
                for (int j = 0; j < GO.GetChildCount(childI); j++) {
                    var anchor = GS.FindRecursive<TerrainAnchor>(GO.GetChildI(childI, j));
                    if (anchor != null) res.Add(anchor);
                }
            }
            return res;
        }

        static void AddUser(Dictionary<TerrainAnchor, List<User>> res, object element, TerrainPoint point) {
            if (point == null || point.IsDeleted() || GO.IsNull(point.anchor) || point.anchor.IsDeleted()) return;
            TerrainAnchor anchor = null;
            var segment = false;
            if (point.anchor is TerrainAnchor ta) {
                anchor = ta;
            } else if (point.anchor is LineAnchor la) {
                anchor = GS.FindRecursive<TerrainAnchor>(la.start);
                segment = true;
            }
            if (anchor == null) return;
            if (!res.TryGetValue(anchor, out var list)) {
                list = new List<User>();
                res[anchor] = list;
            }
            list.Add(new User { element = element, point = point, segment = segment });
        }

        //the terrain patch and building line points attached to each anchor
        public static Dictionary<TerrainAnchor, List<User>> GetUsers(ElementManager manager) {
            var res = new Dictionary<TerrainAnchor, List<User>>();
            foreach (var patch in manager.patches) {
                if (patch.IsDeleted()) continue;
                foreach (var p in patch.GetPerimeterPointsComponents()) AddUser(res, patch, p);
                foreach (var p in patch.GetInternalPointsComponents()) AddUser(res, patch, p);
            }
            foreach (var line in manager.buildings) {
                if (line.IsDeleted()) continue;
                foreach (var p in line.GetPointsComponents()) AddUser(res, line, p);
            }
            return res;
        }

        //true if a point of an element of the type is on the anchor itself (as checked by the city-wide generators)
        public static bool IsUsedBy<T>(List<User> users) {
            if (users == null) return false;
            foreach (var u in users) {
                if (u.element is T && !u.segment) return true;
            }
            return false;
        }

        //the anchors from one to the other along the shortest way (to: null or the same anchor for the loop around
        //the block, ending with the anchor itself), as followed when adding points between two anchors
        public static List<TerrainAnchor> GetLoop(TerrainAnchor from, TerrainAnchor to = null, bool invert = false) {
            return from.GetPointListTo(to ?? from, invert);
        }

        //the link from the anchor to the next one (null at the end of a chain)
        public static PointLink GetLink(TerrainAnchor anchor) {
            if (anchor == null || GO.IsNull(anchor.next)) return null;
            var link = GS.FindRecursive<PointLink>(anchor.GetObject());
            return link != null && link.next == anchor.next.GetObject() ? link : null;
        }
    }
}
