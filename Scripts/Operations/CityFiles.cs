using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using SM = StringManager;

namespace Operations {
    //Creating, loading, saving and exporting cities
    public static class CityFiles {
        public static readonly int[] HeightmapResolutions = { 128, 256, 512, 1024, 2048, 4096 };

        //the templates of a core (its own ones, then the custom ones): shown names and folders
        public static (List<string> names, List<string> folders) GetTemplates(string core) {
            var filesPath = Path.Combine(PathHelper.BasePath(), "Files");
            var corePath = Path.Combine(filesPath, "cores", core);
            var shownNames = new List<string>();
            var directories = new List<string>();
            var coreTemplates = Path.Combine(corePath, "templates");
            if (Directory.Exists(coreTemplates)) {
                foreach (var dir in Directory.GetDirectories(coreTemplates)) {
                    if (Path.GetFileName(dir) != "_common") {
                        var stringName = "TEMPLATE_NAME_" + Path.GetFileName(dir);
                        var translString = SM.Get(stringName, corePath);
                        if (translString == stringName) translString = Path.GetFileName(dir);
                        shownNames.Add(translString);
                        directories.Add(dir);
                    }
                }
            }

            //custom templates
            var customTemplatePath = Path.Combine(filesPath, "custom", "templates", core);
            if (!Directory.Exists(customTemplatePath)) Directory.CreateDirectory(customTemplatePath);
            foreach (var dir in Directory.GetDirectories(customTemplatePath)) {
                if (Path.GetFileName(dir) != "_common") {
                    shownNames.Add(Path.GetFileName(dir));
                    directories.Add(dir);
                }
            }
            return (shownNames, directories);
        }

        //copies the template into the folder and writes an empty city with the given terrain settings
        public static void CreateCityFolder(string folder, string core, string templateFolder, int terrainSize, int heightmapResolution, float maxHeight) {
            var templatesPath = Path.Combine(PathHelper.BasePath(), "Files", "cores", core, "templates");
            Directory.CreateDirectory(folder);
            PathHelper.DirectoryCopy(Path.Combine(templatesPath, "_common"), folder);
            PathHelper.DirectoryCopy(templateFolder, folder);
            var savedCity = new IO.SavedCity.SavedCity();
            savedCity.heightmapResolution = heightmapResolution;
            savedCity.terrainSize = terrainSize;
            savedCity.heightMap = "";
            savedCity.maxHeight = maxHeight;
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(savedCity, Newtonsoft.Json.Formatting.Indented);
            var bytes = System.Text.Encoding.ASCII.GetBytes(json);
            using (var compressedFileStream = File.Create(Path.Combine(folder, "city.json.gz"))) {
                using (var compressor = new GZipStream(compressedFileStream, CompressionMode.Compress)) {
                    compressor.Write(bytes, 0, bytes.Length);
                }
            }
        }

        public static Task<CityDecoder.LoadResult> Load(ElementManager manager, string folder, CityDecoder.LoadOptions options) {
            PreferencesManager.workingDirectory = folder;
            CoreManager.Reset();
            return CityImporter.LoadCityAsync(manager, folder, options);
        }

        //the core feature version is stored with the city, since there is no migration for the changes that don't break it
        static void PrepareSave() {
            var core = PreferencesManager.Get("core", "");
            PreferencesManager.Set("coreFeatureVersion", CoreManager.GetCoreFeatureVersion(core));
            PreferencesManager.Save();
        }

        //saves the city in its folder: returns null on success, or the error
        public static Task<string> Save(ElementManager manager, System.IProgress<(float, string)> progress = null) {
            PrepareSave();
            return new CityEncoder.JSON(true).EncodeCityAsync(manager, PreferencesManager.workingDirectory + "/city.json", progress);
        }

        //the city with its generated geometry, as read by the exporters of the cores: returns null on success, or the error
        public static Task<string> ExportJson(ElementManager manager, string path, System.IProgress<(float, string)> progress = null) {
            return new CityEncoder.JSON_Export().EncodeCityAsync(manager, path, progress);
        }

        public static void Close(ElementManager manager) {
            manager.CloseCity();
        }
    }
}
