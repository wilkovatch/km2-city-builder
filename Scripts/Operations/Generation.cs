using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Operations {
    //Runs the generators of the core (see GeneratorCatalog) on the elements they're enabled for, in the background: when
    //the input of an element (its export JSON with the elements around it, as for the converters) changed and then stayed
    //the same for a moment (so not during a drag), the script makes the files in a cache folder of the program and returns
    //values (e.g. their paths), given to the geometry scripts of the custom elements as ctx.generated. The values are not
    //saved and not undone: they follow the inputs. The exporters make the real files themselves (see the Core Generators
    //page of the wiki)
    public static class Generation {
        const double CheckInterval = 0.25; //seconds between the checks of the inputs
        const double StableTime = 0.5; //seconds the input must stay the same before running

        class Entry {
            public string seenHash; //the input as last seen, since seenAt
            public double seenAt;
            public string runHash; //the input of the last run started
            public string appliedHash; //the input of the values
            public JObject values;
            public string error;
        }

        //by generator, then by element
        static readonly Dictionary<string, Dictionary<object, Entry>> entries = new();
        static readonly HashSet<string> running = new();
        static double now, nextCheck;
        static int checkedUpdate = -1;
        static int epoch; //increased by Reset: the runs started before it are ignored
        static string cacheFolder; //the folder of the city the files are in (null: not created yet)
        static ElementManager lastManager;

        //the folder of the generated files of the current city: in the temporary folder, never in the city
        public static string CacheFolder() {
            var city = Path.GetFullPath(PreferencesManager.workingDirectory).ToLowerInvariant();
            var hash = System.Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(city)));
            return Path.Combine(Path.GetTempPath(), "km2cb_generated", hash.Substring(0, 12).ToLowerInvariant());
        }

        //forgets everything and removes the files (when a city is erased or loaded)
        public static void Reset() {
            epoch++;
            entries.Clear();
            running.Clear();
            checkedUpdate = -1;
            GeneratorCatalog.Reset();
            if (cacheFolder != null) Delete(cacheFolder);
            cacheFolder = null;
        }

        static void Delete(string folder) {
            try {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            } catch (System.Exception) {
                //a file still in use: it's in the temporary folder anyway
            }
        }

        // Values

        //the values of the generators for the element (merged), with "error" if one failed, null if none applies
        public static JObject Values(object element) {
            JObject res = null;
            foreach (var byElement in entries.Values) {
                if (!byElement.TryGetValue(element, out var entry) || (entry.values == null && entry.error == null)) continue;
                res ??= new JObject();
                if (entry.values != null) res.Merge(entry.values);
                if (entry.error != null) res["error"] = entry.error;
            }
            return res;
        }

        //true if a generator enabled for the element hasn't given the values of its current input yet (its input is read
        //again: the scheduler sees the changes only at its next check)
        public static bool IsPending(object element) {
            foreach (var generator in GeneratorCatalog.For(element)) {
                var enabled = generator.IsEnabled(element);
                if (!entries.TryGetValue(generator.name, out var byElement) || !byElement.TryGetValue(element, out var entry)) {
                    if (enabled) return true;
                    continue;
                }
                //values of a disabled generator: until the next check removes them
                if (!enabled || running.Contains(generator.name) || entry.appliedHash != entry.seenHash) return true;
                if (lastManager != null && Hash(lastManager, generator, element) != entry.appliedHash) return true;
            }
            return false;
        }

        //for the scripting API: the values, the error and whether new ones are coming (null if no generator applies)
        public static JObject Describe(object element) {
            if (GeneratorCatalog.For(element).Count == 0) return null;
            var values = Values(element) ?? new JObject();
            return new JObject { ["values"] = values, ["pending"] = IsPending(element) };
        }

        // Scheduling

        //called every frame, after the update of the city
        public static void Tick(ElementManager manager, double delta) {
            now += delta;
            lastManager = manager;
            if (now < nextCheck || PreferencesManager.workingDirectory == "" || History.UndoManager.IsLoading) return;
            nextCheck = now + CheckInterval;
            var generators = GeneratorCatalog.List();
            if (generators.Count == 0) return;
            var changed = manager.cityUpdateCount != checkedUpdate;
            checkedUpdate = manager.cityUpdateCount;
            foreach (var generator in generators) {
                if (!entries.TryGetValue(generator.name, out var byElement)) entries[generator.name] = byElement = new Dictionary<object, Entry>();
                if (changed) Refresh(manager, generator, byElement);
                if (running.Contains(generator.name)) continue;
                var due = byElement.Where(p => p.Value.seenHash != p.Value.runHash && now - p.Value.seenAt >= StableTime).Select(p => p.Key).ToList();
                if (due.Count > 0) Run(manager, generator, byElement, due);
            }
        }

        static IEnumerable<object> Elements(ElementManager manager) {
            foreach (var e in manager.roads) yield return e;
            foreach (var e in manager.intersections) yield return e;
            foreach (var e in manager.patches) yield return e;
            foreach (var e in manager.buildings) yield return e;
            foreach (var e in manager.customElements) yield return e;
        }

        //the inputs of the elements, after a change in the city
        static void Refresh(ElementManager manager, GeneratorCatalog.GeneratorInfo generator, Dictionary<object, Entry> byElement) {
            var enabled = new HashSet<object>();
            foreach (var element in Elements(manager)) {
                if (!generator.Accepts(element) || !generator.IsEnabled(element)) continue;
                enabled.Add(element);
                var hash = Hash(manager, generator, element);
                if (!byElement.TryGetValue(element, out var entry)) byElement[element] = entry = new Entry();
                if (entry.seenHash != hash) {
                    entry.seenHash = hash;
                    entry.seenAt = now;
                }
            }
            foreach (var element in byElement.Keys.Where(e => !enabled.Contains(e)).ToList()) {
                var hadValues = byElement[element].values != null || byElement[element].error != null;
                byElement.Remove(element);
                if (hadValues) Invalidate(manager, element);
            }
        }

        //the input without what the generated values change (the meshes and props of the elements): the same city gives
        //the same hash also after the values are shown
        static readonly HashSet<string> GeometryKeys = new() { "mesh", "collider", "propLines", "meshDict", "paramMeshes", "facades", "materialDict" };

        static void StripGeometry(JToken token) {
            if (token is JObject obj) {
                foreach (var p in obj.Properties().ToList()) {
                    if (GeometryKeys.Contains(p.Name)) p.Remove();
                    else StripGeometry(p.Value);
                }
            } else if (token is JArray array) {
                foreach (var item in array) StripGeometry(item);
            }
        }

        static string Hash(ElementManager manager, CoreElementTool generator, object element) {
            var input = Conversion.BuildInput(manager, generator, new List<object> { element }, "generate");
            StripGeometry(input);
            var bytes = System.Text.Encoding.UTF8.GetBytes(input.ToString(Newtonsoft.Json.Formatting.None));
            return System.Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(bytes));
        }

        static void Invalidate(ElementManager manager, object element) {
            if (element is CustomElement custom) custom.InvalidateGenerated();
            manager.FlagAsChanged();
        }

        //runs the generator on the elements: its input is the one of the converters, with "generate" (the elements) and
        //"outputFolder"; its output {"results": [{"element": {kind, id}, "values": {...}, "error": "<string key>"}]}
        static async void Run(ElementManager manager, GeneratorCatalog.GeneratorInfo generator, Dictionary<object, Entry> byElement, List<object> elements) {
            var runEpoch = epoch;
            running.Add(generator.name);
            var hashes = new Dictionary<object, string>();
            foreach (var e in elements) hashes[e] = byElement[e].runHash = byElement[e].seenHash;
            var tempDir = Path.Combine(Path.GetTempPath(), "km2cb_generate_" + System.Guid.NewGuid().ToString("N"));
            try {
                var folder = CacheFolder();
                if (cacheFolder != folder) {
                    Delete(folder); //left by a session that didn't end
                    cacheFolder = folder;
                }
                Directory.CreateDirectory(folder);
                Directory.CreateDirectory(tempDir);
                var input = Conversion.BuildInput(manager, generator, elements, "generate");
                input["outputFolder"] = folder;
                var inputPath = Path.Combine(tempDir, "input.json");
                var outputPath = Path.Combine(tempDir, "output.json");
                File.WriteAllText(inputPath, input.ToString(Newtonsoft.Json.Formatting.None));
                var pythonFolder = Path.Combine(PathHelper.FilesPath(true), "python");
                var (code, data) = await PythonManager.RunScriptTask(generator.ScriptPath, true, new List<string> { pythonFolder, inputPath, outputPath });
                if (runEpoch != epoch) return;
                JArray results = null;
                string failure = null;
                if (code != 0 || !File.Exists(outputPath)) {
                    failure = string.IsNullOrEmpty(data) ? "the generator wrote no output" : data;
                } else {
                    try {
                        results = JObject.Parse(File.ReadAllText(outputPath))["results"] as JArray;
                    } catch (System.Exception e) {
                        failure = "the output of the generator is not valid JSON: " + e.Message;
                    }
                }
                var byId = elements.ToDictionary(e => (Conversion.ElementKind(e), History.ElementRegistry.GetId(e)));
                var given = new Dictionary<object, (JObject values, string error)>();
                if (failure == null) {
                    foreach (var result in results ?? new JArray()) {
                        var reference = result["element"];
                        if (reference == null || !byId.TryGetValue(((string)reference["kind"], (int?)reference["id"] ?? -1), out var element)) continue;
                        given[element] = (result["values"] as JObject ?? new JObject(), (string)result["error"]);
                    }
                }
                foreach (var element in elements) {
                    //changed or disabled meanwhile: the next check runs it again
                    if (!byElement.TryGetValue(element, out var entry) || entry.seenHash != hashes[element]) continue;
                    var (values, error) = given.TryGetValue(element, out var r) ? r : (new JObject(), failure ?? "the generator gave no result");
                    entry.appliedHash = hashes[element];
                    if (JToken.DeepEquals(entry.values, values) && entry.error == error) continue;
                    entry.values = values;
                    entry.error = string.IsNullOrEmpty(error) ? null : error;
                    //once per change of the result, as the errors of the scripts
                    if (entry.error != null) {
                        var elementName = element is Godot.Node3D node ? GameEngine.GameObject.GetName(node) : Conversion.ElementKind(element);
                        GameEngine.LogError("Generator " + generator.name + ", " + elementName + ": " + StringManager.Get(entry.error));
                    }
                    Invalidate(manager, element);
                }
            } catch (System.Exception e) {
                GameEngine.LogError("Generator " + generator.name + ": " + e.Message);
            } finally {
                if (runEpoch == epoch) running.Remove(generator.name);
                try {
                    Directory.Delete(tempDir, true);
                } catch (System.Exception) { }
            }
        }
    }
}
