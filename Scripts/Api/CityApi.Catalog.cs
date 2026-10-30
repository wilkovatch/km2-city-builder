using System.Collections.Generic;
using System.IO;
using Parameter = CityElements.Types.Parameter;
using SM = StringManager;
using TI = CityElements.Types.Parsers.TypeInheritance;
using TP = CityElements.Types.Parsers.TypeParser;

namespace Api {
    //What scripts can choose from: cores, templates, element types and their parameters, presets, assets, exporters
    public partial class CityApi {
        public static readonly string[] TypeKinds = {
            "road", "intersection", "terrainPatch", "borderMesh", "buildingLine", "building", "buildingSide", "buildingBlock",
            "propContainer", "propElement", "cityProperties", "meshInstance", "parametricObject", "lowLevel"
        };

        [ApiMethod("cores.list", "The installed cores")]
        public object ListCores() {
            var cores = CoreManager.GetCores();
            var res = new List<object>();
            for (int i = 0; i < cores.folders.Count; i++) {
                res.Add(new { name = cores.folders[i], label = cores.names[i], version = CoreManager.GetCoreVersion(cores.folders[i]) });
            }
            return res;
        }

        [ApiMethod("cores.templates", "The city templates of a core (the name is used by city.new)")]
        public object ListTemplates(string core) {
            RequireCore(core);
            var templates = Operations.CityFiles.GetTemplates(core);
            var res = new List<object>();
            for (int i = 0; i < templates.folders.Count; i++) {
                res.Add(new { name = Path.GetFileName(templates.folders[i]), label = templates.names[i] });
            }
            return res;
        }

        static void RequireCore(string core) {
            if (!CoreManager.GetCores().folders.Contains(core)) throw new ApiException("core_not_found", "core '" + core + "' not installed");
        }

        //the folder of the types of a kind in the elements folder of the core (null for the kinds with a single type)
        static string CategoryOf(string kind) {
            switch (kind) {
                case "road": return TI.Roads;
                case "intersection": return TI.Intersections;
                case "terrainPatch": return TI.TerrainPatches;
                case "borderMesh": return TI.BorderMeshes;
                case "buildingLine": case "building": case "buildingSide": return TI.Buildings;
                case "buildingBlock": return TI.BuildingBlocks;
                case "propContainer": return TI.PropsContainers;
                case "propElement": return TI.PropsElements;
                case "parametricObject": return TI.ParametricObjects;
                case "lowLevel": return TI.LowLevel;
                default: return null;
            }
        }

        //the type followed by the types it extends, nearest first
        static string[] AncestryOf(string kind, string name) {
            var category = CategoryOf(kind);
            return category != null ? TP.GetAncestry(category, name) : new[] { name };
        }

        static object TypeInfo<T>(string kind, string name, string label, Parameter[] parameters, List<Operations.Builtins.Property> builtins) {
            return new { name, label = label != null ? SM.GetOrKey(label) : name, ancestry = AncestryOf(kind, name), parameters = SchemaOf(parameters), builtins = SchemaOf(builtins) };
        }

        static List<object> TypesOf<T>(string kind, Dictionary<string, T> types, System.Func<T, string> label, System.Func<T, Parameter[]> parameters,
            System.Func<T, CityElements.Types.Runtime.RoadType> roadType = null) {
            var res = new List<object>();
            if (types == null) return res;
            foreach (var t in types) {
                res.Add(TypeInfo<T>(kind, t.Key, label(t.Value), parameters(t.Value), Operations.Builtins.GetProperties(kind, roadType?.Invoke(t.Value))));
            }
            return res;
        }

        [ApiMethod("types.list", "The types of an element kind of the current core, with their parameters, built-in properties and ancestry (the type, then the types it extends)")]
        public object ListTypes(string kind) {
            RequireCity();
            switch (kind) {
                case "road":
                    return TypesOf(kind, TP.GetRoadTypes(), t => t.typeData.GetUI().label, t => Params(t.parameterContainer), t => t);
                case "intersection":
                    return TypesOf(kind, TP.GetIntersectionTypes(), t => t.typeData.GetUI().label, t => Params(t.parameterContainer));
                case "terrainPatch":
                    return TypesOf(kind, TP.GetTerrainPatchTypes(), t => t.typeData.GetUI().label, t => Params(t.parameterContainer));
                case "borderMesh":
                    return TypesOf(kind, TP.GetTerrainPatchBorderMeshTypes(), t => t.typeData.GetUI().label, t => Params(t.parameterContainer));
                case "buildingLine":
                    return TypesOf(kind, TP.GetBuildingLineTypes(), t => t.typeData.GetUI().label, t => Params(t.parameterContainer));
                case "building":
                    return TypesOf(kind, TP.GetBuildingBuildingTypes(), t => t.typeData.GetUI().label, t => Params(t.parameterContainer));
                case "buildingSide":
                    return TypesOf(kind, TP.GetBuildingSideTypes(), t => t.typeData.GetUI().label, t => Params(t.parameterContainer));
                case "buildingBlock":
                    return TypesOf(kind, TP.GetBuildingBlockTypes(), t => t.typeData.GetUI().label, t => Params(t.parameterContainer));
                case "propContainer":
                    var containers = new List<object>();
                    foreach (var t in TP.GetPropsContainersTypes()) {
                        var lists = new List<object>();
                        foreach (var p in t.Value.parameters) lists.Add(new { name = p.name, allowedTypes = p.allowedTypes, maxNumber = p.maxNumber });
                        containers.Add(new { name = t.Key, label = SM.GetOrKey(t.Value.label), ancestry = AncestryOf(kind, t.Key), lists });
                    }
                    return containers;
                case "propElement":
                    var elems = new List<object>();
                    foreach (var t in TP.GetPropsElementTypes()) {
                        elems.Add(new { name = t.Key, label = SM.GetOrKey(t.Value.typeData.label), ancestry = AncestryOf(kind, t.Key), maxMeshes = t.Value.typeData.maxMeshes, parameters = SchemaOf(Params(t.Value.typeData.parametersInfo)) });
                    }
                    return elems;
                case "cityProperties":
                    return TypesOf(kind, TP.GetCitySettings(), t => null, t => Params(t.parameterContainer));
                case "meshInstance":
                    var settings = TP.GetMeshInstanceSettings();
                    return new List<object> { new { name = "", label = (string)null, ancestry = new[] { "" }, parameters = SchemaOf(Params(settings?.typeData.parametersInfo)) } };
                case "lowLevel":
                    var lowLevelTypes = new List<object>();
                    foreach (var t in TP.GetLowLevelTypes()) {
                        var s = t.Value.typeData.settings;
                        lowLevelTypes.Add(new {
                            name = t.Key, label = SM.GetOrKey(t.Value.typeData.ui.label ?? t.Key), ancestry = AncestryOf(kind, t.Key),
                            parameters = SchemaOf(Params(t.Value.parameterContainer)), controlPoints = s.controlPoints, mesh = s.mesh,
                            contains = s.container?.contains, creatable = s.creatable
                        });
                    }
                    return lowLevelTypes;
                case "parametricObject":
                    var res = new List<object>();
                    foreach (var t in TP.GetParametricMeshInstanceSettings() ?? new Dictionary<string, CityElements.Types.Runtime.MeshInstanceSettings>()) {
                        res.Add(new { name = t.Key, label = (string)null, ancestry = AncestryOf(kind, t.Key), parameters = SchemaOf(Params(t.Value.typeData.parametersInfo)) });
                    }
                    return res;
                default:
                    throw ApiException.InvalidValue("unknown kind '" + kind + "' (" + string.Join(", ", TypeKinds) + ")");
            }
        }

        // Presets

        static List<string> PresetNames(string category, List<string> allowedTypes = null) {
            var res = new List<string>();
            foreach (var p in Operations.Presets.Filter(category, allowedTypes)) res.Add(p.Name);
            return res;
        }

        [ApiMethod("presets.list", "The names of the presets of a category (road, intersection, buildingLine, building, buildingSide, buildingBlock, terrainPatchBorderMesh, propContainer, propElem, roadPropRule, lowLevel/<type> for enabled low-level types, or a container such as rail); the first one is the default")]
        public List<string> ListPresets(string category) {
            RequireCity();
            try { return PresetNames(category); }
            catch (System.ArgumentException e) { throw ApiException.InvalidValue(e.Message); }
        }

        [ApiMethod("presets.get", "The values of a preset")]
        public ObjectState GetPreset(string category, string name) {
            RequireCity();
            ObjectState res;
            try { res = PresetManager.GetPresetByName(name, category); }
            catch (System.ArgumentException e) { throw ApiException.InvalidValue(e.Message); }
            if (res == null) throw ApiException.NotFound("preset '" + name + "' of " + category);
            return res;
        }

        [ApiMethod("presets.save", "Saves a preset with the given values (overwriting the one with the same name)")]
        public void SavePreset(string category, string name, ObjectState values) {
            RequireCity();
            if (string.IsNullOrWhiteSpace(name)) throw ApiException.InvalidValue("the preset name can't be empty");
            if (values == null) throw ApiException.InvalidParam("missing values");
            try { Operations.Presets.Save(values, category, name); }
            catch (System.ArgumentException e) { throw ApiException.InvalidValue(e.Message); }
        }

        [ApiMethod("presets.delete", "Deletes a preset (not the default one); linked roads, intersections, building parts and enabled low-level elements are unlinked from it")]
        public void DeletePreset(string category, string name) {
            RequireCity();
            if (name == Operations.Presets.DefaultName) throw ApiException.InvalidOperation("the default preset can't be deleted");
            ApplyChange("delete preset", () => {
                try {
                    if (!Operations.Presets.Exists(category, name)) throw ApiException.NotFound("preset '" + name + "' of " + category);
                    Operations.Presets.Delete(M, category, name);
                } catch (System.ArgumentException e) { throw ApiException.InvalidValue(e.Message); }
            });
        }

        [ApiMethod("presets.sync", "Gives the current values of a preset to the roads, intersections, building parts and enabled low-level elements linked to it")]
        public void SyncPreset(string category, string name) {
            ApplyChange("sync preset", () => {
                try {
                    if (!Operations.Presets.CanSync(category)) throw ApiException.InvalidOperation("this preset category does not support sync");
                    if (!Operations.Presets.Exists(category, name)) throw ApiException.NotFound("preset '" + name + "' of " + category);
                    Operations.Presets.Sync(M, category, name);
                } catch (System.ArgumentException e) { throw ApiException.InvalidValue(e.Message); }
            });
        }

        // Assets

        [ApiMethod("assets.list", "The textures or meshes available to the city (its folder and the extra folders), optionally filtered with a regular expression")]
        public List<string> ListAssets(string kind, string regex = null) {
            RequireCity();
            List<string> formats;
            if (kind == "texture") formats = TextureImporter.GetSupportedFormats();
            else if (kind == "mesh") formats = MeshImporter.GetSupportedFormats();
            else throw ApiException.InvalidValue("kind must be 'texture' or 'mesh'");
            var paths = new List<string>();
            var absPaths = new List<string>();
            var indices = new Dictionary<string, int>();
            (string, string)[] entries = null;
            try {
                PathHelper.GetFileList(true, regex, formats, ref paths, ref absPaths, ref indices, ref entries);
            } catch (System.ArgumentException e) {
                throw ApiException.InvalidValue("invalid regular expression: " + e.Message);
            }
            return paths;
        }

        // Exporters

        [ApiMethod("exporters.list", "The exporters of the current core, with their flags; validates: the exporter can validate a city (city.validate)")]
        public object ListExporters() {
            RequireCity();
            var res = new List<object>();
            foreach (var e in FileBrowserHelper.GetCustomExporters() ?? new List<FileBrowserHelper.CustomExporter>()) {
                var flags = new List<object>();
                foreach (var f in e.settings.flags ?? new FileBrowserHelper.CustomExporterFlags[0]) {
                    flags.Add(new { id = f.id, label = SM.GetOrKey(f.label), defaultValue = f.defaultValue, enables = f.enables, disables = f.disables,
                        format = f.format, formatLabel = f.formatLabel != null ? SM.GetOrKey(f.formatLabel) : null });
                }
                res.Add(new { format = e.settings.format, formats = CityExporter.GetFormats(e), label = SM.GetOrKey(e.settings.label), flags,
                    validates = e.settings.validation != null });
            }
            return res;
        }
    }
}
