using System.Collections.Generic;
using Godot;

//Updates the handles and the point links every frame in place of their own _Process: there can be tens of thousands of them
//(e.g. when the terrain anchors are shown), and a script callback per node per frame is costly in Godot.
//Only the enabled ones are registered (they track their enabled state), so the hidden ones cost nothing.
//It runs after the other nodes, handles before links, as links follow the
//scale of their handle.
public partial class HandleUpdater : Node {
    public interface IUpdatable {
        int UpdaterIndex { get; set; }
        void UpdateFrame();
    }

    static HandleUpdater instance = null;
    static readonly List<IUpdatable> handles = new List<IUpdatable>();
    static readonly List<IUpdatable> links = new List<IUpdatable>();

    public static void Register(Handle handle) {
        Register(handles, handle, handle);
    }

    public static void Unregister(Handle handle) {
        Unregister(handles, handle);
    }

    public static void Register(PointLink link) {
        Register(links, link, link);
    }

    public static void Unregister(PointLink link) {
        Unregister(links, link);
    }

    static void Register(List<IUpdatable> list, IUpdatable item, Node node) {
        if (item.UpdaterIndex >= 0) return;
        item.UpdaterIndex = list.Count;
        list.Add(item);
        if (!IsInstanceValid(instance)) {
            instance = new HandleUpdater { Name = "HandleUpdater", ProcessPriority = int.MaxValue };
            //the tree can be busy adding the registering node
            node.GetTree().Root.CallDeferred(Node.MethodName.AddChild, instance);
        }
    }

    //swap with the last one, there can be thousands of removals at once
    static void Unregister(List<IUpdatable> list, IUpdatable item) {
        var index = item.UpdaterIndex;
        if (index < 0) return;
        var last = list[list.Count - 1];
        list[index] = last;
        last.UpdaterIndex = index;
        list.RemoveAt(list.Count - 1);
        item.UpdaterIndex = -1;
    }

    //backwards, as an update can unregister items (e.g. a handle hiding itself): the removed one is replaced by the last,
    //which was already updated
    static void UpdateAll(List<IUpdatable> list) {
        for (int i = list.Count - 1; i >= 0; i--) {
            if (i < list.Count) list[i].UpdateFrame();
        }
    }

    public override void _Process(double delta) {
        UpdateAll(handles);
        UpdateAll(links);
    }
}
