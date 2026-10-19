using System.Collections.Generic;

namespace Operations {
    //Presets of the city (presets.json), by category ("road", "intersection", "building", ...)
    public static class Presets {
        public const string DefaultName = "Default";

        //the categories whose elements can be synced with their preset and unlinked from it
        static readonly HashSet<string> linkedCategories = new HashSet<string> { "road", "intersection", "buildingLine", "building", "buildingSide", "buildingBlock" };

        static readonly string[] sideStates = { "frontState", "leftState", "rightState", "backState" };

        //a copy of the preset, flagged as changed so that the element using it is rebuilt (null if not found)
        public static ObjectState Get(string category, string name) {
            if (PresetManager.GetPresetByName(name, category) == null) return null;
            return PresetManager.GetPresetCloneByName(name, category);
        }

        //the presets of the category whose type is one of the allowed ones (all of them if none)
        public static List<ObjectState> Filter(string category, List<string> allowedTypes) {
            var list = PresetManager.GetPresets().GetList(category);
            var res = new List<ObjectState>();
            foreach (var preset in list) {
                if (allowedTypes == null || allowedTypes.Count == 0 || allowedTypes.Contains(preset.Str("type"))) res.Add(preset);
            }
            return res;
        }

        //names the state after the preset and saves a copy of it (overwriting a preset with the same name)
        public static void Save(ObjectState state, string category, string name) {
            state.Name = name;
            PresetManager.SavePreset((ObjectState)state.Clone(), category);
        }

        public static bool Exists(string category, string name) {
            return PresetManager.DoesPresetExist(name, category);
        }

        public static bool CanSync(string category) {
            return linkedCategories.Contains(category);
        }

        //deletes the preset (the default one can't be), the elements using it are unlinked from it
        public static bool Delete(ElementManager manager, string category, string name) {
            if (name == null || name == DefaultName || !Exists(category, name)) return false;
            PresetManager.DeletePreset(name, category);
            Unlink(manager, category, name);
            return true;
        }

        //gives the elements using the preset its current values (the elements changed are recorded for undo)
        public static bool Sync(ElementManager manager, string category, string name) {
            var curPreset = PresetManager.GetPresetByName(name, category);
            if (curPreset == null) return false;
            ObjectState Copy(ObjectState _) {
                var newState = (ObjectState)curPreset.Clone();
                newState.FlagAsChanged();
                return newState;
            }
            switch (category) {
                case "road":
                    foreach (var road in manager.roads) {
                        if (road.state.Name == name) {
                            History.UndoManager.Touch(road);
                            road.SetState(Copy(road.state));
                        }
                    }
                    return true;
                case "intersection":
                    foreach (var intersection in manager.intersections) {
                        if (intersection.state.Name == name) {
                            History.UndoManager.Touch(intersection);
                            intersection.SetState(Copy(intersection.state));
                        }
                    }
                    return true;
                case "buildingLine":
                case "building":
                case "buildingSide":
                case "buildingBlock":
                    ChangeLinkedBuildingParts(manager, category, name, Copy);
                    return true;
                default:
                    GameEngine.LogWarning("Syncing presets for the type '" + category + "' is unimplemented");
                    return false;
            }
        }

        static void Unlink(ElementManager manager, string category, string name) {
            switch (category) {
                case "road":
                    foreach (var road in manager.roads) {
                        if (road.state.Name == name) {
                            History.UndoManager.Touch(road);
                            road.state.Name = "";
                        }
                    }
                    break;
                case "intersection":
                    foreach (var intersection in manager.intersections) {
                        if (intersection.state.Name == name) {
                            History.UndoManager.Touch(intersection);
                            intersection.state.Name = "";
                        }
                    }
                    break;
                case "buildingLine":
                case "building":
                case "buildingSide":
                case "buildingBlock":
                    ChangeLinkedBuildingParts(manager, category, name, x => { x.Name = ""; return x; });
                    break;
                default:
                    GameEngine.LogWarning("Clearing presets for the type '" + category + "' is unimplemented");
                    break;
            }
        }

        //replaces the states linked to the preset with the result of change: the lines, the buildings,
        //or the sides and the blocks inside the buildings (the lines changed are recorded for undo)
        static void ChangeLinkedBuildingParts(ElementManager manager, string category, string name, System.Func<ObjectState, ObjectState> change) {
            foreach (var line in manager.buildings) {
                if (category == "buildingLine") {
                    if (line.state.Name == name) {
                        History.UndoManager.Touch(line);
                        BuildingLines.SetLineState(line, change((ObjectState)line.state.Clone()));
                    }
                    continue;
                }
                foreach (var building in line.buildings) {
                    ObjectState newState = null;
                    if (category == "building") {
                        if (building.state.Name == name) newState = change((ObjectState)building.state.Clone());
                    } else {
                        var buildingState = (ObjectState)building.state.Clone();
                        if (ChangeLinkedSideParts(buildingState, category, name, change)) newState = buildingState;
                    }
                    if (newState == null) continue;
                    History.UndoManager.Touch(line);
                    building.SetState(newState);
                }
            }
        }

        //the sides of the building (buildingSide) or their blocks (buildingBlock), true if any was linked to the preset
        static bool ChangeLinkedSideParts(ObjectState buildingState, string category, string name, System.Func<ObjectState, ObjectState> change) {
            var res = false;
            foreach (var key in sideStates) {
                var side = buildingState.State(key);
                if (side == null) continue;
                if (category == "buildingSide") {
                    if (side.Name == name) {
                        buildingState.SetState(key, change(side));
                        res = true;
                    }
                    continue;
                }
                var blocks = side.Array<ObjectState>("blockDict");
                if (blocks == null) continue;
                var newBlocks = (ObjectState[])blocks.Clone();
                var changed = false;
                for (int i = 0; i < newBlocks.Length; i++) {
                    if (newBlocks[i].Name == name) {
                        newBlocks[i] = change(newBlocks[i]);
                        changed = true;
                    }
                }
                if (changed) {
                    side.SetArray("blockDict", newBlocks);
                    res = true;
                }
            }
            return res;
        }
    }
}
