using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Operations {
    //The properties of the elements that are not core parameters, but are edited by the editor panels directly
    public static class Builtins {
        public class Property {
            public string name; //a path for nested values, with # in place of the array indices
            public string type; //bool, int, float, string, texture or enum
            public bool instanceSpecific; //stored in the instance state (never part of presets)
            public string[] enumValues;
            public int[] enumIds; //explicit persisted values when an enum is a filtered registry

            public Property(string name, string type, bool instanceSpecific = false, string[] enumValues = null) {
                this.name = name;
                this.type = type;
                this.instanceSpecific = instanceSpecific;
                this.enumValues = enumValues;
            }
        }

        public static readonly string[] CurveTypes = { "bezier", "hermite", "lowPoly" };

        //the instance specific properties of a road a new road takes from the last road selected (not those about its intersections)
        public static readonly string[] RoadSplineProperties = {
            "projectAll", "project", "segmentsPer100m", "segments", "lpf", "adjustLowPolyWidth", "curveType", "subdivideEqually", "hermiteTension"
        };

        static readonly Dictionary<string, Property[]> properties = new Dictionary<string, Property[]> {
            ["road"] = new[] {
                new Property("projectAll", "bool", true),
                new Property("project", "bool", true),
                new Property("segmentsPer100m", "bool", true),
                new Property("segments", "int", true),
                new Property("lpf", "int", true),
                new Property("adjustLowPolyWidth", "bool", true),
                new Property("curveType", "enum", true, CurveTypes),
                new Property("subdivideEqually", "bool", true),
                new Property("hermiteTension", "float", true),
                new Property("endCrosswalkSize", "float"),
                new Property("startCrosswalkSize", "float"),
                new Property("endIntersectionAdd", "float"),
                new Property("startIntersectionAdd", "float"),
                new Property("endIntersectionTexture", "texture"),
                new Property("startIntersectionTexture", "texture"),
                new Property("endIntersectionEnd", "bool", true),
                new Property("endIntersectionStart", "bool", true),
                new Property("startIntersectionEnd", "bool", true),
                new Property("startIntersectionStart", "bool", true),
                new Property("intersectionMove", "float", true),
                new Property("endConnectionHeight", "float", true),
                new Property("startConnectionHeight", "float", true),
                new Property("endConnectionHeightAuto", "bool", true),
                new Property("startConnectionHeightAuto", "bool", true),
            },
            ["intersection"] = new[] {
                new Property("sidewalkSegments", "int"),
                new Property("sizeIncrease", "float"),
            },
            ["terrainPatch"] = new[] {
                new Property("smooth", "int"),
                new Property("projectToGround", "bool"),
                new Property("texture", "texture"),
            },
            ["buildingLine"] = new[] {
                new Property("projectToGround", "bool", true),
                new Property("invertDirection", "bool"),
                new Property("loop", "bool"),
                new Property("frontOnly", "bool"),
                new Property("height", "float"),
                new Property("roofTex", "texture"),
            },
            ["building"] = new[] {
                new Property("front", "bool"),
                new Property("left", "bool"),
                new Property("right", "bool"),
                new Property("back", "bool"),
                new Property("top", "bool"),
                new Property("topTexture", "texture"),
                new Property("height", "float"),
                new Property("depth", "float"),
                new Property("fixAcuteAngles", "bool"),
            },
            ["buildingSide"] = new[] {
                new Property("hasGroundFloor", "bool"),
                new Property("lowUpperFloorsCount", "int"),
                new Property("highUpperFloorsCount", "int"),
                new Property("groundFloor.minHeight", "float"),
                new Property("groundFloor.Slices.#.Width", "float"),
                new Property("groundFloor.Slices.#.Repetitions", "int"),
                new Property("groundFloor.Slices.#.top", "int"),
                new Property("groundFloor.Slices.#.middle", "int"),
                new Property("groundFloor.Slices.#.bottom", "int"),
                new Property("upperFloors.#.height", "float"),
                new Property("upperFloors.#.Slices.#.Width", "float"),
                new Property("upperFloors.#.Slices.#.Repetitions", "int"),
                new Property("upperFloors.#.Slices.#.block", "int"),
            },
            ["buildingBlock"] = new[] {
                new Property("listName", "string"),
            },
            ["meshInstance"] = new Property[0],
            ["cityProperties"] = new Property[0],
            ["props"] = new Property[0],
        };

        public static IEnumerable<string> Kinds => properties.Keys;

        //the properties of an element kind; road: the curve choices allowed by its type
        public static List<Property> GetProperties(string kind, CityElements.Types.Runtime.RoadType roadType = null) {
            var res = new List<Property>();
            if (!properties.TryGetValue(kind, out var list)) return res;
            foreach (var p in list) {
                if (kind == "road" && roadType != null && p.name == "curveType") {
                    res.Add(new Property("curveType", "enum", true, SplineCurves.Allowed(roadType.typeData.settings).Select(SplineCurves.Name).ToArray()) { enumIds = SplineCurves.Allowed(roadType.typeData.settings) });
                    continue;
                }
                if (kind == "road" && roadType != null && (p.name == "hermiteTension" || p.name == "subdivideEqually") &&
                    !SplineCurves.Allowed(roadType.typeData.settings).Contains(1)) continue;
                res.Add(p);
            }
            return res;
        }

        //whether a property of a kind is instance specific, whatever the type of the element
        public static bool IsInstanceSpecific(string kind, string name) {
            return properties.TryGetValue(kind, out var list) && list.Any(p => p.name == name && p.instanceSpecific);
        }

        //the property matching a name (array indices in the name match the # of the property paths)
        public static Property Find(string kind, string name, CityElements.Types.Runtime.RoadType roadType = null) {
            var pattern = Regex.Replace(name, @"\.\d+(?=\.|$)", ".#");
            foreach (var p in GetProperties(kind, roadType)) {
                if (p.name == pattern) return p;
            }
            return null;
        }
    }
}
