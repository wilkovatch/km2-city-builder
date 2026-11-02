using GameObject = Godot.Control;
using SM = StringManager;

namespace EditorPanels {
    //The list of an array parameter with subPanel, opened from the panel of the element it belongs to (which edits the same state)
    public class ArrayListPanel : EditorPanel {
        CityElements.Types.Parameter param;
        System.Func<IObjectWithState> getOwner;

        public void InitializeWithData(GameObject canvas, CityElements.Types.Parameter newParam, System.Func<IObjectWithState> newGetOwner) {
            param = newParam;
            getOwner = newGetOwner;
            parentParamPrefix = null; //the array is a parameter of the parent, this panel is not a level of the path
            Initialize(canvas);
        }

        public override void Initialize(GameObject canvas) {
            Initialize(canvas, 1);
            getObject = getOwner;
            if (param == null) return;
            var p = GetPage(0);
            var curW = 0.0f;
            AddParameters(p, ref curW, 1.5f, new[] { param }, new CityElements.Types.TabElement { width = 1.5f, name = param.fullName() }, getOwner, false);
            p.AddButton(SM.Get("END_EDITING"), Terminate, 0.75f);
            p.AddButton(SM.Get("ARRAY_ELEM_GO_UP"), GoUp, 0.75f);
        }

        public override System.Func<EditorPanel, EditorPanel> CaptureReopen() {
            var name = param?.fullName();
            if (name == null) return null;
            return parent => parent.OpenArrayList(name);
        }
    }
}
