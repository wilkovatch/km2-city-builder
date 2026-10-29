using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Godot;

using GameObject = Godot.Control;
using SM = StringManager;

namespace EditorPanels {
    public class NewCityEditorPanel : EditorPanel {
        EditorPanelElements.InputField sizeField, maxHeightField;
        EditorPanelElements.Dropdown resField, coreField, templateField;
        EditorPanelElements.Button createButton;
        int terrSize, heightmapRes;
        float maxHeight;
        string selectedCore, selectedTemplate;
        List<string> resolutionList = new List<string> { "128", "256", "512", "1024", "2048", "4096" };
        List<string> coreList, templateList;
        static bool warningShown = false;

        public override void Initialize(GameObject canvas) {
            Initialize(canvas, 1);
            var p0 = GetPage(0);
            var width = 2.0f;
            sizeField = p0.AddInputField(SM.Get("NC_SIZE"), SM.Get("SIZE_PH"), "4096", GameEngine.InputContentType.Decimal, SetTerrainSize, width / 2);
            resField = p0.AddDropdown(SM.Get("NC_HM_RES"), resolutionList, SetHeightmapResolution, width / 2);
            p0.IncreaseRow();
            maxHeightField = p0.AddInputField(SM.Get("NC_MAX_HEIGHT"), SM.Get("VALUE_PH"), "1000", GameEngine.InputContentType.Decimal, SetTerrainMaxHeight, width);
            p0.IncreaseRow();
            coreField = p0.AddDropdown(SM.Get("NC_CORES"), new List<string>(), SetCore, width);
            p0.IncreaseRow();
            templateField = p0.AddDropdown(SM.Get("NC_TEMPLATES"), new List<string>(), SetTemplate, width);
            p0.IncreaseRow();
            createButton = p0.AddButton(SM.Get("NC_CREATE"), NewCity, width);
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
            createButton.SetInteractable(selectedCore != null);
        }

        void SetTemplate(int value) {
            var val = templateList[value];
            selectedTemplate = val;
        }

        (List<string> names, List<string> folders) GetTemplates() {
            if (selectedCore == null) {
                return (new List<string>() { SM.Get("NONE") }, new List<string>() { "NONE" });
            }
            return Operations.CityFiles.GetTemplates(selectedCore);
        }

        int FindResolution(int trueValue) {
            for (int i = 0; i < resolutionList.Count; i++) {
                int val = int.Parse(resolutionList[i]);
                if (val == trueValue) return i;
            }
            return -1;
        }

        public override void SetActive(bool active) {
            if (!PrepareActivation(active)) return;
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
            coreList.Insert(0, null);
            var fullNames = new List<string>(cores.names);
            fullNames.Insert(0, SM.Get("SELECT_CORE"));
            coreField.SetOptions(fullNames);
            coreField.SetValue(0);
            SetCore(0);
            sizeField.SetValue(CityGroundHelper.terrainSize);
            resField.SetValue(FindResolution(CityGroundHelper.heightmapResolution));
            maxHeightField.SetValue(CityGroundHelper.maxHeight);
            terrSize = CityGroundHelper.terrainSize;
            heightmapRes = CityGroundHelper.heightmapResolution;
            maxHeight = CityGroundHelper.maxHeight;
            base.SetActive(active);
        }

        void SetTerrainSize(string value) {
            int val = int.Parse(value);
            terrSize = val;
        }

        void SetTerrainMaxHeight(string value) {
            float val = float.Parse(value);
            maxHeight = val;
        }

        void SetHeightmapResolution(int value) {
            int val = int.Parse(resolutionList[value]);
            heightmapRes = val;
        }

        void NewCity() {
            builder.fileBrowser.LoadFolder(SetupCity);
        }

        void SetupCity(string folder) {
            if (selectedCore == null) return;
            SetupCityCoroutine(folder, builder);
        }


        public async void SetupCityCoroutine(string folder, Node caller) {
            var progressBar = builder.helper.curProgressBar;
            progressBar.SetActive(true);
            progressBar.SetProgress(0);
            progressBar.SetText(SM.Get("COPYING_TEMPLATE"));
            for (int i = 0; i < 2; i++) { //once only does not make the file dialog close
                await caller.ToSignal(caller.GetTree(), "process_frame");
            }
            Operations.CityFiles.CreateCityFolder(folder, selectedCore, selectedTemplate, terrSize, heightmapRes, maxHeight);
            SetActive(false);
            CityImporter.LoadCity(builder.helper.elementManager, folder + "/city.json.gz");
        }

        void Cancel() {
            SetActive(false);
        }
    }
}
