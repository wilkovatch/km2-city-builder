using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Godot;
using GO = GameEngine.GameObject;

namespace History {
    //A captured element (JSON in the IO.SavedCity format), compressed when large
    sealed class Memento {
        const int CompressionThreshold = 4096;
        readonly string text;
        readonly byte[] compressed;
        readonly int hash;
        readonly int length;

        public Memento(string json) {
            hash = json.GetHashCode();
            length = json.Length;
            if (json.Length > CompressionThreshold) {
                var res = new MemoryStream();
                using (var stream = new DeflateStream(res, CompressionLevel.Fastest)) {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                    stream.Write(bytes, 0, bytes.Length);
                }
                compressed = res.ToArray();
            } else {
                text = json;
            }
        }

        public string Json {
            get {
                if (text != null) return text;
                var res = new MemoryStream();
                using (var stream = new DeflateStream(new MemoryStream(compressed), CompressionMode.Decompress)) {
                    stream.CopyTo(res);
                }
                return System.Text.Encoding.UTF8.GetString(res.ToArray());
            }
        }

        public long Bytes => text != null ? text.Length * 2L : compressed.Length;

        public bool SameAs(Memento other) {
            if (other == null) return false;
            if (hash != other.hash || length != other.length) return false;
            return Json == other.Json;
        }
    }

    sealed class ElementChange {
        public int id;
        public ElementKind kind;
        public Memento before, after; //null: the element does not exist on that side
        public long Bytes => (before?.Bytes ?? 0) + (after?.Bytes ?? 0) + 32;
    }

    //Changes not tied to an element (terrain heights)
    interface IRawChange {
        void Undo(ElementManager manager);
        void Redo(ElementManager manager);
        long Bytes { get; }
    }

    //The heightmap tiles touched by a brush stroke, before and after
    sealed class HeightmapTilesChange : IRawChange {
        public const int TileSize = 64;
        public readonly Dictionary<(int x, int y), float[,]> before = new Dictionary<(int, int), float[,]>();
        public readonly Dictionary<(int x, int y), float[,]> after = new Dictionary<(int, int), float[,]>();
        //the tiles as a later transaction found them, before the update that finalizes this one
        public readonly Dictionary<(int x, int y), float[,]> afterOverride = new Dictionary<(int, int), float[,]>();

        static (int x, int y, int w, int h) GetTileRect(Node3D terrain, (int x, int y) tile) {
            var res = GameEngine.Terrain.GetHeightmapResolution(terrain);
            var x = tile.x * TileSize;
            var y = tile.y * TileSize;
            return (x, y, Mathf.Min(TileSize, res - x), Mathf.Min(TileSize, res - y));
        }

        public static float[,] ReadTile(Node3D terrain, (int x, int y) tile) {
            var r = GetTileRect(terrain, tile);
            return GameEngine.Terrain.GetHeights(terrain, r.x, r.y, r.w, r.h);
        }

        //returns the tiles captured for the first time
        public List<(int x, int y)> CaptureBefore(Node3D terrain, int xBase, int yBase, int width, int height) {
            var captured = new List<(int x, int y)>();
            var res = GameEngine.Terrain.GetHeightmapResolution(terrain);
            if (res == 0) return captured;
            var x0 = Mathf.Max(0, xBase) / TileSize;
            var y0 = Mathf.Max(0, yBase) / TileSize;
            var x1 = Mathf.Min(res - 1, xBase + width - 1) / TileSize;
            var y1 = Mathf.Min(res - 1, yBase + height - 1) / TileSize;
            for (int ty = y0; ty <= y1; ty++) {
                for (int tx = x0; tx <= x1; tx++) {
                    if (!before.ContainsKey((tx, ty))) {
                        before[(tx, ty)] = ReadTile(terrain, (tx, ty));
                        captured.Add((tx, ty));
                    }
                }
            }
            return captured;
        }

        public void CaptureAfter(Node3D terrain) {
            after.Clear();
            foreach (var tile in before.Keys) {
                after[tile] = afterOverride.TryGetValue(tile, out var data) ? data : ReadTile(terrain, tile);
            }
            var unchanged = new List<(int, int)>();
            foreach (var tile in before.Keys) {
                if (AreEqual(before[tile], after[tile])) unchanged.Add(tile);
            }
            foreach (var tile in unchanged) {
                before.Remove(tile);
                after.Remove(tile);
            }
        }

        static bool AreEqual(float[,] a, float[,] b) {
            if (a.GetLength(0) != b.GetLength(0) || a.GetLength(1) != b.GetLength(1)) return false;
            for (int y = 0; y < a.GetLength(0); y++) {
                for (int x = 0; x < a.GetLength(1); x++) {
                    if (a[y, x] != b[y, x]) return false;
                }
            }
            return true;
        }

        public bool IsEmpty => before.Count == 0;

        //the earlier change keeps its "before" tiles, the later one provides the "after" ones
        public void MergeLater(HeightmapTilesChange later) {
            foreach (var tile in later.before.Keys) {
                if (!before.ContainsKey(tile)) before[tile] = later.before[tile];
                after[tile] = later.after[tile];
            }
        }

        void Write(ElementManager manager, Dictionary<(int x, int y), float[,]> tiles) {
            var terrain = manager.builder.helper.terrainObj;
            foreach (var tile in tiles) {
                GameEngine.Terrain.SetHeightsDelayLOD(terrain, tile.Key.x * TileSize, tile.Key.y * TileSize, tile.Value);
            }
            GameEngine.Terrain.SyncHeightmap(terrain);
        }

        public void Undo(ElementManager manager) => Write(manager, before);
        public void Redo(ElementManager manager) => Write(manager, after);
        public long Bytes => 2L * before.Count * TileSize * TileSize * sizeof(float);
    }

    //The whole heightmap, compressed (for the heightmap import)
    sealed class FullHeightmapChange : IRawChange {
        byte[] before, after;
        float maxHeightBefore, maxHeightAfter;

        static byte[] Compress(byte[] data) {
            var res = new MemoryStream();
            using (var stream = new DeflateStream(res, CompressionLevel.Fastest)) {
                stream.Write(data, 0, data.Length);
            }
            return res.ToArray();
        }

        static float[,] Decompress(byte[] data) {
            var res = new MemoryStream();
            using (var stream = new DeflateStream(new MemoryStream(data), CompressionMode.Decompress)) {
                stream.CopyTo(res);
            }
            var bytes = res.ToArray();
            var size = (int)System.Math.Round(System.Math.Sqrt(bytes.Length / sizeof(float)));
            var heights = new float[size, size];
            System.Buffer.BlockCopy(bytes, 0, heights, 0, bytes.Length);
            return heights;
        }

        public static FullHeightmapChange CaptureBefore(Node3D terrain) {
            return new FullHeightmapChange { before = Compress(GameEngine.Terrain.GetHeightBytes(terrain)), maxHeightBefore = CityGroundHelper.maxHeight };
        }

        public void CaptureAfter(Node3D terrain) {
            after = Compress(GameEngine.Terrain.GetHeightBytes(terrain));
            maxHeightAfter = CityGroundHelper.maxHeight;
        }

        void Write(ElementManager manager, byte[] data, float maxHeight) {
            if (data == null) return;
            CityGroundHelper.maxHeight = maxHeight;
            GameEngine.Terrain.SetHeightmap(manager.builder.helper.terrainObj, maxHeight, Decompress(data));
        }

        public void Undo(ElementManager manager) => Write(manager, before, maxHeightBefore);
        public void Redo(ElementManager manager) => Write(manager, after, maxHeightAfter);
        public long Bytes => (before?.Length ?? 0) + (after?.Length ?? 0);
    }

    //The city rotation turns the heightmap by a multiple of 90 degrees, which is undone by turning it back
    sealed class HeightmapRotationChange : IRawChange {
        readonly int rotation;

        public HeightmapRotationChange(int rotation) {
            this.rotation = rotation;
        }

        static int Inverse(int rotation) {
            switch (rotation) {
                case 1: return 2;
                case 2: return 1;
                default: return rotation;
            }
        }

        void Rotate(ElementManager manager, int rot) {
            var helper = manager.builder.helper;
            var tRes = CityGroundHelper.heightmapResolution + 1;
            var heights = GameEngine.Terrain.GetHeights(helper.terrainObj, 0, 0, tRes, tRes);
            helper.SetHeights(Operations.City.RotateHeights(heights, rot));
        }

        public void Undo(ElementManager manager) => Rotate(manager, Inverse(rotation));
        public void Redo(ElementManager manager) => Rotate(manager, rotation);
        public long Bytes => 16;
    }

    sealed class HistoryEntry {
        public string label;
        public object coalesceKey;
        public readonly Dictionary<int, ElementChange> elements = new Dictionary<int, ElementChange>();
        public HeightmapTilesChange tiles;
        public readonly List<IRawChange> raw = new List<IRawChange>();

        public bool IsEmpty => elements.Count == 0 && (tiles == null || tiles.IsEmpty) && raw.Count == 0;

        public long Bytes {
            get {
                long res = 64;
                foreach (var c in elements.Values) res += c.Bytes;
                if (tiles != null) res += tiles.Bytes;
                foreach (var r in raw) res += r.Bytes;
                return res;
            }
        }

        //appends a later entry: the element states before it come from this one, the ones after it from the later one
        public void MergeLater(HistoryEntry later) {
            foreach (var change in later.elements.Values) {
                if (elements.TryGetValue(change.id, out var existing)) {
                    existing.after = change.after;
                    if ((existing.before == null && existing.after == null) || (existing.before != null && existing.before.SameAs(existing.after))) {
                        elements.Remove(change.id);
                    }
                } else {
                    elements[change.id] = change;
                }
            }
            if (later.tiles != null) {
                if (tiles == null) tiles = later.tiles;
                else tiles.MergeLater(later.tiles);
            }
            raw.AddRange(later.raw);
        }
    }

    sealed class OpenTransaction {
        public string label;
        public object coalesceKey;
        public readonly Dictionary<int, (ElementKind kind, Memento before)> touched = new Dictionary<int, (ElementKind, Memento)>();
        public HeightmapTilesChange tiles;
        public readonly List<IRawChange> raw = new List<IRawChange>();
        public FullHeightmapChange fullHeightmap;
        public Dictionary<Node3D, List<TerrainPoint>> anchoredPoints; //built when first needed
        //the elements as a later transaction found them (when it touched them before the update that finalizes this one)
        public readonly Dictionary<int, Memento> afterOverride = new Dictionary<int, Memento>();
    }

    //Records the changes made to the city in transactions (one per user action) and undoes/redoes them.
    //Only the elements touched by an action are captured, before and after it, so the memory used depends on
    //what was changed and not on the size of the city.
    public static class UndoManager {
        public static ElementManager manager;
        public static int maxEntries = 100;
        public static long maxBytes = 64L * 1024 * 1024;

        static readonly List<HistoryEntry> undoStack = new List<HistoryEntry>();
        static readonly List<HistoryEntry> redoStack = new List<HistoryEntry>();
        static OpenTransaction current = null;
        static int depth = 0;
        //committed transactions, finalized after the next update so that what it derives (projection on the ground,
        //regenerated anchors...) is recorded too
        static readonly List<OpenTransaction> queued = new List<OpenTransaction>();
        static bool applying = false;
        static int suppressed = 0;
        static int busy = 0;
        static bool canCoalesceTop = false;
        static bool loading = false;
        static HistoryEntry lastApplied = null;

        //the undo steps above the marker of an open group become a single step when the group is closed
        static int groupMarker = -1;
        static string groupLabel = null;

        public static bool CanUndo => undoStack.Count > 0 && !IsBusy && !IsGroupOpen;
        //changes every time an undo step is added, to know if an action recorded one
        public static int Version { get; private set; } = 0;
        //changes every time an undo step is undone or redone (what the elements derive from others must not be computed
        //again from the restored state: the step restored it too)
        public static int AppliedVersion { get; private set; } = 0;
        public static bool CanRedo => redoStack.Count > 0 && !IsBusy && !IsGroupOpen;
        public static bool IsGroupOpen => groupMarker >= 0;
        public static string GroupLabel => groupLabel;
        public static bool IsBusy => busy > 0 || applying || depth > 0;
        public static bool IsLoading => loading;

        static bool Recording => current != null && !applying && suppressed == 0 && !loading;

        // Transactions

        public static void Begin(string label, object coalesceKey = null) {
            if (applying) return;
            if (depth == 0) current = new OpenTransaction { label = label, coalesceKey = coalesceKey };
            depth++;
        }

        public static void Commit() {
            if (applying || depth == 0) return;
            depth--;
            if (depth == 0) {
                if (current != null) queued.Add(current);
                current = null;
            }
        }

        //runs an action in a transaction
        public static void Record(string label, System.Action action, object coalesceKey = null) {
            Begin(label, coalesceKey);
            try {
                action.Invoke();
            } finally {
                Commit();
            }
        }

        //runs a change derived by the update from the last action (e.g. points computed from the elements it edited) in
        //the transaction of that action, committed but not finalized yet (or in the open one): undoing the action undoes it
        //too. Without one (nothing recorded) it just runs
        public static void RecordDerived(System.Action action) {
            if (current != null || applying || queued.Count == 0) {
                action.Invoke();
                return;
            }
            var t = queued[queued.Count - 1];
            queued.RemoveAt(queued.Count - 1); //not an earlier transaction for the elements it touches
            current = t;
            try {
                action.Invoke();
            } finally {
                current = null;
                queued.Add(t);
            }
        }

        //async operations (whole city generation, rotation...) keep undo and redo disabled while running
        public static void BeginBusy(string label) {
            busy++;
            Begin(label);
        }

        public static void EndBusy() {
            if (busy > 0) busy--;
            Commit();
        }

        //changes made while suppressed are not recorded (city loading)
        public static void Suppress(bool suppress) {
            suppressed = Mathf.Max(0, suppressed + (suppress ? 1 : -1));
        }

        //nothing is recorded while a city is being loaded (the history is cleared at the end)
        public static void SetLoading(bool loading) {
            UndoManager.loading = loading;
        }

        public static void Touch(object element) {
            Touch(element, true);
        }

        static void Touch(object element, bool withAnchoredPoints) {
            if (!Recording || element == null) return;
            var kind = ElementSnapshot.GetKind(element);
            if (!kind.HasValue) return;
            var id = ElementRegistry.GetId(element);
            if (id < 0 || current.touched.ContainsKey(id)) return;
            var alive = ElementSnapshot.IsAlive(element);
            Memento before = null;
            if (alive) {
                try {
                    before = new Memento(ElementSnapshot.Capture(element, kind.Value, id));
                } catch (System.Exception e) {
                    GameEngine.LogError("Undo: could not capture element " + id + " (" + kind.Value + "): " + e);
                    return;
                }
            }
            current.touched[id] = (kind.Value, before);
            for (int i = queued.Count - 1; i >= 0; i--) {
                if (!queued[i].touched.ContainsKey(id)) continue;
                if (!queued[i].afterOverride.ContainsKey(id)) queued[i].afterOverride[id] = before;
                break;
            }
            if (alive && withAnchoredPoints && (kind == ElementKind.Road || kind == ElementKind.Intersection || kind == ElementKind.TerrainPoint)) TouchAnchoredPoints(element);
        }

        static IEnumerable<Node3D> GetAnchorOwners(TerrainPoint point) {
            if (point.IsDeleted() || GO.IsNull(point.anchor) || point.anchor.IsDeleted()) yield break;
            if (point.anchor is PointAnchor fixedPoint) {
                yield return fixedPoint.master.GetObject();
            } else if (point.anchor is ColumnAnchor column) {
                yield return column.master.GetObject();
            } else if (point.anchor is TerrainAnchor ta) {
                yield return GO.GetParent(ta.GetObject());
            } else if (point.anchor is LineAnchor line) {
                foreach (var node in new[] { line.start, line.end }) {
                    if (node == null || !GodotObject.IsInstanceValid(node)) continue;
                    yield return GameEngine.Scriptable.FindRecursive<TerrainPoint>(node) != null ? node : GO.GetParent(node);
                }
            }
        }

        //Capture followers too: deleting a source or regenerating its anchors can clear their attachment.
        static void TouchAnchoredPoints(object owner) {
            var ownerNode = owner is Road road ? road : owner is TerrainPoint pointOwner ? pointOwner.GetObject() : (owner as Intersection)?.geo;
            if (ownerNode == null) return;
            if (current.anchoredPoints == null) {
                current.anchoredPoints = new Dictionary<Node3D, List<TerrainPoint>>();
                foreach (var point in manager.GetTerrainPoints()) {
                    foreach (var anchorOwner in GetAnchorOwners(point)) {
                        if (anchorOwner == null) continue;
                        if (!current.anchoredPoints.TryGetValue(anchorOwner, out var list)) {
                            list = new List<TerrainPoint>();
                            current.anchoredPoints[anchorOwner] = list;
                        }
                        if (!list.Contains(point)) list.Add(point);
                    }
                }
            }
            if (current.anchoredPoints.TryGetValue(ownerNode, out var points)) {
                foreach (var point in points) Touch(point, true);
            }
        }

        //the element a selectable node belongs to (control points, intersection points, meshes, parametric objects)
        public static object ResolveOwner(Node3D node) {
            if (node == null || !GodotObject.IsInstanceValid(node)) return null;
            var point = GameEngine.Scriptable.FindRecursive<TerrainPoint>(node);
            if (point != null && point.GetObject() == node) return point;
            var intersection = GameEngine.Scriptable.FindRecursive<Intersection.IntersectionComponent>(node);
            if (intersection != null && GO.GetParent(intersection) == node) return intersection.intersection;
            var mesh = GameEngine.Scriptable.Cast<MeshInstance>(node);
            if (mesh != null && mesh.objectParameter == null) return mesh;
            //road control points and parametric objects: the first road or city properties among the ancestors (the move
            //handle of a low-level container: the container)
            for (var cur = node; cur != null; cur = cur.GetParentNode3D()) {
                if (cur is Road || cur is CityProperties || cur is LowLevelElement) return cur;
            }
            return null;
        }

        public static void TouchOwner(Node3D node) {
            var owner = ResolveOwner(node);
            Touch(owner);
            //the end of a road on an intersection moves the intersection too
            if (owner is Road road && road.points.Count > 0) {
                if (road.points[0] == node) Touch(road.startIntersection);
                if (road.points[road.points.Count - 1] == node) Touch(road.endIntersection);
            }
        }

        public static void NotifyCreated(object element, int id) {
            if (!Recording) return;
            var kind = ElementSnapshot.GetKind(element);
            if (!kind.HasValue || current.touched.ContainsKey(id)) return;
            current.touched[id] = (kind.Value, null);
        }

        public static void TouchHeights(int xBase, int yBase, int width, int height) {
            if (!Recording) return;
            var terrain = manager?.builder?.helper?.terrainObj;
            if (terrain == null) return;
            if (current.tiles == null) current.tiles = new HeightmapTilesChange();
            foreach (var tile in current.tiles.CaptureBefore(terrain, xBase, yBase, width, height)) {
                for (int i = queued.Count - 1; i >= 0; i--) {
                    var tiles = queued[i].tiles;
                    if (tiles == null || !tiles.before.ContainsKey(tile)) continue;
                    if (!tiles.afterOverride.ContainsKey(tile)) tiles.afterOverride[tile] = current.tiles.before[tile];
                    break;
                }
            }
        }

        public static void TouchWholeHeightmap() {
            if (!Recording || current.fullHeightmap != null) return;
            var terrain = manager?.builder?.helper?.terrainObj;
            if (terrain == null) return;
            try {
                current.fullHeightmap = FullHeightmapChange.CaptureBefore(terrain);
            } catch (System.Exception e) {
                GameEngine.LogError("Undo: could not capture the heightmap: " + e);
            }
        }

        public static void RecordHeightmapRotation(int rotation) {
            if (!Recording) return;
            current.raw.Add(new HeightmapRotationChange(rotation));
        }

        //touches every element of the city (for the operations changing all of it)
        public static void TouchAll() {
            if (!Recording) return;
            foreach (var mesh in manager.meshes) Touch(mesh, false);
            foreach (var road in manager.roads) Touch(road, false);
            foreach (var intersection in manager.intersections) Touch(intersection, false);
            foreach (var point in manager.GetTerrainPoints()) Touch(point, false);
            foreach (var patch in manager.patches) Touch(patch, false);
            foreach (var line in manager.buildings) Touch(line, false);
            foreach (var element in manager.lowLevelElements) Touch(element, false);
        }

        public static void FinalizePending() {
            FinalizePending(false);
        }

        static void FinalizePending(bool force) {
            if (queued.Count == 0 || applying || (depth > 0 && !force)) return;
            var transactions = new List<OpenTransaction>(queued);
            queued.Clear();
            foreach (var t in transactions) {
                var entry = BuildEntry(t);
                if (!entry.IsEmpty) Push(entry);
            }
        }

        static HistoryEntry BuildEntry(OpenTransaction t) {
            var entry = new HistoryEntry { label = t.label, coalesceKey = t.coalesceKey };
            //the deleted elements are unregistered first, so that the elements referencing them don't anymore
            foreach (var id in t.touched.Keys) {
                var element = ElementRegistry.Get(id);
                if (element != null && !ElementSnapshot.IsAlive(element)) ElementRegistry.Unregister(element);
            }
            foreach (var touch in t.touched) {
                var id = touch.Key;
                var element = ElementRegistry.Get(id);
                Memento after = null;
                if (t.afterOverride.TryGetValue(id, out var overridden)) {
                    after = overridden;
                } else if (element != null) {
                    try {
                        after = new Memento(ElementSnapshot.Capture(element, touch.Value.kind, id));
                    } catch (System.Exception e) {
                        GameEngine.LogError("Undo: could not capture element " + id + " (" + touch.Value.kind + "): " + e);
                        continue;
                    }
                }
                var before = touch.Value.before;
                if (before == null && after == null) continue;
                if (before != null && before.SameAs(after)) continue;
                entry.elements[id] = new ElementChange { id = id, kind = touch.Value.kind, before = before, after = after };
            }
            var terrain = manager?.builder?.helper?.terrainObj;
            if (t.tiles != null && terrain != null) {
                t.tiles.CaptureAfter(terrain);
                if (!t.tiles.IsEmpty) entry.tiles = t.tiles;
            }
            entry.raw.AddRange(t.raw);
            if (t.fullHeightmap != null && terrain != null) {
                try {
                    t.fullHeightmap.CaptureAfter(terrain);
                    entry.raw.Add(t.fullHeightmap);
                } catch (System.Exception e) {
                    GameEngine.LogError("Undo: could not capture the heightmap: " + e);
                }
            }
            return entry;
        }

        static void Push(HistoryEntry entry) {
            var top = undoStack.Count > 0 ? undoStack[undoStack.Count - 1] : null;
            redoStack.Clear();
            if (top != null && canCoalesceTop && entry.coalesceKey != null && Equals(top.coalesceKey, entry.coalesceKey)) {
                top.MergeLater(entry);
                if (top.IsEmpty) undoStack.RemoveAt(undoStack.Count - 1);
            } else {
                undoStack.Add(entry);
            }
            canCoalesceTop = true;
            Version++;
            Trim();
        }

        static void Trim() {
            if (IsGroupOpen) return; //the steps of the group must stay until it is closed
            long total = 0;
            foreach (var e in undoStack) total += e.Bytes;
            foreach (var e in redoStack) total += e.Bytes;
            while (undoStack.Count > 1 && (undoStack.Count > maxEntries || total > maxBytes)) {
                total -= undoStack[0].Bytes;
                undoStack.RemoveAt(0);
            }
        }

        //records the changes made by the action (closing the editing panels before an undo, which can delete
        //an element left incomplete) into the last entry. Returns true if that consumed the last entry, or if
        //there was no entry to amend, meaning that the undo itself must not happen.
        public static bool AmendLast(System.Action action) {
            lastApplied = null;
            if (IsBusy) return true;
            FinalizePending(true);
            Begin("");
            var t = current;
            try {
                action.Invoke();
            } finally {
                depth = 0;
                current = null;
                queued.Clear(); //the nested transactions are part of this one
            }
            if (t == null) return true; //the history was cleared
            var entry = BuildEntry(t);
            if (entry.IsEmpty) return false;
            if (undoStack.Count == 0) return true;
            var top = undoStack[undoStack.Count - 1];
            top.MergeLater(entry);
            if (top.IsEmpty) {
                undoStack.RemoveAt(undoStack.Count - 1);
                return true;
            }
            return false;
        }

        public static void Undo() {
            if (IsBusy) return;
            FinalizePending(true);
            if (undoStack.Count == 0) return;
            var entry = undoStack[undoStack.Count - 1];
            undoStack.RemoveAt(undoStack.Count - 1);
            Apply(entry, true);
            lastApplied = entry;
            redoStack.Add(entry);
            canCoalesceTop = false;
        }

        public static void Redo() {
            if (IsBusy) return;
            FinalizePending(true);
            if (redoStack.Count == 0) return;
            var entry = redoStack[redoStack.Count - 1];
            redoStack.RemoveAt(redoStack.Count - 1);
            Apply(entry, false);
            lastApplied = entry;
            undoStack.Add(entry);
            canCoalesceTop = false;
        }

        //undoes the last step and discards it (e.g. a script command that failed halfway)
        public static void UndoAndForget() {
            if (IsBusy) return;
            FinalizePending(true);
            if (undoStack.Count == 0) return;
            var entry = undoStack[undoStack.Count - 1];
            undoStack.RemoveAt(undoStack.Count - 1);
            Apply(entry, true);
            canCoalesceTop = false;
            Version++;
        }

        // Groups: several actions (e.g. the commands of a script) that become a single undo step. Each action keeps its
        // own step while the group is open, so that a failed one can still be undone alone (UndoAndForget).

        public static void OpenGroup(string label) {
            FinalizePending(true);
            groupMarker = undoStack.Count;
            groupLabel = label;
            canCoalesceTop = false; //the steps of the group must not merge with the one before it
        }

        //merges the steps recorded since the group was opened into one
        public static void CloseGroup() {
            if (!IsGroupOpen) return;
            FinalizePending(true);
            var marker = groupMarker;
            groupMarker = -1;
            if (undoStack.Count > marker + 1) {
                var merged = undoStack[marker];
                for (int i = marker + 1; i < undoStack.Count; i++) merged.MergeLater(undoStack[i]);
                undoStack.RemoveRange(marker, undoStack.Count - marker);
                if (!merged.IsEmpty) undoStack.Add(merged);
                Version++;
            }
            if (undoStack.Count > marker) {
                undoStack[undoStack.Count - 1].label = groupLabel;
                undoStack[undoStack.Count - 1].coalesceKey = null;
            }
            groupLabel = null;
            canCoalesceTop = false;
            Trim();
        }

        //undoes and discards the steps recorded since the group was opened
        public static void DiscardGroup() {
            if (!IsGroupOpen) return;
            FinalizePending(true);
            var marker = groupMarker;
            groupMarker = -1;
            groupLabel = null;
            while (undoStack.Count > marker && !IsBusy) UndoAndForget();
            Trim();
        }

        public static void Clear() {
            groupMarker = -1;
            groupLabel = null;
            undoStack.Clear();
            redoStack.Clear();
            current = null;
            depth = 0;
            queued.Clear();
            busy = 0;
            canCoalesceTop = false;
            loading = false;
            lastApplied = null;
        }

        //true if the last undo/redo restored an element in the state it only has while being created (e.g. a road
        //with a single point), which closing its editor deletes: the undo/redo then continues to the next step
        public static bool LastAppliedLeftIncompleteElements() {
            if (lastApplied == null) return false;
            foreach (var id in lastApplied.elements.Keys) {
                if (IsIncomplete(GetAliveElement(id))) return true;
            }
            return false;
        }

        //an element in the state it only has while being created, which closing its editor deletes
        public static bool IsIncomplete(object element) {
            if (element is Road road) return road.points.Count < 2;
            if (element is TerrainPatch patch) return patch.GetPerimeterPointCount() < 3;
            if (element is BuildingLine line) return line.GetPointCount() < 2;
            return false;
        }

        // Application

        static readonly ElementKind[] removalOrder = {
            ElementKind.LowLevel, ElementKind.BuildingLine, ElementKind.TerrainPatch, ElementKind.TerrainPoint,
            ElementKind.Intersection, ElementKind.Road, ElementKind.Mesh
        };

        public static object GetAliveElement(int id) {
            var element = ElementRegistry.Get(id);
            return element != null && ElementSnapshot.IsAlive(element) ? element : null;
        }

        static void ApplyKinds(List<(ElementChange change, Memento target)> changes, params ElementKind[] kinds) {
            var kindSet = new HashSet<ElementKind>(kinds);
            //create the missing elements first, since the others can reference them
            foreach (var (change, target) in changes) {
                if (!kindSet.Contains(change.kind) || change.kind == ElementKind.CityProperties || GetAliveElement(change.id) != null) continue;
                var element = ElementRegistry.Get(change.id);
                if (element != null) ElementRegistry.Unregister(element);
                try {
                    ElementSnapshot.CreateSkeleton(change.kind, change.id, target.Json, manager);
                } catch (System.Exception e) {
                    GameEngine.LogError("Undo: error when recreating element " + change.id + " (" + change.kind + "): " + e);
                }
            }
            foreach (var (change, target) in changes) {
                if (!kindSet.Contains(change.kind)) continue;
                var element = change.kind == ElementKind.CityProperties ? manager.GetDummy<CityProperties>() : GetAliveElement(change.id);
                if (element == null) {
                    GameEngine.LogWarning("Undo: element " + change.id + " (" + change.kind + ") could not be restored");
                    continue;
                }
                try {
                    ElementSnapshot.Apply(element, change.kind, target.Json, manager);
                } catch (System.Exception e) {
                    GameEngine.LogError("Undo: error when restoring element " + change.id + " (" + change.kind + "): " + e);
                }
            }
        }

        static void Apply(HistoryEntry entry, bool undo) {
            applying = true;
            AppliedVersion++;
            try {
                //terrain first, so that the elements projected on it follow
                if (undo) {
                    if (entry.tiles != null) entry.tiles.Undo(manager);
                    for (int i = entry.raw.Count - 1; i >= 0; i--) entry.raw[i].Undo(manager);
                } else {
                    if (entry.tiles != null) entry.tiles.Redo(manager);
                    foreach (var r in entry.raw) r.Redo(manager);
                }

                var removals = new List<ElementChange>();
                var updates = new List<(ElementChange, Memento)>();
                foreach (var change in entry.elements.Values) {
                    var target = undo ? change.before : change.after;
                    if (target == null) removals.Add(change);
                    else updates.Add((change, target));
                }

                foreach (var kind in removalOrder) {
                    foreach (var change in removals) {
                        if (change.kind != kind) continue;
                        var element = GetAliveElement(change.id);
                        if (element != null) ElementSnapshot.Remove(element, kind);
                        var registered = ElementRegistry.Get(change.id);
                        if (registered != null) ElementRegistry.Unregister(registered);
                    }
                }

                //same order as the city decoder: the terrain points need the anchors generated with the roads
                ApplyKinds(updates, ElementKind.CityProperties, ElementKind.Mesh, ElementKind.Road, ElementKind.Intersection);
                var hasTerrainElements = false;
                foreach (var (change, _) in updates) {
                    if (change.kind == ElementKind.TerrainPoint || change.kind == ElementKind.TerrainPatch || change.kind == ElementKind.BuildingLine
                        || change.kind == ElementKind.LowLevel) {
                        hasTerrainElements = true;
                        break;
                    }
                }
                if (hasTerrainElements) {
                    manager.worldChanged = true;
                    try {
                        manager.ProcessUpdate();
                    } catch (System.Exception e) {
                        GameEngine.LogError("Undo: error when updating the city: " + e);
                    }
                }
                ApplyKinds(updates, ElementKind.TerrainPoint);
                ApplyKinds(updates, ElementKind.TerrainPatch, ElementKind.BuildingLine, ElementKind.LowLevel);
            } finally {
                applying = false;
            }
            manager.FlagAsChanged(true);
        }
    }
}
