using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace EditorPanelElements {
    public class Button : EditorPanelElement {
        System.Action action;
        UnityEngine.UI.Button button;

        protected override string TemplateName() {
            return "Button";
        }

        protected override string TitlePath() {
            return "Button/Text";
        }

        public Button(string title, Action action, GameObject parent, Vector2 pos, float widthFactor = 1.0f, string tooltip = null, string tag = null)
        : base(title, parent, pos, widthFactor, tooltip, tag) {
            this.action = action;
            button = obj.transform.Find("Button").GetComponent<UnityEngine.UI.Button>();
            button.onClick.AddListener(delegate {
                Select();
            });
        }

        void Select() {
            if (actionEnabled) action.Invoke();
        }

        public override void SetInteractable(bool interactable) {
            button.interactable = interactable;
        }

        public override bool GetInteractable() {
            return button.interactable;
        }
    }
}