using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using SM = StringManager;

namespace EditorPanels {
    public class NewCityEditorPanel : EditorPanel {
        EditorPanelElements.InputField sizeField;
        EditorPanelElements.Dropdown resField, coreField, templateField;
        int terrSize, heightmapRes;
        string selectedCore, selectedTemplate;
        List<string> resolutionList = new List<string> { "128", "256", "512", "1024", "2048", "4096" };
        List<string> coreList, templateList;
        static bool warningShown = false;

        public override void Initialize(GameObject canvas) {
            Initialize(canvas, 1);
            var p0 = GetPage(0);
            var width = 2.0f;
            sizeField = p0.AddInputField(SM.Get("NC_SIZE"), SM.Get("SIZE_PH"), "4096", UnityEngine.UI.InputField.ContentType.DecimalNumber, SetTerrainSize, width / 2);
            resField = p0.AddDropdown(SM.Get("NC_HM_RES"), resolutionList, SetHeightmapResolution, width / 2);
            p0.IncreaseRow();
            coreField = p0.AddDropdown(SM.Get("NC_CORES"), new List<string>(), SetCore, width);
            p0.IncreaseRow();
            templateField = p0.AddDropdown(SM.Get("NC_TEMPLATES"), new List<string>(), SetTemplate, width);
            p0.IncreaseRow();
            p0.AddButton(SM.Get("NC_CREATE"), NewCity, width);
            p0.IncreaseRow();
            p0.AddButton(SM.Get("CLOSE"), delegate { SetActive(false); }, width);
            p0.IncreaseRow();
            if (PreferencesManager.workingDirectory != "") p0.AddButton(SM.Get("CANCEL"), Cancel, width);
        }

        void SetCore(int value) {
            var val = coreList[value];
            selectedCore = val;
            var templates = GetTemplates();
            templateList = templates.folders;
            templateField.SetOptions(templates.names);
            templateField.SetValue(0);
            SetTemplate(0);
        }

        void SetTemplate(int value) {
            var val = templateList[value];
            selectedTemplate = val;
        }

        (List<string> names, List<string> folders) GetTemplates() {
            var path = new DirectoryInfo(Application.dataPath);
            var filesPath = Path.Combine(path.Parent.ToString(), "Files");
            var corePath = Path.Combine(filesPath, "cores", selectedCore);
            var dirs = Directory.GetDirectories(Path.Combine(corePath, "templates"));
            var res1 = new List<string>();
            var res2 = new List<string>();
            foreach (var dir in dirs) {
                if (Path.GetFileName(dir) != "_common") {
                    var stringName = "TEMPLATE_NAME_" + Path.GetFileName(dir);
                    var translString = SM.Get(stringName, corePath);
                    if (translString == stringName) translString = Path.GetFileName(dir);
                    res1.Add(translString);
                    res2.Add(dir);
                }
            }

            //custom templates
            var customTemplatePath = Path.Combine(filesPath, "custom", "templates", selectedCore);
            if (!Directory.Exists(customTemplatePath)) Directory.CreateDirectory(customTemplatePath);
            dirs = Directory.GetDirectories(customTemplatePath);
            foreach (var dir in dirs) {
                if (Path.GetFileName(dir) != "_common") {
                    res1.Add(Path.GetFileName(dir));
                    res2.Add(dir);
                }
            }

            return (res1, res2);
        }

        int FindResolution(int trueValue) {
            for (int i = 0; i < resolutionList.Count; i++) {
                int val = int.Parse(resolutionList[i]);
                if (val == trueValue) return i;
            }
            return -1;
        }

        public override void SetActive(bool active) {
            var cores = CoreManager.GetCores();
            coreList = cores.folders;
            if (coreList.Count == 0) {
                //no cores, cannot create a city
                base.SetActive(false);
                if (active && !warningShown) {
                    builder.CreateAlert(SM.Get("WARNING"), SM.Get("NO_CORES"), SM.Get("OK"), null, 220);
                    warningShown = true;
                }
                return;
            }
            coreField.SetOptions(cores.names);
            coreField.SetValue(0);
            SetCore(0);
            sizeField.SetValue(CityGroundHelper.terrainSize);
            resField.SetValue(FindResolution(CityGroundHelper.heightmapResolution));
            terrSize = CityGroundHelper.terrainSize;
            heightmapRes = CityGroundHelper.heightmapResolution;
            base.SetActive(active);
        }

        void SetTerrainSize(string value) {
            int val = int.Parse(value);
            terrSize = val;
        }

        void SetHeightmapResolution(int value) {
            int val = int.Parse(resolutionList[value]);
            heightmapRes = val;
        }

        void NewCity() {
            builder.fileBrowser.LoadFolder(SetupCity);
        }

        void SetupCity(string folder) {
            builder.helper.StartCoroutine(SetupCityCoroutine(folder));
        }

        IEnumerator SetupCityCoroutine(string folder) {
            var progressBar = builder.helper.curProgressBar;
            progressBar.SetActive(true);
            progressBar.SetProgress(0);
            progressBar.SetText(SM.Get("COPYING_TEMPLATE"));
            for (int i = 0; i < 2; i++) { //once only does not make the file dialog close
                yield return new WaitForEndOfFrame();
            }
            var path = new DirectoryInfo(Application.dataPath);
            var filesPath = Path.Combine(path.Parent.ToString(), "Files");
            var templatesPath = Path.Combine(filesPath, "cores", selectedCore, "templates");
            PathHelper.DirectoryCopy(Path.Combine(templatesPath, "_common"), folder);
            PathHelper.DirectoryCopy(selectedTemplate, folder);
            var savedCity = new IO.SavedCity.SavedCity();
            savedCity.heightmapResolution = heightmapRes;
            savedCity.terrainSize = terrSize;
            savedCity.heightMap = "";

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(savedCity, Newtonsoft.Json.Formatting.Indented);
            var bytes = System.Text.Encoding.ASCII.GetBytes(json);
            var stream = new MemoryStream(bytes);
            using (var compressedFileStream = File.Create(Path.Combine(folder, "city.json.gz"))) {
                using (var compressor = new GZipStream(compressedFileStream, CompressionMode.Compress)) {
                    stream.CopyTo(compressor);
                }
            }
            SetActive(false);
            CityImporter.LoadCity(GameObject.Find("CityGroundHelper").GetComponent<CityGroundHelper>().elementManager, folder + "/city.json.gz");
        }

        void Cancel() {
            SetActive(false);
        }
    }
}