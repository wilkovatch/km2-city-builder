using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ScrollListComponent : MonoBehaviour {
    public EditorPanelElements.ScrollList list = null;

    Queue<(int ID, string newImg, string newText, int newVal)> newSprites = new Queue<(int, string, string, int)>();
    Dictionary<int, Image> rowImages = new Dictionary<int, Image>();
    Dictionary<int, int> curRowsValues = new Dictionary<int, int>();

    public void SetupSprites(List<(GameObject row, int rowID)> rows, bool checkerboard = false) {
        foreach(var item in rows) {
            var img = item.row.transform.Find("Image").GetComponent<Image>();
            rowImages.Add(item.rowID, img);
            curRowsValues.Add(item.rowID, 0);
            if (checkerboard) {
                var bgImg = item.row.transform.Find("checkerboard").GetComponent<Image>();
                bgImg.sprite = MaterialManager.GetInstance().GetCheckerboard();
            }
        }
    }

    void ClearSprite(int ID) {
        var image = rowImages[ID];
        if (image.sprite != null) {
            Destroy(image.sprite.texture);
            Destroy(image.sprite);
            image.sprite = null;
        }
    }

    public void EnqueueNewSprite(int ID, string image, string text) {
        var newVal = curRowsValues[ID] + 1;
        newSprites.Enqueue((ID, image, text, newVal));
        curRowsValues[ID] = newVal;
        ClearSprite(ID);
    }

    void ProcessSprite() {
        var done = false;
        while (!done && newSprites.Count > 0) {
            var newSprite = newSprites.Dequeue();
            if (curRowsValues[newSprite.ID] == newSprite.newVal) {
                rowImages[newSprite.ID].sprite = TextureImporter.GetSprite(newSprite.newImg);
                rowImages[newSprite.ID].gameObject.transform.parent.Find("Text").GetComponent<Text>().text = newSprite.newText;
                done = true;
            }
        }
    }

    private void Update() {
        if (list == null) return;
        list.UpdateRows();
        ProcessSprite();
        if (Input.GetKeyDown(KeyCode.DownArrow)) {
            list.ManualScroll(true);
        } else if (Input.GetKeyDown(KeyCode.UpArrow)) {
            list.ManualScroll(false);
        }
    }
}

