using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Operations {
    //The properties of the elements that are not core parameters, but are edited by the editor panels directly
    public static class Builtins {
        public class Property {
            public string name; //a path for nested values, with # in place of the array indices
            public string type; //bool, int, float, string, texture or enum
            public bool instanceSpecific; //stored in the instance state (never part of presets)
            public string[] enumValues;

            public Property(string name, string type, bool instanceSpecific = false, string[] enumValues = null) {
                this.name = name;
                this.type = type;
                this.instanceSpecific = instanceSpecific;
                this.enumValues = enumValues;
            }
        }

        public static readonly string[] CurveTypes = { "bezier", "hermite", "lowPoly" };

        static readonly Dictionary<string, Property[]> properties = new Dictionary<string, Property[]> {
            ["road"] = new[] {
                new Property("projectAll", "bool"),
                new Property("project", "bool"),
                new Property("segmentsPer100m", "bool"),
                new Property("segments", "int"),
                new Property("lpf", "int"),
                new Property("makeCoplanar", "bool"), //only if the road type shows it, see GetProperties
                new Property("adjustLowPolyWidth", "bool"),
                new Property("curveType", "enum", false, CurveTypes),
                new Property("subdivideEqually", "bool"),
                new Property("hermiteTension", "float"),
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
                new Property("projectToGround", "bool"),
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
            ["borderMesh"] = new Property[0],
            ["cityProperties"] = new Property[0],
            ["propContainer"] = new Property[0],
            ["propElement"] = new Property[0],
        };

        public static IEnumerable<string> Kinds => properties.Keys;

        //the properties of an element kind; road: makeCoplanar only for the types that show it
        public static List<Property> GetProperties(string kind, CityElements.Types.Runtime.RoadType roadType = null) {
            var res = new List<Property>();
            if (!properties.TryGetValue(kind, out var list)) return res;
            foreach (var p in list) {
                if (kind == "road" && p.name == "makeCoplanar" && (roadType == null || !roadType.InternalParameterVisible("makeCoplanar"))) continue;
                res.Add(p);
            }
            return res;
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
