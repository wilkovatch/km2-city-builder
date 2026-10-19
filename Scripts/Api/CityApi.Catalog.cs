using System.Collections.Generic;
using System.IO;
using Parameter = CityElements.Types.Parameter;
using SM = StringManager;
using TP = CityElements.Types.Parsers.TypeParser;

namespace Api {
    //What scripts can choose from: cores, templates, element types and their parameters, presets, assets, exporters
    public partial class CityApi {
        public static readonly string[] TypeKinds = {
            "road", "intersection", "terrainPatch", "borderMesh", "buildingLine", "building", "buildingSide", "buildingBlock",
            "propContainer", "propElement", "cityProperties", "meshInstance", "parametricObject"
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

        static object TypeInfo<T>(string name, string label, Parameter[] parameters, List<Operations.Builtins.Property> builtins) {
            return new { name, label = label != null ? SM.GetOrKey(label) : name, parameters = SchemaOf(parameters), builtins = SchemaOf(builtins) };
        }

        static List<object> TypesOf<T>(string kind, Dictionary<string, T> types, System.Func<T, string> label, System.Func<T, Parameter[]> parameters,
            System.Func<T, CityElements.Types.Runtime.RoadType> roadType = null) {
            var res = new List<object>();
            if (types == null) return res;
            foreach (var t in types) {
                res.Add(TypeInfo<T>(t.Key, label(t.Value), parameters(t.Value), Operations.Builtins.GetProperties(kind, roadType?.Invoke(t.Value))));
            }
            return res;
        }

        [ApiMethod("types.list", "The types of an element kind of the current core, with their parameters and built-in properties")]
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
                        containers.Add(new { name = t.Key, label = SM.GetOrKey(t.Value.label), lists });
                    }
                    return containers;
                case "propElement":
                    var elems = new List<object>();
                    foreach (var t in TP.GetPropsElementTypes()) {
                        elems.Add(new { name = t.Key, label = SM.GetOrKey(t.Value.typeData.label), maxMeshes = t.Value.typeData.maxMeshes, parameters = SchemaOf(Params(t.Value.typeData.parametersInfo)) });
                    }
                    return elems;
                case "cityProperties":
                    return TypesOf(kind, TP.GetCitySettings(), t => null, t => Params(t.parameterContainer));
                case "meshInstance":
                    var settings = TP.GetMeshInstanceSettings();
                    return new List<object> { new { name = "", label = (string)null, parameters = SchemaOf(Params(settings?.typeData.parametersInfo)) } };
                case "parametricObject":
                    var res = new List<object>();
                    foreach (var t in TP.GetParametricMeshInstanceSettings() ?? new Dictionary<string, CityElements.Types.Runtime.MeshInstanceSettings>()) {
                        res.Add(new { name = t.Key, label = (string)null, parameters = SchemaOf(Params(t.Value.typeData.parametersInfo)) });
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

        [ApiMethod("presets.list", "The names of the presets of a category (road, intersection, buildingLine, building, buildingSide, buildingBlock, terrainPatchBorderMesh, propContainer, propElem, roadPropRule, or a container such as rail); the first one is the default")]
        public List<string> ListPresets(string category) {
            RequireCity();
            return PresetNames(category);
        }

        [ApiMethod("presets.get", "The values of a preset")]
        public ObjectState GetPreset(string category, string name) {
            RequireCity();
            var res = PresetManager.GetPresetByName(name, category);
            if (res == null) throw ApiException.NotFound("preset '" + name + "' of " + category);
            return res;
        }

        [ApiMethod("presets.save", "Saves a preset with the given values (overwriting the one with the same name)")]
        public void SavePreset(string category, string name, ObjectState values) {
            RequireCity();
            if (string.IsNullOrWhiteSpace(name)) throw ApiException.InvalidValue("the preset name can't be empty");
            if (values == null) throw ApiException.InvalidParam("missing values");
            Operations.Presets.Save(values, category, name);
        }

        [ApiMethod("presets.delete", "Deletes a preset (not the default one); the roads, intersections, building lines, buildings, building sides and building blocks using it are unlinked from it")]
        public void DeletePreset(string category, string name) {
            RequireCity();
            if (name == Operations.Presets.DefaultName) throw ApiException.InvalidOperation("the default preset can't be deleted");
            ApplyChange("delete preset", () => {
                if (!Operations.Presets.Exists(category, name)) throw ApiException.NotFound("preset '" + name + "' of " + category);
                Operations.Presets.Delete(M, category, name);
            });
        }

        [ApiMethod("presets.sync", "Gives the current values of a preset to the roads, intersections, building lines, buildings, building sides or building blocks linked to it")]
        public void SyncPreset(string category, string name) {
            ApplyChange("sync preset", () => {
                if (!Operations.Presets.CanSync(category)) throw ApiException.InvalidOperation("only road, intersection, buildingLine, building, buildingSide and buildingBlock presets can be synced");
                if (!Operations.Presets.Exists(category, name)) throw ApiException.NotFound("preset '" + name + "' of " + category);
                Operations.Presets.Sync(M, category, name);
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

        [ApiMethod("exporters.list", "The exporters of the current core, with their flags")]
        public object ListExporters() {
            RequireCity();
            var res = new List<object>();
            foreach (var e in FileBrowserHelper.GetCustomExporters() ?? new List<FileBrowserHelper.CustomExporter>()) {
                var flags = new List<object>();
                foreach (var f in e.settings.flags ?? new FileBrowserHelper.CustomExporterFlags[0]) {
                    flags.Add(new { id = f.id, label = SM.GetOrKey(f.label), defaultValue = f.defaultValue, enables = f.enables, disables = f.disables });
                }
                res.Add(new { format = e.settings.format, label = SM.GetOrKey(e.settings.label), flags });
            }
            return res;
        }
    }
}
