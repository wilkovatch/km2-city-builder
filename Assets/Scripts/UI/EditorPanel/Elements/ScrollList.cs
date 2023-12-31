using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace EditorPanelElements {
    public class ScrollList : EditorPanelElement {
        System.Action<int> action;
        GameObject template, imageTemplate, startPadding, endPadding;
        GameObject content;
        RectTransform contentRt;
        LayoutElement startPaddingLE, endPaddingLE;
        List<string> strings = new List<string>();
        List<string> images = new List<string>();
        List<Color> colors = new List<Color>();
        List<(GameObject row, int rowID)> rows = new List<(GameObject, int)>();
        Dictionary<int, int> curRowsValues = new Dictionary<int, int>();
        Color pressedCol = new Color(145 / 255.0f, 201 / 255.0f, 247 / 255.0f);
        int selected = -1;
        float height;
        float rowHeight = 25.0f;
        int paddingRows = 4;
        int lastFirstIndex = -1;
        ScrollListComponent scrl;
        bool readDefaultColor = false;
        Color defaultColor;
        string emptyLabel = "EMPTY";

        protected override string TemplateName() {
            return "ScrollList";
        }

        void Setup(System.Action<int> action, bool withImage, string emptyLabel) {
            this.action = action;
            if (emptyLabel != null) this.emptyLabel = emptyLabel;
            scrl = obj.GetComponent<ScrollListComponent>();
            scrl.list = this;
            content = obj.transform.Find("Scroll View").Find("Viewport").Find("Content").gameObject;
            contentRt = content.GetComponent<RectTransform>();
            template = content.transform.Find("Item").gameObject;
            template.SetActive(false);
            imageTemplate = content.transform.Find("ImageItem").gameObject;
            imageTemplate.SetActive(false);
            startPadding = content.transform.Find("StartPadding").gameObject;
            endPadding = content.transform.Find("EndPadding").gameObject;
            startPaddingLE = startPadding.GetComponent<LayoutElement>();
            endPaddingLE = endPadding.GetComponent<LayoutElement>();
            height = 225; //TODO: get actual value if it changes
            for (float p = 0; p < height + 2 * paddingRows * rowHeight; p += rowHeight) {
                CreateRow(withImage);
            }
            endPadding.transform.SetAsLastSibling();
            if (withImage) scrl.SetupSprites(rows, true);
        }

        public ScrollList(string title, List<string> items, System.Action<int> action, GameObject parent, Vector2 pos, float widthFactor = 1.0f, string tooltip = null, string tag = null, string emptyLabel = null)
        : base(title, parent, pos, widthFactor, tooltip, tag) {
            Setup(action, false, emptyLabel);
            SetItems(items);
        }

        public ScrollList(string title, List<string> items, List<string> images, System.Action<int> action, GameObject parent, Vector2 pos, float widthFactor = 1.0f, string tooltip = null, string tag = null, string emptyLabel = null)
        : base(title, parent, pos, widthFactor, tooltip, tag) {
            Setup(action, true, emptyLabel);
            SetItems(items, images);
        }

        public void ManualScroll(bool next) {
            if (selected == -1) return;
            bool changed = false;
            if (next) {
                if (selected < strings.Count - 1) {
                    SelectItemManual(selected + 1);
                    changed = true;
                }
            } else {
                if (selected > 0) {
                    SelectItemManual(selected - 1);
                    changed = true;
                }
            }
            if (changed) {
                var oldPos = contentRt.localPosition;
                var contTopY = oldPos.y;
                var contBottomY = contTopY + height;
                var itemTopY = selected * rowHeight;
                var itemBottomY = itemTopY + rowHeight;
                if (itemTopY < contTopY) {
                    contentRt.localPosition = new Vector3(oldPos.x, itemTopY, oldPos.z);
                } else if (itemBottomY > contBottomY) {
                    contentRt.localPosition = new Vector3(oldPos.x, itemBottomY - height, oldPos.z);
                }
                UpdateRows();
            }
        }

        void CreateRow(bool withImage) {
            int rowID = rows.Count;
            var newRow = Object.Instantiate(withImage ? imageTemplate : template, template.transform.parent);
            newRow.GetComponent<UnityEngine.UI.Button>().onClick.AddListener(delegate {
                SelectItem(rowID);
            });
            newRow.SetActive(true);
            rows.Add((newRow, rowID));
            curRowsValues.Add(rowID, -1);
        }

        void SetRow(int row, int index) {
            if (index < 0 || index > strings.Count - 1) {
                if (rows[row].row.activeSelf) rows[row].row.SetActive(false);
            } else {
                if (!rows[row].row.activeSelf) rows[row].row.SetActive(true);
                var txt = rows[row].row.transform.Find("Text").GetComponent<Text>();
                if (!readDefaultColor) {
                    readDefaultColor = true;
                    defaultColor = txt.color;
                }
                if (strings[index] == null || strings[index] == "") {
                    txt.text = StringManager.Get(emptyLabel);
                    txt.color = Color.grey;
                } else {
                    txt.text = strings[index];
                    txt.color = defaultColor;
                }
                if (colors.Count > 0) {
                    txt.color = colors[rows[row].rowID];
                }
                rows[row].row.GetComponent<UnityEngine.UI.Image>().color = selected == index ? pressedCol : Color.white;
                if (images.Count > 0) {
                    scrl.EnqueueNewSprite(rows[row].rowID, images[index], strings[index]);
                }
                curRowsValues[rows[row].rowID] = index;
            }
        }

        int GetRowID(int index) {
            if (index == -1) return -1;
            for (int i = 0; i < curRowsValues.Count; i++) {
                if (curRowsValues[i] == index) {
                    return i;
                }
            }
            return -1;
        }

        public void Deselect() {
            if (selected != -1) {
                var ID = GetRowID(selected);
                if (ID != -1) rows.Find(x => x.rowID == ID).row.GetComponent<UnityEngine.UI.Image>().color = Color.white;
                selected = -1;
            }
        }

        void SelectItemManual(int index) {
            Deselect();
            var rowID = GetRowID(index);
            if (rowID != -1) {
                var button = rows.Find(x => x.rowID == rowID).row;
                button.GetComponent<UnityEngine.UI.Image>().color = pressedCol;
            }
            selected = index;
            if (actionEnabled) action.Invoke(index);
        }

        public override void SetValue(object value) {
            if (value is int) {
                SelectItemManual((int)value);
            }
        }

        void SelectItem(int rowID) {
            if (rowID == -1) return;
            Deselect();
            var button = rows.Find(x => x.rowID == rowID).row;
            button.GetComponent<UnityEngine.UI.Image>().color = pressedCol;
            selected = curRowsValues[rowID];
            action.Invoke(curRowsValues[rowID]);
        }

        public void SetItems(List<string> newItems, List<string> newImages = null, List<Color> newColors = null) {
            strings.Clear();
            images.Clear();
            colors.Clear();
            strings.AddRange(newItems);
            if (newImages != null) images.AddRange(newImages);
            if (newColors != null) colors.AddRange(newColors);
            lastFirstIndex = -1;
            UpdateRows();
        }

        public void UpdateRows() {
            var pos = contentRt.localPosition.y;
            int firstIndex = (int)(pos / rowHeight) - paddingRows;
            if (firstIndex == lastFirstIndex) return;
            int lastIndex = firstIndex + (int)(height / rowHeight) + 2 * paddingRows - 1;
            if (firstIndex < 0) {
                var diff = -firstIndex;
                firstIndex = 0;
                lastIndex += diff;
            } else if (lastIndex >= strings.Count) {
                var diff = lastIndex - (strings.Count - 1);
                lastIndex = strings.Count - 1;
                firstIndex -= diff;
            }
            startPaddingLE.preferredHeight = firstIndex * rowHeight;
            endPaddingLE.preferredHeight = (strings.Count - 1 - lastIndex) * rowHeight;
            if (lastFirstIndex == -1) {
                var curRow = 0;
                for (int i = firstIndex; i <= lastIndex; i++) {
                    SetRow(curRow, i);
                    curRow++;
                }
            } else {
                var delta = firstIndex - lastFirstIndex;
                if (delta > 0) {
                    for (var i = 0; i < delta; i++) {
                        SetRow(0, lastIndex - delta + i + 1);
                        var row = rows[0];
                        rows.RemoveAt(0);
                        row.row.transform.SetAsLastSibling();
                        rows.Add(row);
                    }
                    endPadding.transform.SetAsLastSibling();
                } else {
                    delta = -delta;
                    for (var i = 0; i < delta; i++) {
                        SetRow(rows.Count - 1, firstIndex + delta - i - 1);
                        var row = rows[rows.Count - 1];
                        rows.RemoveAt(rows.Count - 1);
                        row.row.transform.SetAsFirstSibling();
                        rows.Insert(0, row);
                    }
                    startPadding.transform.SetAsFirstSibling();
                }
            }
            lastFirstIndex = firstIndex;
        }
    }
}