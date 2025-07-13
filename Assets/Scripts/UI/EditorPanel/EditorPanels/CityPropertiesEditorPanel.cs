using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using SM = StringManager;

namespace EditorPanels {
    public class CityPropertiesEditorPanel : EditorPanel {

        public override void Initialize(GameObject canvas) {
            InitializeWithCustomParameters<CityElements.Types.Runtime.CitySettings, CityElements.Types.CitySettings.CitySettings>(canvas, GetCurCity, null,
                "city", CityElements.Types.Parsers.TypeParser.GetCitySettings, null, false, 1.5f, false);
            var p0 = GetPage(0);
            p0.IncreaseRow();
            p0.AddButton(SM.Get("CLOSE"), Terminate, 1.5f);
        }

        CityProperties GetCurCity() {
            return builder.helper.elementManager.GetDummy<CityProperties>();
        }
    }
}
