using System.Collections.Generic;
using GameObject = Godot.Control;
using PC = Operations.PropertyContainers;
using SM = StringManager;

namespace EditorPanels {
    //A list of the elements owned by a city-property group (see PropertyContainerContentGroup), opened from its panel
    public class ContainerContentsPanel : EditorPanel {
        public string containerId;
        public int groupIndex;
        EditorPanelElements.ScrollList list;
        List<Godot.Node3D> owned = new();
        int selected = -1;

        CityElements.Types.PropertyContainerContentGroup Group() {
            var groups = PC.ContentGroups(PC.Find(containerId)?.settings);
            return groupIndex >= 0 && groupIndex < groups.Length ? groups[groupIndex] : null;
        }

        public override void Initialize(GameObject canvas) {
            Initialize(canvas, 1);
            var group = Group();
            if (group == null) return;
            var p = GetPage(0);
            owned = PC.Contents(containerId, group);
            selected = -1;
            list = p.AddScrollList(SM.Get(group.label), owned.ConvertAll(GameEngine.GameObject.GetName), i => selected = i, 1.5f);
            p.IncreaseRow(5);
            p.AddButton(SM.Get("EDIT"), Edit, 0.75f);
            p.AddButton(SM.Get("DELETE"), Delete, 0.75f);
            p.IncreaseRow();
            if (group.hint != null) {
                p.AddLabel(SM.Get(group.hint), 1.5f);
                p.IncreaseRow();
            }
            p.AddButton(SM.Get("END_EDITING"), Terminate, 0.75f);
            p.AddButton(SM.Get("ARRAY_ELEM_GO_UP"), GoUp, 0.75f);
        }

        void Edit() {
            if (selected < 0 || selected >= owned.Count) return;
            var node = owned[selected];
            if (node is MeshInstance) {
                //as when opened from the parent panel (EditorPanel.EditObjectInstanceParameter)
                builder.SetTransformPanelParent(this);
                Hide(true);
                builder.SelectObject(node);
                if (!IsOpen()) Hide(true);
            } else {
                var group = groupIndex;
                builder.SelectObject(node);
                builder.customElementEditorPanel.contentsGroup = group;
            }
        }

        void Delete() {
            if (selected < 0 || selected >= owned.Count) return;
            var node = owned[selected];
            History.UndoManager.Touch(node); //the undo target of the panel is the city properties
            if (node is CustomElement element) Operations.CustomElements.Delete(builder.helper.elementManager, element);
            else if (node is MeshInstance mesh) Operations.Meshes.Delete(mesh);
            builder.NotifyChange();
            Update();
        }

        //the list follows the contents (e.g. an undo restores an element after reopening the panel)
        public override void Update() {
            var group = Group();
            if (group == null || list == null) return;
            var current = PC.Contents(containerId, group);
            if (current.Count == owned.Count && current.TrueForAll(owned.Contains)) return;
            owned = current;
            selected = -1;
            list.Deselect();
            list.SetItems(owned.ConvertAll(GameEngine.GameObject.GetName));
        }

        public override System.Func<EditorPanel, EditorPanel> CaptureReopen() {
            var group = groupIndex;
            return parent => (parent as ArrayElemEditorPanel)?.OpenContents(group);
        }

        public override void SetActive(bool active) {
            if (active && containerId != null) Initialize(lastCanvas); //the contents may have changed while it was hidden
            base.SetActive(active);
        }
    }
}
