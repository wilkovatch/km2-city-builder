using System.Collections;
using System.Collections.Generic;
using Godot;
using Input = GameEngine.Input;
using KeyCommand = GameEngine.Input.KeyCommand;
using RaycastHit = GameEngine.Raycast.RaycastHit;

namespace ElementPlacer {
    //Picks places on the outline of the element being edited, for the tools cutting it (splitting a terrain patch or a
    //building line): one of its points, or anywhere on a segment between two of them, where the tool then inserts a
    //point. The place closest to the mouse on screen is shown with a handle, green if it can be picked and red otherwise,
    //and the place picked before it has a yellow handle, joined to it with a line. Like the ElementPicker it works over the
    //click mode there is (see TerrainClick.overlay), and ends with the last place picked, when that click mode changes or
    //with Escape (see CityBuilderMenuBar).
    public class OutlinePicker : TerrainAction {
        //a place on the outline: an existing point, or a position on the segment from the point at the index segment
        public class Place {
            public TerrainPoint point;
            public int segment;
            public Vector3 position;
            public float order; //along the outline: the index of the point, or the one of the segment plus the fraction
        }

        const float PointSnap = 14; //pixels from a point to pick it
        const float SegmentSnap = 40; //pixels from the outline to pick a place on it
        const float EndFraction = 0.001f; //a place on a segment as close to one of its points is that point
        CityBuilderMenuBar builder;
        System.Func<List<TerrainPoint>> points;
        bool loop;
        int count;
        System.Func<IReadOnlyList<Place>, Place, bool> accepts;
        System.Action<List<Place>> onPick;
        System.Action onEnd;
        TerrainAction previous;
        readonly List<Place> picked = new List<Place>();
        Place hovered = null;
        bool active = false;

        public OutlinePicker(CityBuilderMenuBar builder) {
            this.builder = builder;
        }

        public IReadOnlyList<Place> Picked => picked;

        //(another click mode set meanwhile ends it)
        public bool IsActive() {
            return active && builder.terrainClick.overlay == this;
        }

        //points: the points of the outline (null when the element is gone); loop: whether the last point is joined to
        //the first one; count: the places to pick; accepts: whether a place can follow the ones picked before it;
        //onEnd: after the pick or its end
        public void Start(System.Func<List<TerrainPoint>> points, bool loop, int count, System.Func<IReadOnlyList<Place>, Place, bool> accepts,
            System.Action<List<Place>> onPick, System.Action onEnd = null) {
            Stop();
            builder.elementPicker.Stop(); //one tool over the click mode at a time
            this.points = points;
            this.loop = loop;
            this.count = count;
            this.accepts = accepts;
            this.onPick = onPick;
            this.onEnd = onEnd;
            picked.Clear();
            active = true;
            previous = builder.terrainClick.modifier;
            builder.terrainClick.overlay = this;
        }

        public void Stop() {
            End(null);
        }

        void End(System.Action action) {
            if (!active) return;
            var end = onEnd;
            active = false;
            points = null;
            accepts = null;
            onPick = null;
            onEnd = null;
            hovered = null;
            picked.Clear();
            if (builder.terrainClick.overlay == this) builder.terrainClick.overlay = null;
            previous = null;
            action?.Invoke();
            end?.Invoke();
        }

        //picks the place (what a click on it does), returns whether it was accepted
        public bool Pick(Place place) {
            if (!IsActive() || place == null || !accepts(picked, place)) return false;
            picked.Add(place);
            if (picked.Count < count) return true;
            var res = new List<Place>(picked);
            var pick = onPick;
            End(() => pick?.Invoke(res));
            return true;
        }

        // Places

        Place PointPlace(List<TerrainPoint> outline, int i) {
            return new Place { point = outline[i], segment = i, position = outline[i].GetPoint(), order = i };
        }

        int SegmentCount(List<TerrainPoint> outline) {
            return loop && outline.Count > 2 ? outline.Count : outline.Count - 1;
        }

        //on the segment from the point at the index, at the fraction t (a point when at one of its ends)
        Place SegmentPlace(List<TerrainPoint> outline, int i, float t) {
            if (t <= EndFraction) return PointPlace(outline, i);
            if (t >= 1 - EndFraction) return PointPlace(outline, (i + 1) % outline.Count);
            var a = outline[i].GetPoint();
            var b = outline[(i + 1) % outline.Count].GetPoint();
            return new Place { segment = i, position = a.Lerp(b, t), order = i + t };
        }

        //the place of the outline closest to a position (program coordinates): a point if it is there, otherwise the
        //closest position of a segment
        public Place PlaceNear(Vector3 position) {
            var outline = points?.Invoke();
            if (outline == null || outline.Count < 2) return null;
            Place best = null;
            var bestDistance = float.MaxValue;
            for (int i = 0; i < SegmentCount(outline); i++) {
                var a = outline[i].GetPoint();
                var b = outline[(i + 1) % outline.Count].GetPoint();
                var ab = b - a;
                var t = ab.LengthSquared() < 1e-8f ? 0 : Mathf.Clamp((position - a).Dot(ab) / ab.LengthSquared(), 0, 1);
                var distance = a.Lerp(b, t).DistanceSquaredTo(position);
                if (distance < bestDistance) {
                    bestDistance = distance;
                    best = SegmentPlace(outline, i, t);
                }
            }
            return best;
        }

        //the place under the mouse: a point close to it on screen, otherwise the closest position of a segment
        Place PlaceUnderMouse() {
            var outline = points?.Invoke();
            var cam = builder.GetViewport().GetCamera3D();
            if (outline == null || outline.Count < 2 || cam == null) return null;
            var mouse = Input.MousePosition(builder.GetViewport());
            Vector2? Screen(Vector3 p) {
                var godot = GameEngine.Vector3.DecodePosition(p);
                return cam.IsPositionBehind(godot) ? null : cam.UnprojectPosition(godot);
            }
            Place best = null;
            var bestDistance = PointSnap;
            for (int i = 0; i < outline.Count; i++) {
                var screen = Screen(outline[i].GetPoint());
                if (!screen.HasValue || screen.Value.DistanceTo(mouse) >= bestDistance) continue;
                bestDistance = screen.Value.DistanceTo(mouse);
                best = PointPlace(outline, i);
            }
            if (best != null) return best;
            //on each segment, the position closest to the ray of the mouse
            var ray = GameEngine.Raycast.GetRayFromScreen(cam, mouse);
            bestDistance = SegmentSnap;
            for (int i = 0; i < SegmentCount(outline); i++) {
                var a = outline[i].GetPoint();
                var u = outline[(i + 1) % outline.Count].GetPoint() - a;
                var w = a - ray.origin;
                float uu = u.Dot(u), uv = u.Dot(ray.direction), vv = ray.direction.Dot(ray.direction), uw = u.Dot(w), vw = ray.direction.Dot(w);
                var denominator = uu * vv - uv * uv;
                if (uu < 1e-8f) continue;
                var t = denominator > 1e-8f ? Mathf.Clamp((uv * vw - vv * uw) / denominator, 0, 1) : 0.5f;
                var screen = Screen(a + u * t);
                if (!screen.HasValue || screen.Value.DistanceTo(mouse) >= bestDistance) continue;
                bestDistance = screen.Value.DistanceTo(mouse);
                best = SegmentPlace(outline, i, t);
            }
            return best;
        }

        //the points of the places (in the same order), inserting the ones on a segment with insert(index, position):
        //from the last segment back, so that the indexes of the others stay valid
        public static List<TerrainPoint> Resolve(List<Place> places, System.Func<int, Vector3, TerrainPoint> insert) {
            var res = new TerrainPoint[places.Count];
            var order = new List<int>();
            for (int i = 0; i < places.Count; i++) {
                if (places[i].point != null) res[i] = places[i].point;
                else order.Add(i);
            }
            order.Sort((x, y) => places[y].order.CompareTo(places[x].order));
            foreach (var i in order) res[i] = insert(places[i].segment + 1, places[i].position);
            return new List<TerrainPoint>(res);
        }

        //whether two places of a looping outline of the count of points have points between them on both sides (as the
        //two ends of a cut in two parts with three points or more each)
        public static bool SeparatedOnLoop(Place a, Place b, int count) {
            if (Mathf.Abs(a.order - b.order) < 1e-4f) return false;
            bool PointBetween(float from, float to) {
                var end = to > from ? to : to + count;
                return Mathf.Floor(from) + 1 < end - 1e-4f;
            }
            return PointBetween(a.order, b.order) && PointBetween(b.order, a.order);
        }

        // Clicks

        public override void Apply(List<RaycastHit?> hits) {
            if (!IsActive()) return;
            if (builder.terrainClick.modifier != previous || points?.Invoke() == null) {
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
            if (picked.Count > 0) ActionHandlerManager.ShowMarkerHandle(picked[picked.Count - 1].position, 1.0f, GameEngine.Color.yellow);
            if (UIDetection.OnUI()) return;
            var place = PlaceUnderMouse();
            if (place == null) return;
            var ok = accepts(picked, place);
            if (ok) hovered = place;
            if (picked.Count > 0) ActionHandlerManager.ShowGuideLine(picked[picked.Count - 1].position, place.position);
            ActionHandlerManager.ShowHandle(place.position, 1.0f, ok ? GameEngine.Color.green : GameEngine.Color.red);
        }

        public override List<int> GetLayerMasks() {
            return new List<int> { ~(1 << 3) };
        }

        public override bool SnapsToGrid => false;
    }
}
