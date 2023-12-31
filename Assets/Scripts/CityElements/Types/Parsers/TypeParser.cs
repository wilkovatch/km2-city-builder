using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CityElements.Types.Parsers {
    public class TypeParser {

        //Common methods
        static string GetElementsDir() {
            var core = PreferencesManager.Get("core", "");
            if (core == "") return null;
            return Path.Combine(PathHelper.BasePath(), "Files", "cores", core, "elements");
        }

        static Dictionary<string, T> GetTypes<T>(ref Dictionary<string, T> res, System.Action<string> func, bool reload = false) {
            if (res != null && !reload) return res;
            var elementsDir = GetElementsDir();
            if (elementsDir == null) return new Dictionary<string, T>();
            if (res == null || reload) {
                res = new Dictionary<string, T>();
                func.Invoke(elementsDir);
                return res;
            }
            return res;
        }



        //Roads
        static Dictionary<string, Runtime.RoadType> roadTypes;
        public static Dictionary<string, Runtime.RoadType> GetRoadTypes(bool reload = false) {
            return GetTypes(ref roadTypes, elementsDir => {
                var dirs = Directory.GetDirectories(Path.Combine(elementsDir, "roads"));
                var commonDir = Path.Combine(elementsDir, "sharedComponents");
                foreach (var dir in dirs) {
                    var tr = new RoadType(dir, commonDir);
                    var key = Path.GetFileName(dir);
                    roadTypes.Add(key, new Runtime.RoadType(tr, key));
                }
            }, reload);
        }



        //Props
        static Dictionary<string, Runtime.PropsElementType> propsTypes;
        public static Dictionary<string, Runtime.PropsElementType> GetPropsElementTypes(bool reload = false) {
            return GetTypes(ref propsTypes, elementsDir => {
                var files = Directory.GetFiles(Path.Combine(elementsDir, "props", "elements"));
                foreach (var file in files) {
                    var content = File.ReadAllText(file);
                    var tp = Newtonsoft.Json.JsonConvert.DeserializeObject<PropsElementType>(content);
                    var key = Path.GetFileNameWithoutExtension(file);
                    propsTypes.Add(key, new Runtime.PropsElementType(tp, key));
                }
            }, reload);
        }

        static Dictionary<string, PropsContainerType> propsContainersTypes;
        public static Dictionary<string, PropsContainerType> GetPropsContainersTypes(bool reload = false) {
            return GetTypes(ref propsContainersTypes, elementsDir => {
                var files = Directory.GetFiles(Path.Combine(elementsDir, "props", "containers"));
                foreach (var file in files) {
                    var content = File.ReadAllText(file);
                    var tp = Newtonsoft.Json.JsonConvert.DeserializeObject<PropsContainerType>(content);
                    propsContainersTypes.Add(Path.GetFileNameWithoutExtension(file), tp);
                }
            }, reload);
        }



        //Terrain patchces
        static Dictionary<string, Runtime.RoadType> terrainPatchBorderMeshTypes;
        public static Dictionary<string, Runtime.RoadType> GetTerrainPatchBorderMeshTypes(bool reload = false) {
            return GetTypes(ref terrainPatchBorderMeshTypes, elementsDir => {
                var dirs = Directory.GetDirectories(Path.Combine(elementsDir, "terrainPatches", "borderMeshes"));
                var commonDir = Path.Combine(elementsDir, "sharedComponents");
                foreach (var dir in dirs) {
                    var tr = new RoadType(dir, commonDir);
                    var key = Path.GetFileName(dir);
                    terrainPatchBorderMeshTypes.Add(key, new Runtime.RoadType(tr, key));
                }
            }, reload);
        }

        static Dictionary<string, Runtime.TerrainPatchType> terrainPatchTypes;
        public static Dictionary<string, Runtime.TerrainPatchType> GetTerrainPatchTypes(bool reload = false) {
            return GetTypes(ref terrainPatchTypes, elementsDir => {
                var dirs = Directory.GetDirectories(Path.Combine(elementsDir, "terrainPatches", "terrainPatches"));
                foreach (var dir in dirs) {
                    var tr = new TerrainPatchType(dir);
                    var key = Path.GetFileName(dir);
                    terrainPatchTypes.Add(key, new Runtime.TerrainPatchType(tr, key));
                }
            }, reload);
        }



        //Intersections
        static Dictionary<string, Runtime.IntersectionType> intersectionTypes;
        public static Dictionary<string, Runtime.IntersectionType> GetIntersectionTypes(bool reload = false) {
            return GetTypes(ref intersectionTypes, elementsDir => {
                var dirs = Directory.GetDirectories(Path.Combine(elementsDir, "intersections"));
                var commonDir = Path.Combine(elementsDir, "sharedComponents");
                foreach (var dir in dirs) {
                    var tr = new IntersectionType(dir, commonDir);
                    var key = Path.GetFileName(dir);
                    intersectionTypes.Add(key, new Runtime.IntersectionType(tr, key));
                }
            }, reload);
        }



        //Buildings
        static Dictionary<string, Runtime.Buildings.BuildingType> buildingTypes;
        public static Dictionary<string, Runtime.Buildings.BuildingType> GetBuildingTypes(bool reload = false) {
            return GetTypes(ref buildingTypes, elementsDir => {
                var dirs = Directory.GetDirectories(Path.Combine(elementsDir, "buildings"));
                foreach (var dir in dirs) {
                    var tr = new Buildings.BuildingType(dir);
                    var key = Path.GetFileName(dir);
                    buildingTypes.Add(key, new Runtime.Buildings.BuildingType(tr, key));
                }
            }, reload);
        }

        public static Dictionary<string, Runtime.Buildings.BuildingType.Line> GetBuildingLineTypes(bool reload = false) {
            var tmp = GetBuildingTypes(reload);
            var res = new Dictionary<string, Runtime.Buildings.BuildingType.Line>();
            foreach (var elem in tmp) {
                res.Add(elem.Key, elem.Value.line);
            }
            return res;
        }

        public static Dictionary<string, Runtime.Buildings.BuildingType.Side> GetBuildingSideTypes(bool reload = false) {
            var tmp = GetBuildingTypes(reload);
            var res = new Dictionary<string, Runtime.Buildings.BuildingType.Side>();
            foreach (var elem in tmp) {
                res.Add(elem.Key, elem.Value.side);
            }
            return res;
        }

        public static Dictionary<string, Runtime.Buildings.BuildingType.Building> GetBuildingBuildingTypes(bool reload = false) {
            var tmp = GetBuildingTypes(reload);
            var res = new Dictionary<string, Runtime.Buildings.BuildingType.Building>();
            foreach (var elem in tmp) {
                res.Add(elem.Key, elem.Value.building);
            }
            return res;
        }

        //Building blocks
        static Dictionary<string, Runtime.Buildings.BlockType> buildingBlockTypes;
        public static Dictionary<string, Runtime.Buildings.BlockType> GetBuildingBlockTypes(bool reload = false) {
            return GetTypes(ref buildingBlockTypes, elementsDir => {
                var dirs = Directory.GetDirectories(Path.Combine(elementsDir, "buildingBlocks"));
                foreach (var dir in dirs) {
                    var tr = new Buildings.BlockType(dir);
                    var key = Path.GetFileName(dir);
                    buildingBlockTypes.Add(key, new Runtime.Buildings.BlockType(tr, key));
                }
            }, reload);
        }

        //MeshInstance (just one)
        static Runtime.MeshInstanceSettings meshInstanceSettings = null;
        public static Runtime.MeshInstanceSettings GetMeshInstanceSettings(bool reload = false) {
            if (meshInstanceSettings != null && !reload) return meshInstanceSettings;
            var elementsDir = GetElementsDir();
            if (elementsDir == null) return null;
            var file = Path.Combine(elementsDir, "props", "meshInstance.json");
            var content = File.ReadAllText(file);
            var typeData = Newtonsoft.Json.JsonConvert.DeserializeObject<MeshInstanceSettings>(content);
            meshInstanceSettings = new Runtime.MeshInstanceSettings(typeData);
            return meshInstanceSettings;
        }
    }
}
