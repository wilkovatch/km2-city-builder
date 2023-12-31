using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using SM = StringManager;

namespace EditorPanels {
    public class WholeCityTerrainEditorPanel : EditorPanel {
        float distance = 5.0f;
        float segmentLength = 100.0f;
        float vertexFusionDistance = 0.0f;
        int smoothing = 0;
        float internalDistance = 0.0f;
        EditorPanelElements.TextureField texField;

        public override void Initialize(GameObject canvas) {
            Initialize(canvas, 1);
            var p0 = GetPage(0);
            p0.AddLabel(SM.Get("CT_OUTER_PATCH_PARAMS"), 1.5f, SM.Get("CT_OUTER_PATCH_TOOLTIP"), null, 0.4f);
            p0.IncreaseRow(0.4f);
            p0.AddInputField(SM.Get("CT_THICKNESS"), SM.Get("DISTANCE_PH"), "5.0", UnityEngine.UI.InputField.ContentType.DecimalNumber,
                delegate (string value) { distance = float.Parse(value); }, 0.5f, SM.Get("CT_OUTER_PATCH_THICKNESS_TOOLTIP"));
            p0.AddInputField(SM.Get("CT_SEGMENT_LENGTH"), SM.Get("LENGTH_PH"), "100.0", UnityEngine.UI.InputField.ContentType.DecimalNumber,
                delegate (string value) { segmentLength = float.Parse(value); }, 0.5f, SM.Get("CT_OUTER_PATCH_SEGMENT_LENGTH_TOOLTIP"));
            p0.AddInputField(SM.Get("CT_FUSION_DIST"), SM.Get("DISTANCE_PH"), "0.0", UnityEngine.UI.InputField.ContentType.DecimalNumber,
                delegate (string value) { vertexFusionDistance = float.Parse(value); }, 0.5f, SM.Get("CT_OUTER_PATCH_FUSION_DISTANCE_TOOLTIP"));
            p0.IncreaseRow();
            p0.AddLabel(SM.Get("CT_ALL_PATCH_PARAMS"), 1.5f, null, null, 0.4f);
            p0.IncreaseRow(0.4f);
            p0.AddInputField(SM.Get("CT_SMOOTHING"), SM.Get("CT_SMOOTHING_PH"), "0", UnityEngine.UI.InputField.ContentType.IntegerNumber,
                delegate (string value) { smoothing = int.Parse(value); }, 0.75f, null);
            p0.AddInputField(SM.Get("CT_INT_PT_DIST"), SM.Get("DISTANCE_PH"), "0.0", UnityEngine.UI.InputField.ContentType.DecimalNumber,
                delegate (string value) { internalDistance = float.Parse(value); }, 0.75f, SM.Get("CT_INT_PT_DIST_TOOLTIP"));
            p0.IncreaseRow();
            texField = p0.AddTextureField(builder, SM.Get("TEXTURE"), SM.Get("TEXTURE_PH"), null, x => { PreferencesManager.Set("curTerrainTexture", x); }, delegate { return PreferencesManager.Get("curTerrainTexture", ""); }, x => { PreferencesManager.Set("curTerrainTexture", x); }, 1.5f, SM.Get("CT_NO_TEXTURE_TOOLTIP"));
            p0.IncreaseRow();
            p0.IncreaseRow();
            p0.AddButton(SM.Get("CT_CREATE"), CreateCityTerrain, 1.5f);
            p0.IncreaseRow();
            p0.AddButton(SM.Get("CT_ERASE"), EraseCityTerrain, 1.5f);
            p0.IncreaseRow();
            p0.AddButton(SM.Get("CANCEL"), Cancel, 1.5f);
            p0.IncreaseRow();
        }

        public override void SetActive(bool active) {
            texField.SetValue(PreferencesManager.Get("curTerrainTexture", ""));
            base.SetActive(active);
        }

        void Cancel() {
            SetActive(false);
        }

        void CreateCityTerrain() {
            /*if (texture == null) {
                builder.CreateAlert(SM.Get("ERROR"), SM.Get("CT_NO_TEXTURE"), SM.Get("OK"));
                return;
            }*/
            builder.helper.elementManager.CreateCityTerrain(builder.gameObject, distance, segmentLength, vertexFusionDistance, smoothing, internalDistance);
        }

        void EraseCityTerrain() {
            builder.CreateAlert(SM.Get("WARNING"), SM.Get("CT_ERASE_WARNING"), SM.Get("YES"), SM.Get("NO"), builder.helper.elementManager.EraseCityTerrain);
        }
    }
}