using System.Collections;
using System.Collections.Generic;
using System.IO;
using TI = CityElements.Types.Parsers.TypeInheritance;

namespace CityElements.Types.Parsers {
    public class TypeParser {
        static string cachedCore;

        //loading a city rereads the core files once; panel rebuilds reuse the same runtime types
        public static void Reset() {
            cachedCore = null;
            SplineCurves.Reset();
            roadTypes = null;
            propsTypes = null;
            propsContainersTypes = null;
            terrainPatchBorderMeshTypes = null;
            terrainPatchTypes = null;
            intersectionTypes = null;
            buildingTypes = null;
            buildingBlockTypes = null;
            meshInstanceSettings = null;
            parametricMeshInstanceSettings = null;
            citySettings = null;
            customTypes = null;
            lowLevelTypes = null;
            TI.ClearAncestries();
            RuntimeCalculator.Scripting.LuaScriptHost.Reset();
        }

        static void EnsureCore() {
            var core = PreferencesManager.workingDirectory == "" ? "" : PreferencesManager.Get("core", "");
            if (cachedCore == core) return;
            Reset();
            cachedCore = core;
        }

        //Common methods
        static string GetElementsDir() {
            var core = PreferencesManager.Get("core", "");
            if (core == "") return null;
            return Path.Combine(PathHelper.BasePath(), "Files", "cores", core, "elements");
        }

        static string CategoryDir(string elementsDir, string category) {
            return Path.Combine(elementsDir, category.Replace('/', Path.DirectorySeparatorChar));
        }

        //loads the types of a category of the current core, keeping their ancestries (see TypeInheritance)
        static Dictionary<string, T> GetTypes<T>(ref Dictionary<string, T> res, string category,
            System.Func<string, Dictionary<string, string[]>, Dictionary<string, T>> load, bool reload = false) {

            EnsureCore();
            if (res != null && !reload) return res;
            var elementsDir = GetElementsDir();
            if (elementsDir == null) return new Dictionary<string, T>();
            var ancestries = new Dictionary<string, string[]>();
            res = load(elementsDir, ancestries);
            TI.SetAncestries(category, ancestries);
            return res;
        }

        //the folder types of a category, without the abstract ones
        static Dictionary<string, T> LoadFolderTypes<T>(string elementsDir, string category, Dictionary<string, string[]> ancestries, System.Func<string, string, T> create) {
            var res = new Dictionary<string, T>();
            foreach (var dir in Directory.GetDirectories(CategoryDir(elementsDir, category))) {
                if (TI.IsAbstractFolder(dir)) continue;
                var key = Path.GetFileName(dir);
                res.Add(key, create(dir, key));
                ancestries?.Add(key, TI.FolderAncestry(dir));
            }
            return res;
        }

        //the single file types of a category, without the abstract ones
        static Dictionary<string, T> LoadFileTypes<T>(string elementsDir, string category, Dictionary<string, string[]> ancestries, System.Func<string, string, T> create) {
            var res = new Dictionary<string, T>();
            foreach (var file in Directory.GetFiles(CategoryDir(elementsDir, category))) {
                if (TI.IsAbstractFile(file)) continue;
                var key = Path.GetFileNameWithoutExtension(file);
                res[key] = create(file, key);
                if (ancestries != null) ancestries[key] = TI.SingleFileAncestry(file);
            }
            return res;
        }

        //the type followed by its ancestors, nearest first (category: one of the constants of TypeInheritance)
        public static string[] GetAncestry(string category, string name) {
            EnsureCore();
            return TI.GetAncestry(category, name);
        }



        //Roads
        static Dictionary<string, Runtime.RoadType> roadTypes;
        public static Dictionary<string, Runtime.RoadType> GetRoadTypes(bool reload = false) {
            return GetTypes(ref roadTypes, TI.Roads, LoadRoadTypes, reload);
        }

        public static Dictionary<string, Runtime.RoadType> LoadRoadTypes(string elementsDir, Dictionary<string, string[]> ancestries = null) {
            var commonDir = Path.Combine(elementsDir, "sharedComponents");
            return LoadFolderTypes(elementsDir, TI.Roads, ancestries, (dir, key) => {
                //the junctions of the intersections are named <road type>_<road type>
                if (key.Contains("_")) GameEngine.LogWarning("Road type '" + key + "': the name of a road type should not contain '_'");
                return new Runtime.RoadType(new RoadType(dir, commonDir), key);
            });
        }



        //Props
        static Dictionary<string, Runtime.PropsElementType> propsTypes;
        public static Dictionary<string, Runtime.PropsElementType> GetPropsElementTypes(bool reload = false) {
            return GetTypes(ref propsTypes, TI.PropsElements, LoadPropsElementTypes, reload);
        }

        public static Dictionary<string, Runtime.PropsElementType> LoadPropsElementTypes(string elementsDir, Dictionary<string, string[]> ancestries = null) {
            return LoadFileTypes(elementsDir, TI.PropsElements, ancestries, (file, key) => new Runtime.PropsElementType(TI.LoadSingle<PropsElementType>(file), key));
        }

        static Dictionary<string, PropsContainerType> propsContainersTypes;
        public static Dictionary<string, PropsContainerType> GetPropsContainersTypes(bool reload = false) {
            return GetTypes(ref propsContainersTypes, TI.PropsContainers, LoadPropsContainersTypes, reload);
        }

        public static Dictionary<string, PropsContainerType> LoadPropsContainersTypes(string elementsDir, Dictionary<string, string[]> ancestries = null) {
            return LoadFileTypes(elementsDir, TI.PropsContainers, ancestries, (file, key) => TI.LoadSingle<PropsContainerType>(file));
        }



        //Terrain patchces
        static Dictionary<string, Runtime.RoadType> terrainPatchBorderMeshTypes;
        public static Dictionary<string, Runtime.RoadType> GetTerrainPatchBorderMeshTypes(bool reload = false) {
            return GetTypes(ref terrainPatchBorderMeshTypes, TI.BorderMeshes, LoadTerrainPatchBorderMeshTypes, reload);
        }

        public static Dictionary<string, Runtime.RoadType> LoadTerrainPatchBorderMeshTypes(string elementsDir, Dictionary<string, string[]> ancestries = null) {
            var commonDir = Path.Combine(elementsDir, "sharedComponents");
            return LoadFolderTypes(elementsDir, TI.BorderMeshes, ancestries, (dir, key) => new Runtime.RoadType(new RoadType(dir, commonDir), key));
        }

        static Dictionary<string, Runtime.TerrainPatchType> terrainPatchTypes;
        public static Dictionary<string, Runtime.TerrainPatchType> GetTerrainPatchTypes(bool reload = false) {
            return GetTypes(ref terrainPatchTypes, TI.TerrainPatches, LoadTerrainPatchTypes, reload);
        }

        public static Dictionary<string, Runtime.TerrainPatchType> LoadTerrainPatchTypes(string elementsDir, Dictionary<string, string[]> ancestries = null) {
            return LoadFolderTypes(elementsDir, TI.TerrainPatches, ancestries, (dir, key) => new Runtime.TerrainPatchType(new TerrainPatchType(dir), key));
        }



        //Intersections
        static Dictionary<string, Runtime.IntersectionType> intersectionTypes;
        public static Dictionary<string, Runtime.IntersectionType> GetIntersectionTypes(bool reload = false) {
            return GetTypes(ref intersectionTypes, TI.Intersections, LoadIntersectionTypes, reload);
        }

        public static Dictionary<string, Runtime.IntersectionType> LoadIntersectionTypes(string elementsDir, Dictionary<string, string[]> ancestries = null) {
            var commonDir = Path.Combine(elementsDir, "sharedComponents");
            return LoadFolderTypes(elementsDir, TI.Intersections, ancestries, (dir, key) => new Runtime.IntersectionType(new IntersectionType(dir, commonDir), key));
        }



        //Buildings
        static Dictionary<string, Runtime.Buildings.BuildingType> buildingTypes;
        public static Dictionary<string, Runtime.Buildings.BuildingType> GetBuildingTypes(bool reload = false) {
            return GetTypes(ref buildingTypes, TI.Buildings, LoadBuildingTypes, reload);
        }

        public static Dictionary<string, Runtime.Buildings.BuildingType> LoadBuildingTypes(string elementsDir, Dictionary<string, string[]> ancestries = null) {
            return LoadFolderTypes(elementsDir, TI.Buildings, ancestries, (dir, key) => new Runtime.Buildings.BuildingType(new Buildings.BuildingType(dir), key));
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
            return GetTypes(ref buildingBlockTypes, TI.BuildingBlocks, LoadBuildingBlockTypes, reload);
        }

        public static Dictionary<string, Runtime.Buildings.BlockType> LoadBuildingBlockTypes(string elementsDir, Dictionary<string, string[]> ancestries = null) {
            return LoadFolderTypes(elementsDir, TI.BuildingBlocks, ancestries, (dir, key) => new Runtime.Buildings.BlockType(new Buildings.BlockType(dir), key));
        }

        //MeshInstance (just one)
        static Runtime.MeshInstanceSettings meshInstanceSettings = null;
        public static Runtime.MeshInstanceSettings GetMeshInstanceSettings(bool reload = false) {
            EnsureCore();
            if (meshInstanceSettings != null && !reload) return meshInstanceSettings;
            var elementsDir = GetElementsDir();
            if (elementsDir == null) return null;
            var file = Path.Combine(elementsDir, "props", "meshInstance.json");
            var content = File.ReadAllText(file);
            var typeData = Newtonsoft.Json.JsonConvert.DeserializeObject<MeshInstanceSettings>(content);
            meshInstanceSettings = new Runtime.MeshInstanceSettings(typeData);
            return meshInstanceSettings;
        }

        //Parametric MeshInstances
        static Dictionary<string, Runtime.MeshInstanceSettings> parametricMeshInstanceSettings = null;
        public static Dictionary<string, Runtime.MeshInstanceSettings> GetParametricMeshInstanceSettings(bool reload = false) {
            EnsureCore();
            if (parametricMeshInstanceSettings != null && !reload) return parametricMeshInstanceSettings;
            var elementsDir = GetElementsDir();
            if (elementsDir == null) return null;
            var ancestries = new Dictionary<string, string[]>();
            parametricMeshInstanceSettings = LoadParametricMeshInstanceSettings(elementsDir, ancestries);
            TI.SetAncestries(TI.ParametricObjects, ancestries);
            return parametricMeshInstanceSettings;
        }

        public static Dictionary<string, Runtime.MeshInstanceSettings> LoadParametricMeshInstanceSettings(string elementsDir, Dictionary<string, string[]> ancestries = null) {
            return LoadFileTypes(elementsDir, TI.ParametricObjects, ancestries, (file, key) => new Runtime.MeshInstanceSettings(TI.LoadSingle<MeshInstanceSettings>(file)));
        }

        //City (only one type, dictionary here for compatibility with the InitializeWithCustomParameters method)
        static Dictionary<string, Runtime.CitySettings> citySettings = null;
        public static Dictionary<string, Runtime.CitySettings> GetCitySettings(bool reload = false) {
            EnsureCore();
            if (citySettings != null && !reload) return citySettings;
            var elementsDir = GetElementsDir();
            if (elementsDir == null) return new Dictionary<string, Runtime.CitySettings>();
            var dir = Path.Combine(elementsDir, "cityProperties");
            var tr = new CitySettings.CitySettings(dir);
            citySettings = new Dictionary<string, Runtime.CitySettings> { { "city", new Runtime.CitySettings(tr) } };
            return citySettings;
        }

        static Dictionary<string, CustomElement> customTypes = null;
        public static Dictionary<string, CustomElement> GetCustomTypes(bool reload = false) {
            EnsureCore();
            if (customTypes != null && !reload) return customTypes;
            var elementsDir = GetElementsDir();
            if (elementsDir == null) return null;
            var ancestries = new Dictionary<string, string[]>();
            customTypes = LoadCustomTypes(elementsDir, ancestries);
            TI.SetAncestries(TI.CustomTypes, ancestries);
            return customTypes;
        }

        public static Dictionary<string, CustomElement> LoadCustomTypes(string elementsDir, Dictionary<string, string[]> ancestries = null) {
            return LoadFileTypes(elementsDir, TI.CustomTypes, ancestries, (file, key) => TI.LoadSingle<CustomElement>(file));
        }

        //Low-level elements (the folder is optional, cores without it have no low-level types)
        static Dictionary<string, Runtime.LowLevelType> lowLevelTypes;
        public static Dictionary<string, Runtime.LowLevelType> GetLowLevelTypes(bool reload = false) {
            var types = GetTypes(ref lowLevelTypes, TI.LowLevel, LoadLowLevelTypes, reload);
            if (!types.ContainsKey(PropPath.TypeId)) types.Add(PropPath.TypeId, PropPath.CreateType());
            return types;
        }

        public static Dictionary<string, Runtime.LowLevelType> LoadLowLevelTypes(string elementsDir, Dictionary<string, string[]> ancestries = null) {
            if (!Directory.Exists(CategoryDir(elementsDir, TI.LowLevel))) return new Dictionary<string, Runtime.LowLevelType>();
            return LoadFolderTypes(elementsDir, TI.LowLevel, ancestries, (dir, key) => new Runtime.LowLevelType(new LowLevelType(dir), key));
        }

        //the ancestries of the types of every category of the current core
        public static List<(string category, string name, string[] ancestry)> GetAllAncestries() {
            GetRoadTypes();
            GetIntersectionTypes();
            GetTerrainPatchTypes();
            GetTerrainPatchBorderMeshTypes();
            GetBuildingTypes();
            GetBuildingBlockTypes();
            GetPropsElementTypes();
            GetPropsContainersTypes();
            GetParametricMeshInstanceSettings();
            GetCustomTypes();
            GetLowLevelTypes();
            return TI.GetAllAncestries();
        }
    }
}
