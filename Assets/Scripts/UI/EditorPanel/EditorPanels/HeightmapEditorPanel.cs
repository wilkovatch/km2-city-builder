using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using SM = StringManager;

namespace EditorPanels {
    public class HeightmapEditorPanel : EditorPanel {
        float scale = 5.0f;

        EditorPanelElements.TextureField texField;
        string selectedHeightmap;

        public override void Initialize(GameObject canvas) {
            Initialize(canvas, 1);
            var p0 = GetPage(0);
            p0.AddInputField(SM.Get("HM_Y_SCALE"), SM.Get("HM_Y_SCALE_PH"), "1.0", UnityEngine.UI.InputField.ContentType.DecimalNumber, delegate (string value) { scale = float.Parse(value); });
            p0.IncreaseRow();
            texField = p0.AddTextureField(builder, SM.Get("HM_GRAYSCALE_TEX"), SM.Get("TEXTURE_PH"), "", x => selectedHeightmap = x, delegate { return selectedHeightmap; }, x => selectedHeightmap = x);
            p0.IncreaseRow();
            p0.AddButton(SM.Get("LOAD"), ApplyHeightmap);
            p0.IncreaseRow();
            p0.AddButton(SM.Get("CANCEL"), delegate () { SetActive(false); });
        }

        void ApplyHeightmap() {
            SetActive(false);
            var filename = PathHelper.FindInFolders(selectedHeightmap);
            if (!File.Exists(filename)) {
                builder.CreateAlert(SM.Get("ERROR"), SM.Get("HEIGHTMAP_NOT_FOUND_ERROR"), SM.Get("OK"));
                return;
            }
            builder.helper.ApplyHeightmap(filename, scale);
        }

        public override void SetActive(bool active) {
            if (active) {
                selectedHeightmap = "";
                texField.SetValue("");
            }
            base.SetActive(active);
        }
    }
}