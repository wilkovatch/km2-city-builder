using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using Newtonsoft.Json.Linq;
using SM = StringManager;

namespace Api {
    //The city as a whole: files, terrain, city-wide generation and editing
    public partial class CityApi {
        // Files

        [ApiMethod("city.new", "Creates a city in a folder from a template of a core (see cores.templates) and loads it")]
        public Task<object> NewCity(string folder, string core, string template, int terrainSize = 4096, int heightmapResolution = 4096, float maxHeight = 1000,
            bool installPythonModules = false) {

            RequireNoTransaction("creating a city");
            return RunLong(async () => {
                if (string.IsNullOrEmpty(folder)) throw ApiException.InvalidParam("missing folder");
                RequireCore(core);
                if (System.Array.IndexOf(Operations.CityFiles.HeightmapResolutions, heightmapResolution) < 0) {
                    throw ApiException.InvalidValue("heightmapResolution must be one of " + string.Join(", ", Operations.CityFiles.HeightmapResolutions));
                }
                if (terrainSize <= 0 || maxHeight <= 0) throw ApiException.InvalidValue("terrainSize and maxHeight must be positive");
                if (File.Exists(Path.Combine(folder, "city.json.gz"))) throw new ApiException("already_exists", "the folder already contains a city");
                string templateFolder = null;
                foreach (var t in Operations.CityFiles.GetTemplates(core).folders) {
                    if (Path.GetFileName(t) == template) templateFolder = t;
                }
                if (templateFolder == null) throw ApiException.NotFound("template '" + template + "' of " + core);
                builder.BeginExternalChange();
                try {
                    Operations.CityFiles.CreateCityFolder(folder, core, templateFolder, terrainSize, heightmapResolution, maxHeight);
                } catch (System.Exception e) when (e is IOException || e is System.UnauthorizedAccessException) {
                    throw new ApiException("io_error", e.Message);
                }
                return await LoadCity(folder, "never", installPythonModules);
            });
        }

        async Task<object> LoadCity(string folder, string migrate, bool installPythonModules, bool rememberAsLastCity = false) {
            if (migrate != "never" && migrate != "always") throw ApiException.InvalidValue("migrate must be 'never' or 'always'");
            builder.CloseAllPanels();
            drawings.Clear();
            var interaction = new Operations.FixedInteraction { allowMigration = migrate == "always", installPythonModules = installPythonModules };
            var options = new CityDecoder.LoadOptions { interaction = interaction, progress = progress, rememberAsLastCity = rememberAsLastCity };
            var res = await Operations.CityFiles.Load(M, folder, options);
            if (!res.success) {
                var code = res.error == "migration_required" ? "migration_required" : res.error;
                throw new ApiException(code, interaction.lastError ?? res.message);
            }
            //the city is complete after its first update
            M.worldChanged = true;
            M.ProcessUpdate();
            History.UndoManager.Clear();
            return CityInfo();
        }

        [ApiMethod("city.load", "Loads the city of a folder. migrate: 'always' to migrate a city made with another version of its core (the city files are changed), 'never' to fail instead. remember: load it again when the program starts")]
        public Task<object> Load(string folder, string migrate = "never", bool installPythonModules = false, bool remember = false) {
            RequireNoTransaction("loading a city");
            return RunLong(() => {
                if (string.IsNullOrEmpty(folder)) throw ApiException.InvalidParam("missing folder");
                if (!File.Exists(Path.Combine(folder, "city.json.gz"))) throw ApiException.NotFound("city in '" + folder + "'");
                return LoadCity(folder, migrate, installPythonModules, remember);
            });
        }

        [ApiMethod("city.reload", "Loads the current city again from its saved file (the unsaved changes are lost)")]
        public Task<object> Reload() {
            RequireCity();
            RequireNoTransaction("reloading the city");
            var folder = PreferencesManager.workingDirectory;
            return RunLong(() => LoadCity(folder, "never", false));
        }

        [ApiMethod("city.save", "Saves the city in its folder")]
        public Task<object> Save() {
            RequireCity();
            return RunLong(async () => {
                Flush();
                var error = await Operations.CityFiles.Save(M, progress);
                if (error != null) throw new ApiException("io_error", error);
                return (object)new { folder = PreferencesManager.workingDirectory };
            });
        }

        [ApiMethod("city.close", "Closes the city (the unsaved changes are lost)")]
        public void Close() {
            RequireNoTransaction("closing the city");
            if (busy > 0 || IsLongOperationRunning()) throw new ApiException("busy", "another operation is running");
            builder.CloseAllPanels();
            drawings.Clear();
            Operations.CityFiles.Close(M);
            builder.Reload();
        }

        [ApiMethod("city.exportJson", "Writes the export JSON of the city (with the generated geometry, as read by the exporters of the cores)")]
        public Task<object> ExportJson(string path) {
            RequireCity();
            if (string.IsNullOrEmpty(path)) throw ApiException.InvalidParam("missing path");
            return RunLong(async () => {
                Flush();
                CreateParentFolder(path);
                var error = await Operations.CityFiles.ExportJson(M, path, progress);
                if (error != null) throw new ApiException("export_failed", error);
                return (object)new { path };
            });
        }

        [ApiMethod("city.export", "Exports the city with an exporter of the core (by its format, see exporters.list), with the given flags (the others keep their default)")]
        public Task<object> Export(string path, string exporter, Dictionary<string, bool> flags = null, bool showConsole = false) {
            RequireCity();
            if (string.IsNullOrEmpty(path)) throw ApiException.InvalidParam("missing path");
            var exp = CityExporter.FindExporter(exporter);
            if (exp == null) throw ApiException.NotFound("exporter '" + exporter + "'");
            var known = new HashSet<string>();
            foreach (var f in exp.settings.flags ?? new FileBrowserHelper.CustomExporterFlags[0]) known.Add(f.id);
            if (flags != null) {
                foreach (var key in flags.Keys) if (!known.Contains(key)) throw ApiException.InvalidValue("unknown flag '" + key + "' (" + string.Join(", ", known) + ")");
            }
            var values = CityExporter.GetExporterFlags(exp, flags);
            var locked = CityExporter.GetLockedFlags(exp, values);
            if (flags != null) {
                foreach (var key in flags.Keys) if (locked.Contains(key)) throw ApiException.InvalidValue("the flag '" + key + "' can't be set with the values of the others");
            }
            if (!path.EndsWith("." + exp.settings.format)) path += "." + exp.settings.format;
            return RunLong(async () => {
                Flush();
                CreateParentFolder(path);
                var res = await CityExporter.ExportWithExporterAsync(exp, values, M, path, showConsole, progress);
                if (!res.success) throw new ApiException("export_failed", res.error, new { exitCode = res.exitCode, output = res.output });
                return (object)new { path, exitCode = res.exitCode, output = res.output };
            });
        }

        //the folder of an exported file is created if missing
        static void CreateParentFolder(string path) {
            try {
                var folder = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            } catch (System.Exception e) when (e is IOException || e is System.UnauthorizedAccessException || e is System.ArgumentException || e is System.NotSupportedException) {
                throw new ApiException("io_error", "could not create the folder of " + path + ": " + e.Message);
            }
        }

        // Terrain

        [ApiMethod("terrain.brush", "One step of the terrain brush at the position: mode raise, lower or level (to the height under the center, or to height)")]
        public void TerrainBrush(string mode, Vector3 position, float range = 3, float intensity = 1, float? height = null) {
            Operations.Terrain.BrushMode m;
            switch (mode) {
                case "raise": m = Operations.Terrain.BrushMode.Raise; break;
                case "lower": m = Operations.Terrain.BrushMode.Lower; break;
                case "level": m = Operations.Terrain.BrushMode.Level; break;
                default: throw ApiException.InvalidValue("mode must be raise, lower or level");
            }
            if (height.HasValue && m != Operations.Terrain.BrushMode.Level) throw ApiException.InvalidParam("height is only for the level mode");
            ApplyChange("terrain", () => Operations.Terrain.ApplyBrush(builder.helper, m, position, range, intensity, height));
        }

        void CheckGridArea(int x, int z, int width, int depth) {
            var grid = Operations.Terrain.GetGridSize(builder.helper);
            if (width <= 0 || depth <= 0) throw ApiException.InvalidValue("width and depth must be positive");
            if (x < 0 || z < 0 || x + width > grid || z + depth > grid) throw ApiException.InvalidValue("the area is out of the grid (0-" + grid + ")");
        }

        [ApiMethod("terrain.heights.get", "The heights (in meters) of an area of the heightmap grid (see terrain.info), row by row along z; encoding: base64 (little endian float32) or array (a list of rows)")]
        public object GetTerrainHeights(int x, int z, int width, int depth, string encoding = "base64") {
            RequireCity();
            if (encoding != "base64" && encoding != "array") throw ApiException.InvalidValue("encoding must be base64 or array");
            CheckGridArea(x, z, width, depth);
            var heights = Operations.Terrain.GetHeights(builder.helper, x, z, width, depth);
            object data;
            if (encoding == "base64") {
                var bytes = new byte[width * depth * sizeof(float)];
                System.Buffer.BlockCopy(heights, 0, bytes, 0, bytes.Length);
                data = System.Convert.ToBase64String(bytes);
            } else {
                var rows = new List<float[]>();
                for (int i = 0; i < depth; i++) {
                    var row = new float[width];
                    for (int j = 0; j < width; j++) row[j] = heights[i, j];
                    rows.Add(row);
                }
                data = rows;
            }
            return new { x, z, width, depth, encoding, heights = data };
        }

        [ApiMethod("terrain.heights.set", "Sets the heights (in meters) of an area of the heightmap grid, as terrain.heights.get gives them; returns how many were out of the range of the terrain and clamped")]
        public object SetTerrainHeights(int x, int z, int width, int depth, Newtonsoft.Json.Linq.JToken heights, string encoding = "base64") {
            RequireCity();
            if (encoding != "base64" && encoding != "array") throw ApiException.InvalidValue("encoding must be base64 or array");
            CheckGridArea(x, z, width, depth);
            var values = new float[depth, width];
            try {
                if (encoding == "base64") {
                    var bytes = System.Convert.FromBase64String(heights.Value<string>());
                    if (bytes.Length != width * depth * sizeof(float)) throw ApiException.InvalidValue("the data has " + bytes.Length / sizeof(float) + " heights instead of " + width * depth);
                    System.Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
                } else {
                    if (!(heights is Newtonsoft.Json.Linq.JArray rows) || rows.Count != depth) throw ApiException.InvalidValue("heights must be a list of " + depth + " rows");
                    for (int i = 0; i < depth; i++) {
                        if (!(rows[i] is Newtonsoft.Json.Linq.JArray row) || row.Count != width) throw ApiException.InvalidValue("row " + i + " must have " + width + " heights");
                        for (int j = 0; j < width; j++) values[i, j] = row[j].Value<float>();
                    }
                }
            } catch (System.FormatException e) {
                throw ApiException.InvalidValue("invalid heights: " + e.Message);
            } catch (System.Exception e) when (e is System.InvalidCastException || e is System.ArgumentException) {
                throw ApiException.InvalidValue("invalid heights: " + e.Message);
            }
            var clamped = ApplyChange("terrain", () => Operations.Terrain.SetHeights(builder.helper, x, z, values));
            return new { clamped };
        }

        [ApiMethod("terrain.importHeightmap", "Sets the terrain heights from a grayscale image (in the city or extra folders), scale: the height of white")]
        public Task<object> ImportHeightmap(string file, float scale = 1) {
            RequireCity();
            var filename = PathHelper.FindInFolders(file);
            if (!File.Exists(filename)) throw ApiException.NotFound("image '" + file + "'");
            RequireNoTransaction("importing a heightmap");
            return RunLong(async () => {
                builder.BeginExternalChange();
                await builder.helper.ApplyHeightmapAsync(filename, scale, progress);
                M.FlagAsChanged(true);
                Flush();
                builder.EndExternalChange();
                return (object)null;
            });
        }

        // Whole city

        [ApiMethod("city.generateTerrain", "Creates terrain patches between the roads (like Create > Whole city terrain)")]
        public Task<object> GenerateTerrain(float distance = 5, float segmentLength = 100, float fusionDistance = 0, int smoothing = 0, float internalDistance = 0, string texture = null) {
            RequireCity();
            return RunLong(async () => {
                builder.BeginExternalChange();
                if (texture != null) PreferencesManager.Set("curTerrainTexture", texture);
                await M.CreateCityTerrain(distance, segmentLength, fusionDistance, smoothing, internalDistance, progress);
                Flush();
                builder.EndExternalChange();
                return (object)new { terrainPatches = CountAlive(M.patches) };
            });
        }

        [ApiMethod("city.generateBuildings", "Creates building lines around the blocks between the roads (like Create > Whole city buildings), split into buildings with the presets")]
        public Task<object> GenerateBuildings(string linePreset, List<string> buildingPresets, float minLength = 15, float maxLength = 15, bool subdivide = true, bool nullOnly = false) {
            RequireCity();
            var lineState = Operations.Presets.Get("buildingLine", linePreset);
            if (lineState == null) throw ApiException.NotFound("building line preset '" + linePreset + "'");
            var presets = GetBuildingPresets(buildingPresets);
            if (presets.Count == 0) throw ApiException.InvalidParam("at least one building preset is needed");
            return RunLong(async () => {
                builder.BeginExternalChange();
                await M.CreateCityBuildings(minLength, maxLength, presets, subdivide, lineState, nullOnly ? new List<string>() { "" } : null, progress);
                Flush();
                builder.EndExternalChange();
                return (object)new { buildingLines = CountAlive(M.buildings) };
            });
        }

        [ApiMethod("city.eraseTerrain", "Deletes every terrain patch")]
        public void EraseTerrain() {
            ApplyChange(SM.Get("CT_ERASE"), () => Operations.City.EraseTerrain(M));
        }

        [ApiMethod("city.eraseBuildings", "Deletes every building line")]
        public void EraseBuildings() {
            ApplyChange(SM.Get("CB_ERASE"), () => Operations.City.EraseBuildings(M));
        }

        [ApiMethod("city.rotate", "Rotates the whole city (elements and terrain): 90ccw, 90cw or 180")]
        public Task<object> Rotate(string angle) {
            RequireCity();
            int rotation;
            switch (angle) {
                case "90ccw": rotation = 1; break;
                case "90cw": rotation = 2; break;
                case "180": rotation = 3; break;
                default: throw ApiException.InvalidValue("angle must be 90ccw, 90cw or 180");
            }
            RequireNoTransaction("rotating the city");
            return RunLong(async () => {
                builder.BeginExternalChange();
                await Operations.City.Rotate(M, rotation, progress);
                Flush();
                builder.EndExternalChange();
                return (object)null;
            });
        }

        static Operations.City.BatchKind ParseBatchKind(string kind) {
            switch (kind) {
                case "road": return Operations.City.BatchKind.Road;
                case "intersection": return Operations.City.BatchKind.Intersection;
                case "terrainPatch": return Operations.City.BatchKind.TerrainPatch;
                case "buildingLine": return Operations.City.BatchKind.BuildingLine;
                case "building": return Operations.City.BatchKind.Building;
                case "buildingSide": return Operations.City.BatchKind.BuildingSide;
                case "meshInstance": return Operations.City.BatchKind.MeshInstance;
                default: throw ApiException.InvalidValue("kind must be road, intersection, terrainPatch, buildingLine, building, buildingSide or meshInstance");
            }
        }

        [ApiMethod("city.batchUpdate", "Sets fields of every element of a kind satisfying the where expression (like Edit > Batch update), returns how many were updated")]
        public int BatchUpdate(string kind, string update, string where = "") {
            var k = ParseBatchKind(kind);
            return ApplyChange(SM.Get("BATCH_UPDATE"), () => {
                try {
                    return Operations.City.BatchUpdate(M, k, update, where);
                } catch (Operations.City.BatchException e) {
                    switch (e.part) {
                        case Operations.City.BatchException.Part.Update: throw ApiException.InvalidValue("syntax error in the update expression: " + e.Message);
                        case Operations.City.BatchException.Part.Where: throw ApiException.InvalidValue("syntax error in the where expression: " + e.Message);
                        default: throw new ApiException("batch_failed", e.Message);
                    }
                }
            });
        }

        [ApiMethod("city.batchParameters", "The fields the batch update can use for a kind, by type of element")]
        public object BatchParameters(string kind) {
            RequireCity();
            var res = new Dictionary<string, List<object>>();
            foreach (var t in Operations.City.GetBatchParameters(ParseBatchKind(kind))) {
                var list = new List<object>();
                foreach (var p in t.Value) list.Add(new { name = p.name, type = p.type });
                res[t.Key] = list;
            }
            return res;
        }

        // Project settings

        [ApiMethod("project.settings.get", "The settings of the city: extra asset folders (of the city, and global) and ground overlay texture")]
        public object GetProjectSettings() {
            RequireCity();
            return new {
                extraFolders = PreferencesManager.GetList<string>("extraFolders", new List<string>()),
                globalExtraFolders = SettingsManager.GetList<string>("extraFolders", new List<string>()),
                overlayTexture = PreferencesManager.Get("overlayTex", "")
            };
        }

        [ApiMethod("project.settings.set", "Changes the settings of the city: extra asset folders of the city and ground overlay texture")]
        public object SetProjectSettings(List<string> extraFolders = null, string overlayTexture = null) {
            RequireCity();
            if (extraFolders != null) PreferencesManager.SetList("extraFolders", extraFolders);
            if (overlayTexture != null) {
                PreferencesManager.Set("overlayTex", overlayTexture);
                builder.helper.LoadOverlayTexture(overlayTexture);
            }
            return GetProjectSettings();
        }
    }
}
