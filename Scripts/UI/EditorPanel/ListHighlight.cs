using System.Collections.Generic;
using Godot;
using SelectionOutline = RuntimeGizmos.SelectionOutline;

//Outlines (in a colour of its own, on a free render layer) what is selected in the lists of the panels showing parts of
//the city, e.g. the checkpoints of a race. The lists are followed rather than notified, so what is deselected, or in a
//list hidden with its panel or tab, or freed, stops being outlined.
public class ListHighlight {
    const uint Layer = 1u << 16;
    readonly List<(EditorPanelElements.ScrollList list, System.Func<int, Node> itemNode)> lists = new();
    SelectionOutline outline;
    List<GeometryInstance3D> outlined = new();

    //itemNode: the node of an item of the list (the renderers under it are outlined), null if it has none
    public void Add(EditorPanelElements.ScrollList list, System.Func<int, Node> itemNode) {
        lists.Add((list, itemNode));
    }

    //called every frame, as the renderers of the items change with their geometry (and GO.SetLayer resets the layers)
    public void Update(Node gizmo) {
        foreach (var render in outlined) SelectionOutline.RemoveFromMask(render, Layer);
        outlined = new List<GeometryInstance3D>();
        lists.RemoveAll(x => x.list.IsFreed());
        foreach (var (list, itemNode) in lists) {
            if (list.IsShown() && list.Selected >= 0) outlined.AddRange(SelectionOutline.GetRenderers(itemNode(list.Selected)));
        }
        if (outline == null) {
            if (outlined.Count == 0 || gizmo == null) return;
            outline = new SelectionOutline {
                Name = "ContentOutline",
                maskLayer = Layer,
                outlineColor = new Color(0.0f, 0.85f, 1.0f)
            };
            gizmo.AddChild(outline);
        }
        outline.UpdateOutline(outlined);
    }
}
