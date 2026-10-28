using System.Collections.Generic;
using System.Linq;
using Godot;
using Newtonsoft.Json.Linq;

namespace Api {
    public partial class CityApi {
        //Public ribbon shape: {name: [[{start: pointTarget, end: pointTarget, reference: pointTarget?}, ...], ...]}.
        void SetPropRibbons(PropPath element, JObject ribbons) {
            if (ribbons == null || !ribbons.Properties().Any()) throw ApiException.InvalidValue("at least one ribbon is required");
            var names = new List<string>();
            var items = new JArray();
            bool reference = false;
            var referenceSegments = new List<string>();
            foreach (var ribbon in ribbons.Properties()) {
                if (string.IsNullOrWhiteSpace(ribbon.Name) || ribbon.Name.Contains('.')) throw ApiException.InvalidValue("invalid ribbon name");
                if (ribbon.Value is not JArray segments || segments.Count == 0) throw ApiException.InvalidValue("a ribbon needs segments");
                names.Add(ribbon.Name);
                var output = new JArray();
                foreach (var segment in segments) {
                    if (segment is not JArray sections || sections.Count < 2) throw ApiException.InvalidValue("a segment needs at least two sections");
                    var copy = (JArray)sections.DeepClone();
                    var count = copy.Count(s => s["reference"] != null);
                    if (count != 0 && count != copy.Count) throw ApiException.InvalidValue("a distance reference must cover the whole segment");
                    if (count > 0) referenceSegments.Add((names.Count - 1) + "." + output.Count);
                    foreach (JObject section in copy) {
                        if (section["start"] == null || section["end"] == null) throw ApiException.InvalidValue("a section needs start and end points");
                        reference |= section["reference"] != null;
                        section["reference"] ??= section["start"].DeepClone();
                    }
                    output.Add(new JObject { ["sections"] = copy });
                }
                items.Add(new JObject { ["segments"] = output });
            }
            History.UndoManager.Touch(element);
            element.state.SetArray("ribbonNames", names.ToArray());
            element.state.SetBool("useDistanceReference", reference);
            element.state.SetArray("referenceSegments", referenceSegments.ToArray());
            element.SetPoints(LowLevelPoints(element, new JObject { ["ribbons"] = items }, Vector3.Zero));
            element.state.FlagAsChanged();
        }

        [ApiMethod("propPaths.create", "Creates a standalone prop rule with named segmented ribbons and shared/anchored point targets")]
        public int CreatePropPath(JObject ribbons, JObject rule = null, string name = null) {
            return ApplyChange("create prop rule", () => {
                var element = (PropPath)M.CreateLowLevelElement(PropPath.TypeId, name);
                SetPropRibbons(element, ribbons);
                element.state.SetState("propRule", rule?.ToObject<ObjectState>() ?? new ObjectState());
                Operations.Props.NormalizeRule(element.state.State("propRule"));
                return IdOf(element);
            });
        }

        [ApiMethod("propPaths.setRibbons", "Replaces the ribbons of a standalone prop rule")]
        public void SetPropPathRibbons(ElementRef element, JObject ribbons) {
            ApplyChange("set prop ribbons", () => SetPropRibbons(Resolve<PropPath>(element, "propPath"), ribbons));
        }

        [ApiMethod("propPaths.setRule", "Replaces the procedural rule, keeping its ribbon geometry")]
        public void SetPropPathRule(ElementRef element, JObject rule) {
            ApplyChange("set prop rule", () => {
                var e = Resolve<PropPath>(element, "propPath");
                History.UndoManager.Touch(e);
                e.state.SetState("propRule", rule?.ToObject<ObjectState>() ?? new ObjectState());
                Operations.Props.NormalizeRule(e.state.State("propRule"));
                e.state.FlagAsChanged();
            });
        }

        [ApiMethod("propPaths.insert", "Inserts a segment or paired section at an array path")]
        public object InsertPropPathSection(ElementRef element, string array, int? index = null, Vector3? position = null)
            => InsertLowLevel(element, array, index, position);

        [ApiMethod("propPaths.remove", "Removes a segment or paired section at an array path")]
        public void RemovePropPathSection(ElementRef element, string array, int index) => RemoveLowLevel(element, array, index);

        [ApiMethod("propPaths.renameRibbon", "Renames a ribbon and its prop container together")]
        public void RenamePropRibbon(ElementRef element, int index, string name) {
            ApplyChange("rename prop ribbon", () => Resolve<PropPath>(element, "propPath").RenameRibbon(index, name));
        }

        [ApiMethod("propPaths.removeRibbon", "Removes a ribbon and its rule container")]
        public void RemovePropRibbon(ElementRef element, int index) {
            ApplyChange("remove prop ribbon", () => Resolve<PropPath>(element, "propPath").RemoveRibbon(index));
        }

        [ApiMethod("propPaths.move", "Moves a standalone prop rule, respecting shared points and anchors")]
        public int MovePropPath(ElementRef element, Vector3? offset = null, Vector3? position = null) => MoveLowLevel(element, offset, position);

        [ApiMethod("propPaths.delete", "Deletes a standalone prop rule and its generated props")]
        public void DeletePropPath(ElementRef element) => DeleteLowLevel(element);

        [ApiMethod("propPaths.movePoint", "Moves a ribbon point, using its path from elements.get")]
        public void MovePropPathPoint(ElementRef element, string path, Vector3 position) => MoveLowLevelPoint(element, position, path);

        [ApiMethod("propPaths.attachPoint", "Shares or anchors a ribbon point")]
        public int AttachPropPathPoint(ElementRef element, string path, PointTarget to = null, int[] line = null, int? column = null)
            => AttachLowLevelPoint(element, to, path, null, line, column);

        [ApiMethod("propPaths.detachPoint", "Detaches a ribbon point")]
        public int DetachPropPathPoint(ElementRef element, string path) => DetachLowLevelPoint(element, path);
    }
}
