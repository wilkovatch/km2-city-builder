using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SM = StringManager;

namespace EditorPanels {
    namespace Buildings {
        public class BlockEditorPanel : EditorPanel {
            public BuildingSideGenerator curSide = null;
            public int curBlock;
            public EditorPanelElements.TextureField tex;
            CityElements.Types.Buildings.BuildingSideType sideType;

            public BlockEditorPanel() {
                AddComplexElement(new PresetSelector(this));
                AddComplexElement(new TypeSelector<CityElements.Types.Runtime.Buildings.BlockType>(this));
            }

            TypeSelector<CityElements.Types.Runtime.Buildings.BlockType> TS() {
                return GetComplexElement<TypeSelector<CityElements.Types.Runtime.Buildings.BlockType>>();
            }

            public override void Initialize(GameObject canvas) {
                if (canvas != null) lastCanvas = canvas;
                var side = GetSide();
                if (side == null) return;
                sideType = side.GetSideType().typeData;
                InitializeWithCustomParameters<CityElements.Types.Runtime.Buildings.BlockType, CityElements.Types.Buildings.BlockType>(lastCanvas, GetBlock, TS,
                    null, GetBuildingBlockTypes, ProcessCustomParts, true, 1.5f, false);
            }

            public Dictionary<string, CityElements.Types.Runtime.Buildings.BlockType> GetBuildingBlockTypes(bool reload = false) {
                var tmp = CityElements.Types.Parsers.TypeParser.GetBuildingBlockTypes(reload);
                var res = new Dictionary<string, CityElements.Types.Runtime.Buildings.BlockType>();
                foreach (var elem in tmp) {
                    if (System.Array.IndexOf(sideType.settings.blockTypes, elem.Key) != -1) {
                        res[elem.Key] = elem.Value;
                    }
                }
                return res;
            }

            bool ProcessCustomParts(CityElements.Types.TabElement elem, EditorPanelPage p, PresetSelector pS,
                TypeSelector<CityElements.Types.Runtime.Buildings.BlockType> tS, CityElements.Types.Runtime.Buildings.BlockType type) {

                var w = elem.width;
                if (elem.name == "mainGroup") {
                    if (!pS.EditingPreset()) {
                        p.AddButton(SM.Get("END_EDITING"), Terminate, w * 0.5f);
                        p.AddButton(SM.Get("BS_FB_GO_UP"), GoUp, w * 0.5f);
                    } else {
                        p.AddButton(SM.Get("END_EDITING"), Terminate, w);
                    }
                    p.IncreaseRow();

                    tS.AddTypeDropdown(p, SM.Get("BS_B_TYPE"), w);

                    System.Func<ObjectState> getter = delegate { return GetBlock(); };
                    System.Func<ObjectState, ObjectState> setter = x => { SetBlock((ObjectState)x.Clone()); return GetBlock(); };
                    pS.AddPresetLoadAndSaveDropdown(p, SM.Get("BS_B_PRESET"), true, "buildingBlock", setter, getter, false, null, null, null, w);

                    p.AddFieldInputField(SM.Get("BS_B_NAME"), SM.Get("BS_B_NAME_PH"), UnityEngine.UI.InputField.ContentType.Standard, GetBlock, "properties.listName", null, w);
                    p.IncreaseRow();
                } else {
                    return false;
                }
                return true;
            }

            public override void Terminate() {
                keepActive = false;
                SetActive(false);
                parentPanel.Terminate();
            }

            public override void SetActive(bool active) {
                if (curSide != null) {
                    curSide.SetOutline(active);
                }
                base.SetActive(active);
            }

            BuildingSideGenerator GetSide() {
                return curSide;
            }

            ObjectState GetBlock() {
                return curSide.State.Array<ObjectState>("blockDict")[curBlock];
            }

            void SetBlock(ObjectState state) {
                curSide.State.Array<ObjectState>("blockDict")[curBlock].ReplaceWith(state);
            }

            public override void BaseExtraAction<T>(T p, System.Action<T> a) {
                curSide.State.FlagAsChanged();
                base.BaseExtraAction(p, a);
            }

            public void ReloadState() {
                TS().SetState(GetBlock(), SetBlock);
            }
        }
    }
}