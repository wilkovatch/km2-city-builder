using System.Collections;
using System.Collections.Generic;
using Godot;
using Input = GameEngine.Input;
using KeyCommand = GameEngine.Input.KeyCommand;

/// <summary>
/// Per-frame driver of a ScrollList : keeps the pooled rows in sync with
/// the scroll position, loads at most one row thumbnail per frame and handles up/down arrow selection.
/// </summary>
public partial class ScrollListComponent : Node {
    public EditorPanelElements.ScrollList list = null;

    Queue<(int ID, string newImg, int newVal)> newSprites = new Queue<(int, string, int)>();
    Dictionary<int, TextureRect> rowImages = new Dictionary<int, TextureRect>();
    Dictionary<int, int> curRowsValues = new Dictionary<int, int>();

    public void SetupSprite(int rowID, TextureRect image) {
        rowImages[rowID] = image;
        curRowsValues[rowID] = 0;
    }

    public void ClearSprite(int ID) {
        // Invalidates any pending load for this row.
        curRowsValues[ID]++;
        rowImages[ID].Texture = null;
    }

    public void EnqueueNewSprite(int ID, string image) {
        ClearSprite(ID);
        newSprites.Enqueue((ID, image, curRowsValues[ID]));
    }

    void ProcessSprite() {
        var done = false;
        while (!done && newSprites.Count > 0) {
            var newSprite = newSprites.Dequeue();
            if (curRowsValues[newSprite.ID] == newSprite.newVal) {
                rowImages[newSprite.ID].Texture = UILayout.LoadPreview(newSprite.newImg);
                done = true;
            }
        }
    }

    public override void _Process(double delta) {
        if (list == null) return;
        // Skip hidden lists.
        if (GetParent() is Control parent && !parent.IsVisibleInTree()) return;
        list.UpdateRows();
        ProcessSprite();
        if (Input.GetInputStart(KeyCommand.Down)) {
            list.ManualScroll(true);
        } else if (Input.GetInputStart(KeyCommand.Up)) {
            list.ManualScroll(false);
        }
    }
}
