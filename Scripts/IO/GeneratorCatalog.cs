using System.Collections.Generic;

//The generators of the current core (its generators folder): Python scripts making the files an element needs (e.g. a
//model fitted to the roads it's attached to) while it's edited, in a cache of the program, to show them in the editor
//(see Operations.Generation and the Core Generators page of the wiki). The exporters make the real files themselves.
//Each one is a folder with a generator.json: {"sources": ["customElement/bridge", ...], "types": [...], "script":
//"generate.py", "enabledWhen": "<module>.<function>"}
public static class GeneratorCatalog {
    public class GeneratorInfo : CoreElementTool {
        public GeneratorInfo() {
            script = "generate.py";
        }
    }

    //read once per core: the scheduler checks them at every update
    static List<GeneratorInfo> cached;
    static string cachedFolder;

    public static List<GeneratorInfo> List() {
        var folder = PathHelper.CoreAvailable() ? PathHelper.FilesPath() : null;
        if (cached == null || cachedFolder != folder) {
            cached = CoreElementTool.Load<GeneratorInfo>("generators", "generator.json");
            cachedFolder = folder;
        }
        return cached;
    }

    //read again at the next List (e.g. when a city is loaded, so that changed files are seen)
    public static void Reset() {
        cached = null;
    }

    public static GeneratorInfo Get(string name) {
        return List().Find(g => g.name == name);
    }

    //the generators that accept the element
    public static List<GeneratorInfo> For(object element) {
        return List().FindAll(g => g.Accepts(element));
    }
}
