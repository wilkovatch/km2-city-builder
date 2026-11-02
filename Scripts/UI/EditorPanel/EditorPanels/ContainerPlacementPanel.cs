using System;
using System.Collections.Generic;
using CityElements.Types;
using GO = Godot.Control;

namespace EditorPanels {
    //Click-add to an array selected by a core command, without navigating through the city-property panels.
    public class ContainerPlacementPanel : EditorPanel {
        public string containerId;
        PropertyContainerCommand currentCommand;
        public PropertyContainerCommand command {
            get => currentCommand;
            set { currentCommand = value; choice = 0; }
        }
        int choice;

        public override void Initialize(GO canvas) {
            Initialize(canvas, 1);
            if (command == null) return;
            var page = GetPage(0);
            page.AddLabel(StringManager.Get(command.label), 1.5f);
            page.IncreaseRow();
            if (command.alternatePaths?.Length > 0) {
                var target = page.AddDropdown(StringManager.Get("CONTAINER_PLACEMENT_TARGET"), new List<string>(Array.ConvertAll(command.alternateLabels, StringManager.Get)),
                    SelectTarget, 1.5f);
                target.actionEnabled = false;
                target.SetValue(choice);
                target.actionEnabled = true;
                page.IncreaseRow();
            }
            page.AddLabel(StringManager.Get("CE_CLICK_TO_CREATE"), 1.5f);
            page.IncreaseRow();
            page.AddButton(StringManager.Get("END_EDITING"), Terminate, 1.5f);
        }

        (ObjectState owner, Parameter parameter) Target(bool create) {
            var entry = Operations.PropertyContainers.Find(containerId);
            if (entry == null) return (null, null);
            var path = command.alternatePaths?.Length > 0 ? command.alternatePaths[choice] : command.path;
            var state = entry.state;
            var parameters = FindType()?.parameters;
            var parts = path.Split('.');
            for (int i = 0; i < parts.Length; i++) {
                var param = Array.Find(parameters ?? Array.Empty<Parameter>(), p => p.fullName() == parts[i]);
                if (param == null) return (null, null);
                if (i == parts.Length - 1) return (state, param);
                state = create ? Operations.States.EnsureCustomCityProperties(state, param) : state?.State(param.fullName());
                parameters = Operations.States.GetSubparams(param);
            }
            return (null, null);
        }

        CustomCityPropertiesType FindType() {
            var category = Operations.PropertyContainers.Find(containerId)?.settings;
            foreach (var type in CityElements.Types.Parsers.TypeParser.GetCustomCityPropertiesTypes().Values) {
                if (type.elementContainer == category) return type;
            }
            return null;
        }

        internal void SelectTarget(int index) {
            if (index < 0 || index >= (command?.alternatePaths?.Length ?? 0)) return;
            choice = index;
            Start();
        }

        void Start() {
            if (command == null || builder?.arrayPointPlacer == null) return;
            var param = Target(false).parameter;
            if (param == null) return;
            builder.arrayPointPlacer.Start(this, () => {
                if (!Operations.PropertyContainers.IsCurrent(containerId)) return null;
                History.UndoManager.Touch(builder.helper.elementManager.GetDummy<CityProperties>());
                return Target(true).owner;
            },
                param, () => builder.helper.elementManager.GetDummy<CityProperties>());
        }

        public override void SetActive(bool active) {
            if (active && command != null) Initialize(lastCanvas);
            base.SetActive(active);
            if (active) Start();
            else if (builder?.arrayPointPlacer?.IsPlacingFrom(this) == true) builder.arrayPointPlacer.Stop();
        }
    }
}
