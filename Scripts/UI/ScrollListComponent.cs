using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Input = GameEngine.Input;
using KeyCommand = GameEngine.Input.KeyCommand;

/// <summary>
/// Per-frame driver of a ScrollList : keeps the pooled rows in sync with
/// the scroll position, loads at most one row thumbnail per frame and handles up/down arrow selection.
/// </summary>
public partial class ScrollListComponent : Node {
    public EditorPanelElements.ScrollList list = null;
    public System.Func<string, Task<Texture2D>> thumbnailProvider;
    bool loadingSprite;

    Queue<(int ID, string newImg, int newVal)> newSprites = new Queue<(int, string, int)>();
    Dictionary<int, TextureRect> rowImages = new Dictionary<int, TextureRect>();
    Dictionary<int, int> curRowsValues = new Dictionary<int, int>();

    public void SetupSprite(int rowID, TextureRect image) {
        rowImages[rowID] = image;
        curRowsValues[rowID] = 0;
    }

    public void SetImageOrientation(bool flipV) {
        foreach (var image in rowImages.Values) image.FlipV = flipV;
    }

    public void ResetSprites() {
        newSprites.Clear();
        foreach (var id in rowImages.Keys) ClearSprite(id);
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

    async void ProcessSprite() {
        if (loadingSprite) return;
        var done = false;
        while (!done && newSprites.Count > 0) {
            var newSprite = newSprites.Dequeue();
            if (curRowsValues[newSprite.ID] == newSprite.newVal) {
                loadingSprite = true;
                try {
                    var texture = thumbnailProvider != null
                        ? await thumbnailProvider(newSprite.newImg)
                        : UILayout.LoadPreview(newSprite.newImg);
                    if (IsInstanceValid(this) && IsInsideTree() && curRowsValues[newSprite.ID] == newSprite.newVal
                        && IsInstanceValid(rowImages[newSprite.ID])) rowImages[newSprite.ID].Texture = texture;
                } catch (System.Exception e) {
                    GameEngine.LogWarning("Unable to load thumbnail: " + e.Message);
                } finally {
                    loadingSprite = false;
                }
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
