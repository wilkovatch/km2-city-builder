using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using GO = GameEngine.GameObject;
using SM = StringManager;

namespace Operations {
    //Operations on the whole city
    public static class City {
        // Rotation

        //rotation: 1 = 90 degrees counterclockwise, 2 = 90 degrees clockwise, 3 = 180 degrees
        public static Quaternion GetRotation(int rotation) {
            var y = 0.0f;
            switch (rotation) {
                case 1:
                    y = -90;
                    break;
                case 2:
                    y = 90;
                    break;
                case 3:
                    y = 180;
                    break;
            }
            return GameEngine.Quaternion.Euler(0, y, 0);
        }

        public static float[,] RotateHeights(float[,] heights, int rotation) {
            var tRes = heights.GetLength(0);
            var heightsRotated = new float[tRes, tRes];
            for (int y = 0; y < tRes; y++) {
                for (int x = 0; x < tRes; x++) {
                    switch (rotation) {
                        case 1: //-90
                            heightsRotated[x, y] = heights[tRes - 1 - y, x];
                            break;
                        case 2: //90
                            heightsRotated[x, y] = heights[y, tRes - 1 - x];
                            break;
                        case 3: //180
                            heightsRotated[x, y] = heights[tRes - 1 - x, tRes - 1 - y];
                            break;
                    }
                }
            }
            return heightsRotated;
        }

        //rotates every element and the terrain around the center of the city (a single undo step)
        public static async Task Rotate(ElementManager manager, int rotation, System.IProgress<(float, string)> progress = null) {
            if (rotation < 1 || rotation > 3) throw new System.ArgumentException("invalid rotation");
            var helper = manager.builder.helper;
            var progressBar = new ProgressReporter(helper.curProgressBar, progress);
            progressBar.SetActive(true);
            progressBar.SetText(SM.Get("ROTATING_CITY"));
            progressBar.SetProgress(0);
            for (int i = 0; i < 2; i++) { //once only does not make the panel close
                await manager.ToSignal(manager.GetTree(), "process_frame");
            }
            History.UndoManager.BeginBusy(SM.Get("ROTATE_CITY"));
            try {
                History.UndoManager.TouchAll();
                var rot = GetRotation(rotation);
                var doneTransforms = new HashSet<Node3D>();
                //positions and rotations go through GO to work in program space and world coordinates
                void MoveTransform(Node3D transform) {
                    if (!doneTransforms.Add(transform)) return;
                    GO.SetPosition(transform, rot * GO.GetPosition(transform));
                }
                void MoveAndRotateTransform(Node3D transform) {
                    if (!doneTransforms.Add(transform)) return;
                    GO.SetPosition(transform, rot * GO.GetPosition(transform));
                    //the city rotates around the world Y axis, so the rotation is applied after the object's own one
                    GO.SetRotation(transform, rot * GO.GetRotationQuaternion(transform));
                }

                foreach (var mesh in manager.meshes) MoveAndRotateTransform(mesh);
                foreach (var road in manager.roads) {
                    foreach (var point in road.points) MoveTransform(point);
                }
                foreach (var intersection in manager.intersections) MoveTransform(intersection.point);
                foreach (var patch in manager.patches) {
                    foreach (var point in patch.GetPerimeterPointsComponents()) MoveTransform(point.GetObject());
                    foreach (var point in patch.GetInternalPointsComponents()) MoveTransform(point.GetObject());
                }
                foreach (var line in manager.buildings) {
                    foreach (var point in line.GetPointsComponents()) MoveTransform(point.GetObject());
                }

                var tRes = CityGroundHelper.heightmapResolution + 1;
                var heights = GameEngine.Terrain.GetHeights(helper.terrainObj, 0, 0, tRes, tRes);
                helper.SetHeights(RotateHeights(heights, rotation));
                History.UndoManager.RecordHeightmapRotation(rotation);
                manager.FlagAsChanged();
            } finally {
                History.UndoManager.EndBusy();
                progressBar.SetActive(false);
            }
        }

        // Whole city generation

        public static void EraseTerrain(ElementManager manager) {
            manager.EraseCityTerrain();
        }

        public static void EraseBuildings(ElementManager manager) {
            manager.EraseCityBuildings();
        }

        // Batch update

        //the element kinds of the batch update, in the order of the editor
        public enum BatchKind {
            Road = 0,
            Intersection = 1,
            TerrainPatch = 2,
            BuildingLine = 3,
            Building = 4,
            BuildingSide = 5,
            MeshInstance = 6
        }

        public class BatchException : System.Exception {
            public enum Part { Update, Where, Runtime }
            public readonly Part part;

            public BatchException(Part part, string message) : base(message) {
                this.part = part;
            }
        }

        class ExpressionTokenStream {
            class ExpressionInputStream {
                int pos = 0;
                string expr;
                public ExpressionInputStream(string expr) { this.expr = expr; }
                public char Next() { return expr[pos++]; }
                public void Back() { pos--; }
                public char Peek() { return expr[pos]; }
                public bool EOF() { return pos >= expr.Length; }
            }
            ExpressionInputStream stream;
            List<char> eqChars = new List<char> { '!', '=' };

            public ExpressionTokenStream(string expr) {
                stream = new ExpressionInputStream(expr);
            }

            void SkipWhitespace() {
                while (!stream.EOF() && stream.Peek() == ' ') {
                    stream.Next();
                }
            }

            string ReadDelimited(System.Func<char, bool> endCondition, bool concatStart, System.Func<char, bool> backCondition = null) {
                var res = "";
                var first = stream.Next();
                if (concatStart) res += first;
                while (!stream.EOF()) {
                    var c = stream.Next();
                    if (endCondition(c)) {
                        if (backCondition != null && backCondition(c)) stream.Back();
                        break;
                    } else {
                        res += c;
                    }
                }
                return res;
            }

            string ReadNext() {
                SkipWhitespace();
                if (stream.EOF()) return "";
                var c = stream.Peek();
                if (c == '"') return ReadDelimited(x => x == '"', false);
                if (eqChars.Contains(c)) return ReadDelimited(x => !eqChars.Contains(x), true, x => true);
                else return ReadDelimited(x => x == ' ' || eqChars.Contains(x), true, x => eqChars.Contains(x));
            }

            public List<string> ReadTokens() {
                var res = new List<string>();
                var cur = ReadNext();
                while (cur != "") {
                    res.Add(cur);
                    cur = ReadNext();
                }
                return res;
            }
        }

        //"field = value and field != value or ...": a list of alternatives (or), each a list of conditions (and).
        //null if invalid, empty if there are no conditions
        public static List<List<(string field, bool equals, string value)>> ParseConditions(string str) {
            var res = new List<List<(string field, bool equals, string value)>>();
            if (str == null || str == "") return res;
            var tokenizer = new ExpressionTokenStream(str);
            var tokens = tokenizer.ReadTokens();
            var operators = new List<string> { "=", "!=", "and", "or" };
            var internalOperators = new List<string> { "=", "!=" };
            int curPartNeeded = 1;
            string field = "", value = "";
            bool curEquals = false;
            var curElem = new List<(string field, bool equals, string value)>();
            for (int i = 0; i < tokens.Count; i++) {
                var lowerToken = tokens[i].ToLower();
                var isOperator = operators.Contains(lowerToken);
                var isInternalOperator = internalOperators.Contains(lowerToken);
                var isAndOr = isOperator && !isInternalOperator;
                switch (curPartNeeded) {
                    case 0: //and/or
                        if (isAndOr) {
                            if (lowerToken == "or") {
                                res.Add(curElem);
                                curElem = new List<(string field, bool equals, string value)>();
                            }
                        } else {
                            return null;
                        }
                        break;
                    case 1: //field
                        field = tokens[i];
                        break;
                    case 2: //= or !=
                        if (isInternalOperator) {
                            curEquals = lowerToken != "!=";
                        } else {
                            return null;
                        }
                        break;
                    case 3: //value
                        value = tokens[i];
                        break;
                }
                curPartNeeded++;
                if (curPartNeeded > 3) {
                    curElem.Add((field, curEquals, value));
                    curPartNeeded = 0;
                }
            }
            if (curPartNeeded != 0) {
                return null;
            } else {
                res.Add(curElem);
            }
            return res;
        }

        //"field = value and field = value": the assignments, null if invalid
        public static List<(string field, string value)> ParseUpdate(string str) {
            var res = ParseConditions(str);
            if (res == null || res.Count != 1) return null;
            var list = new List<(string field, string value)>();
            foreach (var elem in res[0]) {
                if (!elem.equals) return null;
                list.Add((elem.field, elem.value));
            }
            return list;
        }

        static bool ValueEquals(string value, object fieldVal) {
            if (fieldVal is string strFieldVal) return value.Equals(strFieldVal);
            if (fieldVal == null) return false;
            if (int.TryParse(value, out var intVal)) return intVal.Equals(fieldVal);
            if (float.TryParse(value, out var floatVal)) return floatVal.Equals(fieldVal);
            if (value == "True") return fieldVal.Equals(true);
            if (value == "False") return fieldVal.Equals(false);
            return value.Equals(fieldVal);
        }

        static bool CheckConditions(object obj, System.Func<string, string> path, List<List<(string field, bool equals, string value)>> conditions) {
            if (conditions.Count == 0) return true;
            foreach (var orCondition in conditions) {
                var satisfied = true;
                foreach (var andCondition in orCondition) {
                    var fieldEquals = ValueEquals(andCondition.value, FieldStatus.GetValue(obj, path(andCondition.field)));
                    satisfied = satisfied && (andCondition.equals ? fieldEquals : !fieldEquals);
                }
                if (satisfied) return true;
            }
            return false;
        }

        static CityElements.Types.Parameter FindParameter(CityElements.Types.ParameterContainer pc, string name) {
            if (pc?.parameters == null) return null;
            foreach (var p in pc.parameters) {
                if (p.fullName() == name) return p;
            }
            return null;
        }

        static T GetType<T>(Dictionary<string, T> types, string type) where T : class {
            return type != null && types.TryGetValue(type, out var res) ? res : null;
        }

        //where a field of an element is stored, from its type and its kind (instance specific fields are in the instance state)
        static string GetFieldPath(string kind, CityElements.Types.ParameterContainer pc, string field) {
            var builtin = Builtins.Find(kind, field);
            var instance = builtin != null ? builtin.instanceSpecific : (FindParameter(pc, field)?.instanceSpecific ?? false);
            return (instance ? "instanceState" : "state") + ".properties." + field;
        }

        //sets the fields of the elements of a kind satisfying the conditions, returns the number of elements updated
        public static int BatchUpdate(ElementManager m, BatchKind kind, string update, string where) {
            var updateInfo = ParseUpdate(update);
            if (updateInfo == null) throw new BatchException(BatchException.Part.Update, "invalid update expression");
            var whereConditions = ParseConditions(where);
            if (whereConditions == null) throw new BatchException(BatchException.Part.Where, "invalid where expression");
            var count = 0;
            try {
                switch (kind) {
                    case BatchKind.Road:
                        foreach (var road in m.roads) {
                            var pc = GetType(CityElements.Types.Parsers.TypeParser.GetRoadTypes(), road.state.Str("type", null))?.parameterContainer;
                            System.Func<string, string> path = f => GetFieldPath("road", pc, f);
                            if (!CheckConditions(road, path, whereConditions)) continue;
                            History.UndoManager.Touch(road);
                            foreach (var info in updateInfo) FieldStatus.SetValue(road, info.value, path(info.field));
                            count++;
                        }
                        break;
                    case BatchKind.Intersection:
                        foreach (var intersection in m.intersections) {
                            var pc = GetType(CityElements.Types.Parsers.TypeParser.GetIntersectionTypes(), intersection.state.Str("type", null))?.parameterContainer;
                            System.Func<string, string> path = f => GetFieldPath("intersection", pc, f);
                            if (!CheckConditions(intersection, path, whereConditions)) continue;
                            History.UndoManager.Touch(intersection);
                            foreach (var info in updateInfo) FieldStatus.SetValue(intersection, info.value, path(info.field));
                            count++;
                        }
                        break;
                    case BatchKind.TerrainPatch:
                        foreach (var patch in m.patches) {
                            System.Func<string, string> path = f => "state.properties." + f;
                            if (!CheckConditions(patch, path, whereConditions)) continue;
                            History.UndoManager.Touch(patch);
                            foreach (var info in updateInfo) FieldStatus.SetValue(patch, info.value, path(info.field));
                            count++;
                        }
                        break;
                    case BatchKind.BuildingLine:
                        foreach (var line in m.buildings) {
                            var pc = GetType(CityElements.Types.Parsers.TypeParser.GetBuildingLineTypes(), line.state.Str("type", null))?.parameterContainer;
                            System.Func<string, string> path = f => GetFieldPath("buildingLine", pc, f);
                            if (!CheckConditions(line, path, whereConditions)) continue;
                            History.UndoManager.Touch(line);
                            foreach (var info in updateInfo) FieldStatus.SetValue(line, info.value, path(info.field));
                            count++;
                        }
                        break;
                    case BatchKind.Building:
                        foreach (var line in m.buildings) {
                            foreach (var building in line.buildings) {
                                System.Func<string, string> path = f => "State.properties." + f;
                                if (!CheckConditions(building, path, whereConditions)) continue;
                                History.UndoManager.Touch(line);
                                foreach (var info in updateInfo) FieldStatus.SetValue(building, info.value, path(info.field));
                                count++;
                            }
                        }
                        break;
                    case BatchKind.BuildingSide:
                        foreach (var line in m.buildings) {
                            foreach (var building in line.buildings) {
                                foreach (var side in new[] { building.front, building.back, building.left, building.right }) {
                                    System.Func<string, string> path = f => "State.properties." + f;
                                    if (side == null || !CheckConditions(side, path, whereConditions)) continue;
                                    History.UndoManager.Touch(line);
                                    foreach (var info in updateInfo) FieldStatus.SetValue(side, info.value, path(info.field));
                                    count++;
                                }
                            }
                        }
                        break;
                    case BatchKind.MeshInstance:
                        foreach (var info in updateInfo) {
                            if (info.field != "meshPath") throw new BatchException(BatchException.Part.Update, "invalid field for mesh instance, only meshPath can be changed");
                        }
                        //the conditions can use the mesh, the type and the parameters of the type
                        System.Func<string, string> meshPath = f => f == "meshPath" ? f : "settings.properties." + f;
                        foreach (var mesh in new List<MeshInstance>(m.meshes)) {
                            if (!CheckConditions(mesh, meshPath, whereConditions)) continue;
                            foreach (var info in updateInfo) MeshInstance.SetNewMesh(info.value, mesh, m);
                            count++;
                        }
                        break;
                }
            } catch (BatchException) {
                throw;
            } catch (System.Exception e) {
                GameEngine.Print(e.ToString());
                throw new BatchException(BatchException.Part.Runtime, e.Message);
            }
            return count;
        }

        static void AddParameterContainer(CityElements.Types.ParameterContainer pc, List<(string, string)> subRes) {
            if (pc?.parameters == null) return;
            foreach (var p in pc.parameters) {
                var trueName = p.fullName();
                if (p.type == "int" || p.type == "float" || p.type == "string" || p.type == "bool") {
                    subRes.Add((trueName, p.type));
                } else if (p.type == "texture") {
                    subRes.Add((trueName, "string"));
                } else if (p.type == "enum") {
                    subRes.Add((trueName, "int"));
                }
            }
        }

        static void AddBuiltins(string kind, List<(string, string)> subRes, CityElements.Types.Runtime.RoadType roadType = null) {
            foreach (var p in Builtins.GetProperties(kind, roadType)) {
                if (p.name.Contains(".")) continue; //nested values (building sides) can't be batch updated
                var type = p.type == "texture" ? "string" : p.type == "enum" ? "int" : p.type;
                subRes.Add((p.name, type));
            }
        }

        //the fields that can be used in the batch update of a kind, by type of element (name and value type)
        public static Dictionary<string, List<(string name, string type)>> GetBatchParameters(BatchKind kind) {
            var res = new Dictionary<string, List<(string, string)>>();
            switch (kind) {
                case BatchKind.Road:
                    foreach (var t in CityElements.Types.Parsers.TypeParser.GetRoadTypes()) {
                        var subRes = new List<(string, string)>();
                        AddBuiltins("road", subRes, t.Value);
                        AddParameterContainer(t.Value.parameterContainer, subRes);
                        res[t.Key] = subRes;
                    }
                    break;
                case BatchKind.Intersection:
                    foreach (var t in CityElements.Types.Parsers.TypeParser.GetIntersectionTypes()) {
                        var subRes = new List<(string, string)>();
                        AddBuiltins("intersection", subRes);
                        AddParameterContainer(t.Value.parameterContainer, subRes);
                        res[t.Key] = subRes;
                    }
                    break;
                case BatchKind.TerrainPatch:
                    foreach (var t in CityElements.Types.Parsers.TypeParser.GetTerrainPatchTypes()) {
                        var subRes = new List<(string, string)>();
                        AddBuiltins("terrainPatch", subRes);
                        AddParameterContainer(t.Value.parameterContainer, subRes);
                        res[t.Key] = subRes;
                    }
                    break;
                case BatchKind.BuildingLine:
                    foreach (var t in CityElements.Types.Parsers.TypeParser.GetBuildingLineTypes()) {
                        var subRes = new List<(string, string)>();
                        AddBuiltins("buildingLine", subRes);
                        AddParameterContainer(t.Value.parameterContainer, subRes);
                        res[t.Key] = subRes;
                    }
                    break;
                case BatchKind.Building:
                    foreach (var t in CityElements.Types.Parsers.TypeParser.GetBuildingBuildingTypes()) {
                        var subRes = new List<(string, string)>();
                        AddBuiltins("building", subRes);
                        AddParameterContainer(t.Value.parameterContainer, subRes);
                        res[t.Key] = subRes;
                    }
                    break;
                case BatchKind.BuildingSide:
                    foreach (var t in CityElements.Types.Parsers.TypeParser.GetBuildingSideTypes()) {
                        var subRes = new List<(string, string)>();
                        AddBuiltins("buildingSide", subRes);
                        AddParameterContainer(t.Value.parameterContainer, subRes);
                        res[t.Key] = subRes;
                    }
                    break;
                case BatchKind.MeshInstance:
                    foreach (var t in CityElements.Types.Parsers.TypeParser.GetMeshInstanceTypes()) {
                        var subRes = new List<(string, string)> { ("meshPath", "string"), ("type", "string") };
                        AddParameterContainer(t.Value.parameterContainer, subRes);
                        res[t.Key] = subRes;
                    }
                    break;
            }
            return res;
        }
    }
}
