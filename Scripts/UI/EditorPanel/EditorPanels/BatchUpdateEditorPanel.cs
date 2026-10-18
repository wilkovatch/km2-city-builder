using System.Collections;
using System.Collections.Generic;
using GameObject = Godot.Control;
using SM = StringManager;
using System.Text.RegularExpressions;

namespace EditorPanels {
    public class BatchUpdateEditorPanel : EditorPanel {
        string updateExpr = "";
        string whereExpr = "";
        int curType = 0;

        public override void Initialize(GameObject canvas) {
            Initialize(canvas, 1);
            var p0 = GetPage(0);
            var objTypes = new List<string> {
                SM.Get("BATCH_TYPE_ROAD"),
                SM.Get("BATCH_TYPE_INTERSECTION"),
                SM.Get("BATCH_TYPE_TERRAIN_PATCH"),
                SM.Get("BATCH_TYPE_BUILDING_LINE"),
                SM.Get("BATCH_TYPE_BUILDING"),
                SM.Get("BATCH_TYPE_BUILDING_SIDE"),
                SM.Get("BATCH_TYPE_MESH_INSTANCE")
            };
            p0.AddDropdown(SM.Get("BATCH_TYPE"), objTypes, x => curType = x, 1.5f);
            p0.IncreaseRow();
            p0.AddButton(SM.Get("BATCH_PARAM_LIST"), ShowParameters, 1.5f);
            p0.IncreaseRow();
            p0.AddInputField(SM.Get("BATCH_UPDATE_EXPR"), SM.Get("EXPR_PH"), "", GameEngine.InputContentType.Standard, x => updateExpr = x, 1.5f);
            p0.IncreaseRow();
            p0.AddInputField(SM.Get("BATCH_WHERE_EXPR"), SM.Get("EXPR_PH"), "", GameEngine.InputContentType.Standard, x => whereExpr = x, 1.5f);
            p0.IncreaseRow();
            p0.AddButton(SM.Get("BATCH_EXEC"), Exec, 1.5f);
            p0.IncreaseRow();
            p0.AddButton(SM.Get("CLOSE"), Terminate, 1.5f);
        }

        Dictionary<string, List<(string name, string type)>> GetParameters() {
            return Operations.City.GetBatchParameters((Operations.City.BatchKind)curType);
        }

        void ShowParameters() {
            ShowParametersPage(0, 0);
        }

        void ShowParametersNextPage(int page, int num) {
            builder.DoDelayed(delegate { ShowParametersPage(page, num); }); //the previous alert must be closed
        }

        void ShowParametersPage(int page, int num) {
            int height = 600;
            int pageParams = height / 30;
            var parameters = GetParameters();
            if (page >= parameters.Count) return;
            var last = page == parameters.Count - 1;
            var text = "";
            var keys = new List<string>(parameters.Keys);
            var key = keys[page];
            var lst = parameters[key];
            lst.Sort();
            for (int i = num; i < num + pageParams && i < lst.Count; i++) {
                var p = lst[i];
                text += p.Item1 + " (" + p.Item2 + ")\n";
            }
            if (num + pageParams < lst.Count) {
                text += SM.Get("BATCH_PARAM_LIST_CONTINUES") + "\n";
                last = false;
            }
            var title = SM.Get("BATCH_PARAM_LIST_TITLE") + key;
            if (last) {
                builder.CreateAlert(title, text, SM.Get("CLOSE"), null, height, true, 600.0f);
            } else {
                var nextPage = page + 1;
                var nextNum = 0;
                if (num + pageParams < lst.Count) {
                    nextPage = page;
                    nextNum = num + pageParams;
                }
                builder.CreateAlert(title, text, SM.Get("BATCH_NEXT_PAGE"), SM.Get("CLOSE"), delegate { ShowParametersNextPage(nextPage, nextNum); }, null, height, true, 600.0f);
            }
        }

        void Exec() {
            try {
                Operations.City.BatchUpdate(builder.helper.elementManager, (Operations.City.BatchKind)curType, updateExpr, whereExpr);
                builder.NotifyChange();
                builder.CreateAlert(SM.Get("SUCCESS"), SM.Get("BATCH_SUCCESS"), SM.Get("OK"));
            } catch (Operations.City.BatchException e) {
                switch (e.part) {
                    case Operations.City.BatchException.Part.Update:
                        builder.CreateAlert(SM.Get("ERROR"), SM.Get("BATCH_ERROR_UPDATE"), SM.Get("OK"));
                        break;
                    case Operations.City.BatchException.Part.Where:
                        builder.CreateAlert(SM.Get("ERROR"), SM.Get("BATCH_ERROR_WHERE"), SM.Get("OK"));
                        break;
                    default:
                        builder.NotifyChange(); //the elements updated before the error
                        builder.CreateAlert(SM.Get("ERROR"), SM.Get("BATCH_ERROR_RUNTIME"), SM.Get("OK"));
                        break;
                }
            }
        }
    }
}