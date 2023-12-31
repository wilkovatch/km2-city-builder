using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using SM = StringManager;
using CM = CoreManager;

namespace EditorPanels {
    public class SettingsEditorPanel : EditorPanel {
        EditorPanelElements.ScrollList cores;
        EditorPanelElements.InputField pythonPath, pythonPort;
        EditorPanelElements.Checkbox pythonDebug;
        EditorPanelElements.Button installBtn, uninstallBtn, checkUpdatesBtn, showInfoBtn, openCoreFolderBtn;
        List<CM.CoreInfo> curList;
        int curCoreI = -1;

        public override void Initialize(GameObject canvas) {
            pageButtonNames.AddRange(new string[] { SM.Get("PREF_TAB_CORES"), SM.Get("PREF_TAB_PYTHON") });
            Initialize(canvas, 2);
            var p0 = GetPage(0);
            var p1 = GetPage(1);
            var w = 1.5f;

            //Cores
            cores = p0.AddScrollList(SM.Get("PREF_CORES"), new List<string>(), x => Select(x), w);
            p0.IncreaseRow(5.0f);
            uninstallBtn = p0.AddButton(SM.Get("PREF_CORES_UNINSTALL"), UninstallCore, w / 2.0f);
            installBtn = p0.AddButton(SM.Get("PREF_CORES_INSTALL"), InstallCore, w / 2.0f);
            p0.IncreaseRow();
            checkUpdatesBtn = p0.AddButton(SM.Get("PREF_CORES_CHECK_UPDATES"), CheckCoreUpdates, w * 0.4f);
            showInfoBtn = p0.AddButton(SM.Get("PREF_CORES_SHOW_INFO"), ShowCoreInfo, w * 0.3f);
            openCoreFolderBtn = p0.AddButton(SM.Get("PREF_CORES_OPEN_FOLDER"), OpenCoreFolder, w * 0.3f);
            p0.IncreaseRow();
            p0.AddButton(SM.Get("PREF_CORES_ADD"), InputCoreRepo, w);
            p0.IncreaseRow();
            p0.AddButton(SM.Get("CLOSE"), delegate { SetActive(false); }, w);
            installBtn.SetInteractable(false);
            uninstallBtn.SetInteractable(false);
            checkUpdatesBtn.SetInteractable(false);
            showInfoBtn.SetInteractable(false);

            //Python
            pythonPath = p1.AddInputField(SM.Get("PREF_PYTHON_PATH"), SM.Get("PATH_PH"), "", InputField.ContentType.Standard, x => { SettingsManager.Set("pythonPath", x); }, w);
            p1.IncreaseRow();
            pythonPort = p1.AddInputField(SM.Get("PREF_PYTHON_PORT"), SM.Get("NUMBER_PH"), "", InputField.ContentType.IntegerNumber, x => { SettingsManager.Set("pythonPort", int.Parse(x)); }, w);
            p1.IncreaseRow();
            pythonDebug = p1.AddCheckbox(SM.Get("PREF_PYTHON_DEBUG"), false, x => { SettingsManager.Set("pythonDebug", x); }, w);
            p1.IncreaseRow();
            p1.AddButton(SM.Get("CLOSE"), delegate { SetActive(false); }, w);

            ReloadCoreList(cores, true);
        }

        bool OpenCityCheck() {
            if (PreferencesManager.workingDirectory != "") {
                builder.CreateAlert(
                    SM.Get("ERROR"),
                    SM.Get("CANNOT_MANAGE_CORES"),
                    SM.Get("OK")
                );
                return false;
            } else {
                return true;
            }
        }

        void UninstallCore() {
            if (!OpenCityCheck()) return;
            if (curCoreI == -1) return;
            var coreName = curList[curCoreI].folder;
            var curCore = PreferencesManager.Get("core", "");
            if (curCore == coreName) {
                builder.CreateAlert(
                    SM.Get("ERROR"),
                    SM.Get("PREF_UNINSTALL_CORE_ERROR"),
                    SM.Get("OK")
                );
            } else {
                builder.CreateAlert(
                    SM.Get("WARNING"),
                    SM.Get("PREF_UNINSTALL_CORE_WARNING"),
                    SM.Get("YES"),
                    SM.Get("NO"),
                    delegate {
                        CM.UninstallCore(
                            curList[curCoreI],
                            delegate { ReloadCoreList(cores, true); },
                            delegate {
                                builder.DoDelayed( delegate {
                                    builder.CreateAlert(
                                        SM.Get("ERROR"),
                                        SM.Get("PREF_UNINSTALL_CORE_ERROR_GENERIC"),
                                        SM.Get("OK")
                                    );
                                });
                            }
                        );
                    }
                );
            }
        }

        void InstallCore() {
            if (!OpenCityCheck()) return;
            if (curCoreI == -1) return;
            CM.InstallCore(curList[curCoreI], CoreInstalledPost, builder);
        }

        void CheckCoreUpdates() {
            if (!OpenCityCheck()) return;
            if (curCoreI == -1) return;
            var c = curList[curCoreI];
            CM.CheckUpdates(c, ShowCoreUpdates, builder);
        }

        void ShowCoreUpdates(CM.CoreInfo c, bool res, bool dirty) {
            if (res) {
                if (dirty) {
                    builder.CreateAlert(
                        SM.Get("PREF_CORES_UPDATE_DIRTY_TITLE"),
                        SM.Get("PREF_CORES_UPDATE_DIRTY_TEXT").Replace("$CORE", c.name),
                        SM.Get("OK"),
                        null,
                        250
                    );
                } else {
                    builder.CreateAlert(
                        SM.Get("PREF_CORES_UPDATE_AVAILABLE_TITLE"),
                        SM.Get("PREF_CORES_UPDATE_AVAILABLE_TEXT").Replace("$CORE", c.name),
                        SM.Get("YES"),
                        SM.Get("NO"),
                        delegate { CM.UpdateCore(curList[curCoreI], CoreUpdatedPost, builder); }
                    );
                }
            } else {
                builder.CreateAlert(
                    SM.Get("PREF_CORES_UPDATE_NOT_AVAILABLE_TITLE"),
                    SM.Get("PREF_CORES_UPDATE_NOT_AVAILABLE_TEXT").Replace("$CORE", c.name),
                    SM.Get("OK")
                );
            }
        }

        void CoreUpdatedPost(CM.CoreInfo c, bool res) {
            var oldIndex = curList.IndexOf(c);
            ReloadCoreList(cores, true);
            c = curList[oldIndex];
            string text, title;
            if (res) {
                title = SM.Get("PREF_CORES_UPDATE_DONE_TITLE");
                text = SM.Get("PREF_CORES_UPDATE_DONE_TEXT");
            } else {
                title = SM.Get("PREF_CORES_UPDATE_ERROR_TITLE");
                text = SM.Get("PREF_CORES_UPDATE_ERROR_TEXT");
            }
            builder.DoDelayed(delegate {
                builder.CreateAlert(
                    title,
                    text.Replace("$CORE", c.name).Replace("$VERSION", c.version),
                    SM.Get("OK")
                );
            });
        }

        void CoreInstalledPost(CM.CoreInfo c, bool res) {
            ReloadCoreList(cores, true);
            string text, title;
            if (res) {
                title = SM.Get("PREF_CORES_INSTALL_DONE_TITLE");
                text = SM.Get("PREF_CORES_INSTALL_DONE_TEXT");
            } else {
                title = SM.Get("PREF_CORES_INSTALL_ERROR_TITLE");
                text = SM.Get("PREF_CORES_INSTALL_ERROR_TEXT");
            }
            builder.CreateAlert(
                title,
                text,
                SM.Get("OK"),
                delegate {
                    builder.DoDelayed(delegate {
                        PythonManager.CheckPythonForCore(builder, c.folder, null);
                    });
                }
            );
        }

        void ShowCoreInfo() {
            if (curCoreI == -1) return;
            var c = curList[curCoreI];
            var repoInfo = c.repo != null ? (SM.Get("PREF_CORES_INFO_REPO") + c.repo) : "";
            builder.CreateAlert(
                c.name,
                SM.Get("PREF_CORES_INFO_FOLDER") + c.folder + "\n" +
                SM.Get("PREF_CORES_INFO_VERSION") + c.version + "\n" +
                repoInfo,
                SM.Get("OK"),
                null,
                280
            );
        }

        protected void InputCoreRepo() {
            if (!OpenCityCheck()) return;
            builder.CreateInput(
                SM.Get("PREF_CORE_INPUT_TITLE"),
                SM.Get("PREF_CORE_INPUT_PH"),
                SM.Get("PREF_CORE_INPUT_ADD"),
                SM.Get("CANCEL"),
                str => { CM.AddCore(str, CoreInstalledPost, builder); }
            );
        }

        void OpenCoreFolder() {
            if (curCoreI == -1) return;
            var c = curList[curCoreI];
            System.Diagnostics.Process.Start(CM.GetCorePath(c)); //TODO: check if it works on Mac and Linux
        }

        void Select(int i) {
            curCoreI = i;
            if (i >= 0) {
                var installed = curList[i].installed;
                var hasRepo = curList[i].repo != null;
                installBtn.SetInteractable(!installed);
                uninstallBtn.SetInteractable(installed);
                checkUpdatesBtn.SetInteractable(installed && hasRepo);
                showInfoBtn.SetInteractable(installed);
                openCoreFolderBtn.SetInteractable(installed);
            } else {
                installBtn.SetInteractable(false);
                uninstallBtn.SetInteractable(false);
                checkUpdatesBtn.SetInteractable(false);
                showInfoBtn.SetInteractable(false);
                openCoreFolderBtn.SetInteractable(false);
            }
        }

        void ReloadCoreList(EditorPanelElements.ScrollList cores, bool deselect) {
            cores.Deselect();
            var lst = CM.GetCores(true).cores;
            if (lst != null) {
                var items = new List<string>();
                var colors = new List<Color>();
                for (int i = 0; i < lst.Count; i++) {
                    items.Add(lst[i].name);
                    colors.Add(lst[i].installed ? (lst[i].stock ? Color.black : Color.blue) : Color.gray);
                }
                cores.SetItems(items, null, colors);
            } else {
                cores.SetItems(new List<string>());
            }
            if (deselect) Select(-1);
            curList = lst;
        }

        public override void SetActive(bool active) {
            if (active) {
                pythonPath.SetValue(SettingsManager.Get<string>("pythonPath", null));
                pythonPort.SetValue("" + SettingsManager.Get("pythonPort", 5005));
                pythonDebug.SetValue(SettingsManager.Get("pythonDebug", false));
            }
            base.SetActive(active);
        }
    }
}
