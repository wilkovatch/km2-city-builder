using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace IO {
    //Sets the default JSON settings at startup:
    //enums are serialized by name (e.g. "Intersection" instead of 2), which the core python scripts rely on
    static class JsonDefaults {
#pragma warning disable CA2255 //runs before any other code
        [ModuleInitializer]
        internal static void Init() {
#pragma warning restore CA2255
            JsonConvert.DefaultSettings = () => new JsonSerializerSettings {
                Converters = { new StringEnumConverter(), new VersionConverter() }
            };
        }
    }
}
