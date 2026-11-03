using System;
using System.Collections.Generic;
using System.Linq;

namespace CityElements.Types {
    //The authored shape belongs to the custom element; generated instances are derived, never saved as city meshes.
    public class PropPlacerSettings {
        public string type;
        public PropPlacerStrip[] strips;
        public int maxModels; //0: unlimited, 1: one model
        public bool density = true;
        public Parameter[] modelParameters;
        public string placement; //optional Lua callback (variables, context) -> placements
        public bool projectToGround = true;
        public int projectionMask = (1 << 3) | (1 << 7);
        public bool meshTransform;
        public bool autoYOffset = true;
        public float guideHeightOffset = 0.05f;

        public void Initialize(CustomElementType element) {
            var settings = element.settings;
            if (type != "spline" && type != "polygon") throw new Exception("Unknown prop placer shape: " + type);
            if (settings.point != null || settings.meshInstance != null || settings.mesh != null || settings.geometry != null
                || settings.container != null || settings.derivedPoints != null) throw new Exception("Prop placers cannot also define point, meshInstance, mesh, geometry, container or derivedPoints");
            if ((settings.controlPoints?.Length ?? 0) > 0) throw new Exception("Prop placer control points are defined by its shape");
            var parameters = new List<Parameter>(element.parameters.parameters ?? Array.Empty<Parameter>());
            void Add(Parameter p) { if (parameters.Any(x => x.fullName() == p.name)) throw new Exception("Reserved prop placer parameter: " + p.name); parameters.Add(p); }
            Add(new Parameter { name = "projectToGround", label = "PP_PROJECT", type = "bool" });
            if (type == "spline") {
                if (settings.spline == null) throw new Exception("Spline prop placers need spline settings");
                if (strips == null || strips.Length == 0 || strips.Select(x => x.name).Distinct().Count() != strips.Length
                    || strips.Any(x => string.IsNullOrWhiteSpace(x.name) || x.name.Contains('.'))) throw new Exception("Prop placer strips need unique names");
                settings.propsContainers = strips.Select(x => x.name).ToArray();
                //the fields are shown in the panel of the element: its presets save them, and a second strip would have the same fields
                if (strips.Any(x => x.inline) && (strips.Length != 1 || !settings.presets)) throw new Exception("An inline prop placer strip needs to be the only one of a type with presets");
            } else {
                if (settings.spline != null) throw new Exception("Polygon prop placers cannot define spline settings");
                if (maxModels < 0) throw new Exception("maxModels cannot be negative");
                settings.controlPoints = new[] { new CustomElementPointNode { name = "perimeter", label = "PP_PERIMETER",
                    array = new CustomElementArraySettings { min = 0, closed = true } } };
                Add(new Parameter { name = "seed", label = "PP_SEED", type = "int" });
                var fields = new List<Parameter> { new Parameter { name = "mesh", label = "PROP_ELEM_MESH", type = "mesh" } };
                if (density) fields.Add(new Parameter { name = "density", label = "PP_DENSITY", tooltip = "PP_DENSITY_TOOLTIP", type = "float", defaultValueInArray = 1 });
                fields.Add(new Parameter { name = "minYaw", label = "PP_MIN_YAW", type = "float", defaultValueInArray = 0 });
                fields.Add(new Parameter { name = "maxYaw", label = "PP_MAX_YAW", type = "float", defaultValueInArray = 360 });
                foreach (var p in modelParameters ?? Array.Empty<Parameter>()) {
                    if (fields.Any(x => x.fullName() == p.fullName())) throw new Exception("Reserved prop model parameter: " + p.name);
                    fields.Add(p);
                }
                Add(new Parameter { name = "models", label = "PP_MODELS", type = "array", arrayProperties = new ArrayProperties {
                    maxElements = maxModels, elementLabel = "mesh", elementProperties = fields.ToArray() } });
            }
            element.parameters.parameters = parameters.ToArray();
        }
    }

    public class PropPlacerStrip {
        public string name, label;
        public string start = "0", end = "0"; //signed lateral offsets, expressions over the element's parameters
        public string[] allowedContainerTypes;
        public bool inline; //the type and the fields of the props are in the panel of the element, always enabled
        public string[] boundsParameters; //the parameters of the element giving the bounds: hidden with a props type with lineOnly
    }
}
