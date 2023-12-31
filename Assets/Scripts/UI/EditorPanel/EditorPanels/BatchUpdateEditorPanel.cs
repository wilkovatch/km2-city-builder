using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SM = StringManager;
using System.Text.RegularExpressions;

namespace EditorPanels {
    public class BatchUpdateEditorPanel : EditorPanel {
        string updateExpr = "";
        string whereExpr = "";
        int curType = 0;

        public override void Initialize(GameObject canvas) {
            Initialize(canvas, 1);
            var p0 = GetPage(0);
            var objTypes = new List<string> {
                SM.Get("BATCH_TYPE_ROAD"),
                SM.Get("BATCH_TYPE_INTERSECTION"),
                SM.Get("BATCH_TYPE_TERRAIN_PATCH"),
                SM.Get("BATCH_TYPE_BUILDING_LINE"),
                SM.Get("BATCH_TYPE_BUILDING"),
                SM.Get("BATCH_TYPE_BUILDING_SIDE"),
                SM.Get("BATCH_TYPE_MESH_INSTANCE")
            };
            p0.AddDropdown(SM.Get("BATCH_TYPE"), objTypes, x => curType = x, 1.5f);
            p0.IncreaseRow();
            p0.AddButton(SM.Get("BATCH_PARAM_LIST"), ShowParameters, 1.5f);
            p0.IncreaseRow();
            p0.AddInputField(SM.Get("BATCH_UPDATE_EXPR"), SM.Get("EXPR_PH"), "", UnityEngine.UI.InputField.ContentType.Standard, x => updateExpr = x, 1.5f);
            p0.IncreaseRow();
            p0.AddInputField(SM.Get("BATCH_WHERE_EXPR"), SM.Get("EXPR_PH"), "", UnityEngine.UI.InputField.ContentType.Standard, x => whereExpr = x, 1.5f);
            p0.IncreaseRow();
            p0.AddButton(SM.Get("BATCH_EXEC"), Exec, 1.5f);
            p0.IncreaseRow();
            p0.AddButton(SM.Get("CLOSE"), Terminate, 1.5f);
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

        List<List<(string field, bool equals, string value)>> GetConditions(string str) {
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
                            if (lowerToken == "!=") {
                                curEquals = false;
                            } else {
                                curEquals = true;
                            }
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

        (bool status, List<(string field, string value)> info) GetUpdateInfo() {
            var res = GetConditions(updateExpr);
            if (res == null || res.Count != 1) return (false, null);
            var list = new List<(string field, string value)>();
            var updates = res[0];
            foreach (var elem in updates) {
                if (!elem.equals) return (false, null);
                list.Add((elem.field, elem.value));
            }
            return (true, list);
        }

        bool CheckConditions(object obj, string prefix, List<List<(string field, bool equals, string value)>> conditions) {
            var ok = conditions.Count == 0; //no conditions => ok, has conditions => check if any satisfied (so start false)
            foreach (var orCondition in conditions) {
                var satisfied = true;
                foreach (var andCondition in orCondition) {
                    bool fieldEquals;
                    var fieldVal = FieldStatus.GetValue(obj, prefix + andCondition.field);
                    if (fieldVal is string strFieldVal) {
                        fieldEquals = andCondition.value.Equals(strFieldVal);
                    } else {
                        if (andCondition.value is string str) {
                            int intVal;
                            float floatVal;
                            if (int.TryParse(str, out intVal)) fieldEquals = intVal.Equals(fieldVal);
                            else if (float.TryParse(str, out floatVal)) fieldEquals = floatVal.Equals(fieldVal);
                            else if (str == "True") fieldEquals = fieldVal.Equals(true);
                            else if (str == "False") fieldEquals = fieldVal.Equals(false);
                            else fieldEquals = andCondition.value.Equals(fieldVal);
                        } else {
                            fieldEquals = andCondition.value.Equals(fieldVal);
                        }
                    }
                    if (andCondition.equals) {
                        satisfied = satisfied && fieldEquals;
                    } else {
                        satisfied = satisfied && !fieldEquals;
                    }
                }
                if (satisfied) {
                    ok = true;
                    break;
                }
            }
            return ok;
        }

        void AddBaseRoadParams(List<(string, string)> subRes, CityElements.Types.Runtime.RoadType t) {
            var elems = new List<(string, string)> {
                ("projectAll", "bool"),
                ("project", "bool"),
                ("segmentsPer100m", "int"),
                ("segments", "int"),
                ("lpf", "int"),
                ("adjustLowPolyWidth", "bool"),
                ("curveType", "int"),
                ("subdivideEqually", "bool"),
                ("endCrosswalkSize", "float"),
                ("startCrosswalkSize", "float"),
                ("endIntersectionAdd", "float"),
                ("startIntersectionAdd", "float"),
                ("entIntersectionTexture", "string"),
                ("startIntersectionTexture", "string"),
                ("endIntersectionEnd", "bool"),
                ("endIntersectionStart", "bool"),
                ("startIntersectionEnd", "bool"),
                ("startIntersectionStart", "bool"),
                ("intersectionMove", "float")
            };
            if (t.InternalParameterVisible("makeCoplanar")) elems.Add(("makeCoplanar", "bool"));
            subRes.AddRange(elems);
        }

        void AddBaseIntersectionParams(List<(string, string)> subRes) {
            var elems = new List<(string, string)> {
                ("sidewalkSegments", "int"),
                ("sizeIncrease", "float")
            };
            subRes.AddRange(elems);
        }

        void AddBaseTerrainPatchParams(List<(string, string)> subRes) {
            var elems = new List<(string, string)> {
                ("smooth", "int"),
                ("projectToGround", "bool"),
                ("texture", "string")
            };
            subRes.AddRange(elems);
        }

        void AddBaseBuildingLineParams(List<(string, string)> subRes) {
            var elems = new List<(string, string)> {
                ("projectToGround", "bool"),
                ("invertDirection", "bool"),
                ("loop", "bool"),
                ("frontOnly", "bool"),
                ("height", "float"),
                ("roofTex", "string")
            };
            subRes.AddRange(elems);
        }

        void AddBaseBuildingParams(List<(string, string)> subRes) {
            var elems = new List<(string, string)> {
                ("front", "bool"),
                ("left", "bool"),
                ("right", "bool"),
                ("back", "bool"),
                ("top", "bool"),
                ("height", "float"),
                ("depth", "float"),
                ("fixAcuteAngles", "bool")
            };
            subRes.AddRange(elems);
        }

        void AddBaseBuildingSideParams(List<(string, string)> subRes) {
            var elems = new List<(string, string)> {
                ("hasGroundFloor", "bool"),
                ("lowUpperFloorsCount", "int"),
                ("highUpperFloorsCount", "int")
            };
            subRes.AddRange(elems);
        }

        void AddBaseMeshInstanceParams(List<(string, string)> subRes) {
            var elems = new List<(string, string)> {
                ("meshPath", "string")
            };
            subRes.AddRange(elems);
        }

        void ProcessParameterContainer(CityElements.Types.ParameterContainer pc, List<(string, string)> subRes) {
            foreach (var p in pc.parameters) {
                var hasContainer = p.container != null && p.container != "";
                var trueName = hasContainer ? (p.container + "_" + p.name) : p.name;
                if (p.type == "int" || p.type == "float" || p.type == "string" || p.type == "bool") {
                    subRes.Add((trueName, p.type));
                } else if (p.type == "texture") {
                    subRes.Add((trueName, "string"));
                } else if (p.type == "enum") {
                    subRes.Add((trueName, "int"));
                }
            }
        }

        Dictionary<string, List<(string, string)>> GetParameters() {
            var res = new Dictionary<string, List<(string, string)>>();

            switch (curType) {
                case 0: //road
                    var roadTypes = CityElements.Types.Parsers.TypeParser.GetRoadTypes();
                    foreach (var type in roadTypes.Keys) {
                        var subRes = new List<(string, string)>();
                        var t = roadTypes[type];
                        AddBaseRoadParams(subRes, t);
                        ProcessParameterContainer(t.parameterContainer, subRes);
                        res[type] = subRes;
                    }
                    break;
                case 1: //intersection
                    var intersectionTypes = CityElements.Types.Parsers.TypeParser.GetIntersectionTypes();
                    foreach (var type in intersectionTypes.Keys) {
                        var subRes = new List<(string, string)>();
                        var t = intersectionTypes[type];
                        AddBaseIntersectionParams(subRes);
                        ProcessParameterContainer(t.parameterContainer, subRes);
                        res[type] = subRes;
                    }
                    break;
                case 2: //terrain patch
                    var terrainPatchTypes = CityElements.Types.Parsers.TypeParser.GetTerrainPatchTypes();
                    foreach (var type in terrainPatchTypes.Keys) {
                        var subRes = new List<(string, string)>();
                        var t = terrainPatchTypes[type];
                        AddBaseTerrainPatchParams(subRes);
                        ProcessParameterContainer(t.parameterContainer, subRes);
                        res[type] = subRes;
                    }
                    break;
                case 3: //building line
                    var buildingLineTypes = CityElements.Types.Parsers.TypeParser.GetBuildingLineTypes();
                    foreach (var type in buildingLineTypes.Keys) {
                        var subRes = new List<(string, string)>();
                        var t = buildingLineTypes[type];
                        AddBaseBuildingLineParams(subRes);
                        ProcessParameterContainer(t.parameterContainer, subRes);
                        res[type] = subRes;
                    }
                    break;
                case 4: //building
                    var buildingTypes = CityElements.Types.Parsers.TypeParser.GetBuildingBuildingTypes();
                    foreach (var type in buildingTypes.Keys) {
                        var subRes = new List<(string, string)>();
                        var t = buildingTypes[type];
                        AddBaseBuildingParams(subRes);
                        ProcessParameterContainer(t.parameterContainer, subRes);
                        res[type] = subRes;
                    }
                    break;
                case 5: //building side
                    var buildingSideTypes = CityElements.Types.Parsers.TypeParser.GetBuildingSideTypes();
                    foreach (var type in buildingSideTypes.Keys) {
                        var subRes = new List<(string, string)>();
                        var t = buildingSideTypes[type];
                        AddBaseBuildingSideParams(subRes);
                        ProcessParameterContainer(t.parameterContainer, subRes);
                        res[type] = subRes;
                    }
                    break;
                case 6: //mesh instance
                    var meshSettings = CityElements.Types.Parsers.TypeParser.GetMeshInstanceSettings();
                    var subRes2 = new List<(string, string)>();
                    AddBaseMeshInstanceParams(subRes2);
                    var t2 = meshSettings.typeData.parametersInfo;
                    ProcessParameterContainer(t2, subRes2);
                    res[""] = subRes2;
                    break;
            }

            return res;
        }

        void ShowParameters() {
            ShowParametersPage(0, 0);
        }

        void ShowParametersNextPage(int page, int num) {
            builder.DoDelayed(delegate { ShowParametersPage(page, num); }); //the previous alert must be closed
        }

        void ShowParametersPage(int page, int num) {
            int height = 600;
            int pageParams = height / 30;
            var parameters = GetParameters();
            if (page >= parameters.Count) return;
            var last = page == parameters.Count - 1;
            var text = "";
            var keys = new List<string>(parameters.Keys);
            var key = keys[page];
            var lst = parameters[key];
            lst.Sort();
            for (int i = num; i < num + pageParams && i < lst.Count; i++) {
                var p = lst[i];
                text += p.Item1 + " (" + p.Item2 + ")\n";
            }
            if (num + pageParams < lst.Count) {
                text += SM.Get("BATCH_PARAM_LIST_CONTINUES") + "\n";
                last = false;
            }
            var title = SM.Get("BATCH_PARAM_LIST_TITLE") + key;
            if (last) {
                builder.CreateAlert(title, text, SM.Get("CLOSE"), null, height, true, 600.0f);
            } else {
                var nextPage = page + 1;
                var nextNum = 0;
                if (num + pageParams < lst.Count) {
                    nextPage = page;
                    nextNum = num + pageParams;
                }
                builder.CreateAlert(title, text, SM.Get("BATCH_NEXT_PAGE"), SM.Get("CLOSE"), delegate { ShowParametersNextPage(nextPage, nextNum); }, null, height, true, 600.0f);
            }
        }

        void Exec() {
            var updateInfo = GetUpdateInfo();
            var whereConditions = GetConditions(whereExpr);
            if (!updateInfo.status) {
                builder.CreateAlert(SM.Get("ERROR"), SM.Get("BATCH_ERROR_UPDATE"), SM.Get("OK"));
            } else if (whereConditions == null) {
                builder.CreateAlert(SM.Get("ERROR"), SM.Get("BATCH_ERROR_WHERE"), SM.Get("OK"));
            } else {
                try {
                    var m = builder.helper.elementManager;
                    switch (curType) {
                        case 0: //road
                            foreach (var road in m.roads) {
                                if (CheckConditions(road, "state.properties.", whereConditions)) {
                                    foreach (var info in updateInfo.info) FieldStatus.SetValue(road, info.value, "state.properties." + info.field);
                                }
                            }
                            break;
                        case 1: //intersection
                            foreach (var intersection in m.intersections) {
                                if (CheckConditions(intersection, "state.properties.", whereConditions)) {
                                    foreach (var info in updateInfo.info) FieldStatus.SetValue(intersection, info.value, "state.properties." + info.field);
                                }
                            }
                            break;
                        case 2: //terrain patch
                            foreach (var patch in m.patches) {
                                if (CheckConditions(patch, "state.properties.", whereConditions)) {
                                    foreach (var info in updateInfo.info) FieldStatus.SetValue(patch, info.value, "state.properties." + info.field);
                                }
                            }
                            break;
                        case 3: //building line
                            foreach (var line in m.buildings) {
                                if (CheckConditions(line, "state.properties.", whereConditions)) {
                                    foreach (var info in updateInfo.info) FieldStatus.SetValue(line, info.value, "state.properties." + info.field);
                                }
                            }
                            break;
                        case 4: //building
                            foreach (var line in m.buildings) {
                                foreach (var building in line.buildings) {
                                    if (CheckConditions(building, "State.properties.", whereConditions)) {
                                        foreach (var info in updateInfo.info) FieldStatus.SetValue(building, info.value, "State.properties." + info.field);
                                    }
                                }
                            }
                            break;
                        case 5: //building side
                            foreach (var line in m.buildings) {
                                foreach (var building in line.buildings) {
                                    foreach (var info in updateInfo.info) {
                                        if (building.front != null && CheckConditions(building.front, "State.properties.", whereConditions)) {
                                            FieldStatus.SetValue(building.front, info.value, "State.properties." + info.field);
                                        }
                                        if (building.back != null && CheckConditions(building.back, "State.properties.", whereConditions)) {
                                            FieldStatus.SetValue(building.back, info.value, "State.properties." + info.field);
                                        }
                                        if (building.left != null && CheckConditions(building.left, "State.properties.", whereConditions)) {
                                            FieldStatus.SetValue(building.left, info.value, "State.properties." + info.field);
                                        }
                                        if (building.right != null && CheckConditions(building.right, "State.properties.", whereConditions)) {
                                            FieldStatus.SetValue(building.right, info.value, "State.properties." + info.field);
                                        }
                                    }
                                }
                            }
                            break;
                        case 6: //mesh instance
                            for (int i = 0; i < m.meshes.Count; i++) {
                                var mesh = m.meshes[i];
                                if (CheckConditions(mesh, "", whereConditions)) {
                                    foreach (var info in updateInfo.info) {
                                        if (info.field == "meshPath") {
                                            MeshInstance.SetNewMesh(info.value, mesh, builder.helper.elementManager);
                                        } else {
                                            throw new System.Exception("invalid field for mesh instance, only meshPath can be changed");
                                        }
                                    }
                                }
                            }
                            break;
                    }
                    builder.NotifyChange();
                    builder.CreateAlert(SM.Get("SUCCESS"), SM.Get("BATCH_SUCCESS"), SM.Get("OK"));
                } catch (System.Exception e) {
                    MonoBehaviour.print(e.ToString());
                    builder.CreateAlert(SM.Get("ERROR"), SM.Get("BATCH_ERROR_RUNTIME"), SM.Get("OK"));
                }
            }
        }
    }
}