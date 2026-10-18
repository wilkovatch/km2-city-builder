using System.Collections;
using System.Collections.Generic;
using System.IO;
using Godot;
using GameObject = Godot.Control;
using SM = StringManager;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;

namespace EditorPanels {
    public class WholeCityRotationEditorPanel : EditorPanel {
        int curRotation = 0;
        EditorPanelElements.Dropdown dropdown;

        public override void Initialize(GameObject canvas) {
            Initialize(canvas, 1);
            var p0 = GetPage(0);
            var entries = new List<string>() {
                SM.Get("CITY_ROTATION_ANGLES_SELECT"),
                SM.Get("CITY_ROTATION_ANGLES_90_CCW"),
                SM.Get("CITY_ROTATION_ANGLES_90_CW"),
                SM.Get("CITY_ROTATION_ANGLES_180"),
            };
            dropdown = p0.AddDropdown(SM.Get("CITY_ROTATION_ANGLES"), entries, SetRotation, 1.5f);
            p0.IncreaseRow();
            p0.AddButton(SM.Get("CITY_ROTATION_ROTATE"), Rotate, 0.75f);
            p0.AddButton(SM.Get("CANCEL"), Cancel, 0.75f);
            p0.IncreaseRow();
        }

        void SetRotation(int i) {
            curRotation = i;
        }

        public override void SetActive(bool active) {
            base.SetActive(active);
            dropdown.SetValue(0);
            curRotation = 0;
        }

        void Cancel() {
            SetActive(false);
        }

        void Rotate() {
            if (curRotation == 0) {
                builder.CreateAlert(SM.Get("ERROR"), SM.Get("CITY_ROTATION_ANGLES_MISSING"), SM.Get("OK"));
            } else {
                RotateCity(curRotation);
            }
        }

        async void RotateCity(int rotation) {
            SetActive(false);
            await Operations.City.Rotate(builder.helper.elementManager, rotation);
            builder.NotifyChange();
            builder.CreateAlert(SM.Get("SUCCESS"), SM.Get("CITY_ROTATED"), SM.Get("OK"));
        }
    }
}